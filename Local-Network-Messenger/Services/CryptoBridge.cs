using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Services
{
    public sealed record CryptoRequest(string KeyId, string PayloadBase64);

    public sealed record CryptoResult(string PayloadBase64, string Status, string Message);

    public interface ICryptoBridge
    {
        Task<CryptoResult> EncryptAsync(CryptoRequest request, CancellationToken cancellationToken);
        Task<CryptoResult> DecryptAsync(CryptoRequest request, CancellationToken cancellationToken);
    }

    public sealed class PassThroughCryptoBridge : ICryptoBridge
    {
        public Task<CryptoResult> EncryptAsync(CryptoRequest request, CancellationToken cancellationToken)
        {
            var result = new CryptoResult(request.PayloadBase64, "disabled", "Sifreleme koprusu devrede degil.");
            return Task.FromResult(result);
        }

        public Task<CryptoResult> DecryptAsync(CryptoRequest request, CancellationToken cancellationToken)
        {
            var result = new CryptoResult(request.PayloadBase64, "disabled", "Sifreleme koprusu devrede degil.");
            return Task.FromResult(result);
        }
    }
}
