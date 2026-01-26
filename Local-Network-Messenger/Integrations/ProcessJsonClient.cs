using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Integrations
{
    public sealed class ProcessJsonClient : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly JsonSerializerOptions _options;
        private readonly TimeSpan _timeout;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcEnvelope<JsonElement>>> _pending = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Task _readTask;
        private readonly Task _errorTask;

        public ProcessJsonClient(ProcessStartInfo startInfo, JsonSerializerOptions? options = null, TimeSpan? timeout = null)
        {
            _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            };
            _timeout = timeout ?? TimeSpan.FromSeconds(8);

            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.StandardInputEncoding = Encoding.UTF8;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("IPC process could not start.");
            _writer = _process.StandardInput;
            _readTask = Task.Run(ReadLoopAsync);
            _errorTask = Task.Run(DrainErrorAsync);
        }

        public async Task<IpcEnvelope<TResponse>> SendAsync<TRequest, TResponse>(
            string type,
            TRequest payload,
            CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid().ToString("N");
            var envelope = new IpcEnvelope<TRequest>(id, type, payload);
            var json = JsonSerializer.Serialize(envelope, _options);

            var tcs = new TaskCompletionSource<IpcEnvelope<JsonElement>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(id, tcs))
            {
                throw new InvalidOperationException("IPC request could not be tracked.");
            }

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _writer.WriteLineAsync(json);
                await _writer.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }

            var timeoutTask = Task.Delay(_timeout, cancellationToken);
            var completed = await Task.WhenAny(tcs.Task, timeoutTask);
            if (completed != tcs.Task)
            {
                _pending.TryRemove(id, out _);
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw new TimeoutException("IPC request timed out.");
            }

            var response = await tcs.Task;
            var responsePayload = response.Payload.Deserialize<TResponse>(_options);
            if (responsePayload == null)
            {
                throw new InvalidDataException("IPC response payload invalid.");
            }

            return new IpcEnvelope<TResponse>(response.Id, response.Type, responsePayload);
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    IpcEnvelope<JsonElement>? envelope;
                    try
                    {
                        envelope = JsonSerializer.Deserialize<IpcEnvelope<JsonElement>>(line, _options);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    if (envelope == null)
                    {
                        continue;
                    }

                    if (_pending.TryRemove(envelope.Id, out var tcs))
                    {
                        tcs.TrySetResult(envelope);
                    }
                }
            }
            catch (Exception ex)
            {
                FailAll(ex);
                return;
            }

            FailAll(new InvalidOperationException("IPC process terminated."));
        }

        private async Task DrainErrorAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var line = await _process.StandardError.ReadLineAsync();
                    if (line == null)
                    {
                        break;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private void FailAll(Exception exception)
        {
            foreach (var pair in _pending)
            {
                if (_pending.TryRemove(pair.Key, out var tcs))
                {
                    tcs.TrySetException(exception);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                await Task.WhenAll(_readTask, _errorTask);
            }
            catch
            {
            }

            _process.Dispose();
            _cts.Dispose();
            _writeLock.Dispose();
        }
    }
}
