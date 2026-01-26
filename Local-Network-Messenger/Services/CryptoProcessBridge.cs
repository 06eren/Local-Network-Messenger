using System;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Integrations;

namespace Local_Network_Messenger.Services
{
    public sealed class CryptoProcessBridge : ICryptoBridge, IAsyncDisposable
    {
        private readonly ProcessJsonClient _client;

        public CryptoProcessBridge(ProcessJsonClient client)
        {
            _client = client;
        }

        public async Task<CryptoResult> EncryptAsync(CryptoRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _client.SendAsync<CryptoIpcRequest, CryptoIpcResponse>(
                    IpcMessageTypes.CryptoRequest,
                    new CryptoIpcRequest("encrypt", request.KeyId, request.PayloadBase64),
                    cancellationToken);

                if (response.Payload.Error != null)
                {
                    return new CryptoResult(request.PayloadBase64, "error", response.Payload.Error.Message);
                }

                var payload = response.Payload.PayloadBase64 ?? request.PayloadBase64;
                return new CryptoResult(payload, "ok", "Sifreleme tamamlandi.");
            }
            catch (Exception ex)
            {
                return new CryptoResult(request.PayloadBase64, "error", ex.Message);
            }
        }

        public async Task<CryptoResult> DecryptAsync(CryptoRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _client.SendAsync<CryptoIpcRequest, CryptoIpcResponse>(
                    IpcMessageTypes.CryptoRequest,
                    new CryptoIpcRequest("decrypt", request.KeyId, request.PayloadBase64),
                    cancellationToken);

                if (response.Payload.Error != null)
                {
                    return new CryptoResult(request.PayloadBase64, "error", response.Payload.Error.Message);
                }

                var payload = response.Payload.PayloadBase64 ?? request.PayloadBase64;
                return new CryptoResult(payload, "ok", "Cozme tamamlandi.");
            }
            catch (Exception ex)
            {
                return new CryptoResult(request.PayloadBase64, "error", ex.Message);
            }
        }

        public ValueTask DisposeAsync()
        {
            return _client.DisposeAsync();
        }
    }
}
