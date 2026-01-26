using System;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Integrations;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed class FileScanProcessService : IFileScanService, IAsyncDisposable
    {
        private readonly ProcessJsonClient _client;

        public FileScanProcessService(ProcessJsonClient client)
        {
            _client = client;
        }

        public async Task<FileScanResult> ScanAsync(FileScanRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _client.SendAsync<FileScanIpcRequest, FileScanIpcResponse>(
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
                return new FileScanResult("error", "Tarama servisine baglanilamadi.", ex.Message);
            }
        }

        public ValueTask DisposeAsync()
        {
            return _client.DisposeAsync();
        }
    }
}
