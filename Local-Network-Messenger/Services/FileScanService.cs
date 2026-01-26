using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public interface IFileScanService
    {
        Task<FileScanResult> ScanAsync(FileScanRequest request, CancellationToken cancellationToken);
    }

    public sealed class FileScanService : IFileScanService
    {
        public Task<FileScanResult> ScanAsync(FileScanRequest request, CancellationToken cancellationToken)
        {
            var result = new FileScanResult("queued", "Dosya taramaya alindi.", null);
            return Task.FromResult(result);
        }
    }
}
