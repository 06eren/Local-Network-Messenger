using System;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Integrations;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed class FileScanProcessService : IFileScanService, IAsyncDisposable
    {
        private readonly Func<ProcessJsonClient> _clientFactory;
        private readonly SemaphoreSlim _clientLock = new(1, 1);
        private ProcessJsonClient? _client;
        private bool _disposed;

        public FileScanProcessService(ProcessJsonClient client, Func<ProcessJsonClient> clientFactory)
        {
            _client = client;
            _clientFactory = clientFactory;
        }

        public async Task<FileScanResult> ScanAsync(FileScanRequest request, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                return new FileScanResult("unknown", "Tarama servisi kapali.", null);
            }

            for (var attempt = 0; attempt < 2; attempt += 1)
            {
                try
                {
                    var client = await GetClientAsync(cancellationToken);
                    var response = await client.SendAsync<FileScanIpcRequest, FileScanIpcResponse>(
                        IpcMessageTypes.FileScanRequest,
                        new FileScanIpcRequest(request.FileName, request.SizeBytes, null),
                        cancellationToken);

                    if (response.Payload.Error != null)
                    {
                        return new FileScanResult(
                            "error",
                            response.Payload.Error.Message,
                            response.Payload.Details);
                    }

                    var status = string.IsNullOrWhiteSpace(response.Payload.Status)
                        ? "unknown"
                        : response.Payload.Status;
                    var details = response.Payload.Details;
                    return new FileScanResult(status, "Tarama tamamlandi.", details);
                }
                catch (Exception ex)
                {
                    if (attempt == 0 && await RestartClientAsync())
                    {
                        continue;
                    }

                    return new FileScanResult(
                        "unknown",
                        "Tarama servisi kapali. Dosya gonderimine devam edildi.",
                        ex.Message);
                }
            }

            return new FileScanResult("unknown", "Tarama servisi kapali.", null);
        }

        private async Task<ProcessJsonClient> GetClientAsync(CancellationToken cancellationToken)
        {
            await _clientLock.WaitAsync(cancellationToken);
            try
            {
                if (_client == null)
                {
                    _client = _clientFactory();
                }

                return _client;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        private async Task<bool> RestartClientAsync()
        {
            await _clientLock.WaitAsync();
            try
            {
                if (_client != null)
                {
                    await _client.DisposeAsync();
                }

                _client = _clientFactory();
                return true;
            }
            catch
            {
                _client = null;
                return false;
            }
            finally
            {
                _clientLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            await _clientLock.WaitAsync();
            try
            {
                if (_client != null)
                {
                    await _client.DisposeAsync();
                    _client = null;
                }
            }
            finally
            {
                _clientLock.Release();
            }
        }
    }
}
