using System;
using System.Threading;
using System.Threading.Tasks;
using Local_Network_Messenger.Models;

namespace Local_Network_Messenger.Services
{
    public sealed class PythonNetFileScanService : IFileScanService
    {
        private readonly PythonNetScope _scope;

        public PythonNetFileScanService()
        {
            if (!PythonNetRuntime.IsInitialized)
            {
                throw new InvalidOperationException("Python.NET baslatilamadi.");
            }

            using var _ = PythonNetRuntime.AcquireGIL();
            _scope = PythonNetRuntime.CreateScope()
                ?? throw new InvalidOperationException("Python scope olusturulamadi.");
            _scope.Exec(@"
import json
suspicious = {'exe', 'bat', 'cmd', 'ps1', 'vbs', 'js', 'dll', 'scr', 'msi'}

def scan(file_name, size_bytes):
    if not file_name:
        return json.dumps({'status': 'unknown', 'message': 'Dosya adi yok.'})
    ext = file_name.split('.')[-1].lower()
    if ext in suspicious:
        return json.dumps({'status': 'needs-review', 'message': 'Uzanti ' + ext + ' ek inceleme gerektiriyor.'})
    if size_bytes > 50 * 1024 * 1024:
        return json.dumps({'status': 'needs-review', 'message': 'Buyuk dosya, ekstra kontrol gerekli.'})
    return json.dumps({'status': 'clean', 'message': 'Temiz'})
");
        }

        public Task<FileScanResult> ScanAsync(FileScanRequest request, CancellationToken cancellationToken)
        {
            using var _ = PythonNetRuntime.AcquireGIL();
            _scope.Set("lnm_file_name", request.FileName);
            _scope.Set("lnm_size_bytes", request.SizeBytes);
            var json = _scope.EvalToString("scan(lnm_file_name, lnm_size_bytes)");
            if (string.IsNullOrWhiteSpace(json))
            {
                return Task.FromResult(new FileScanResult("unknown", "Tarama tamamlandi.", null));
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var status = root.GetProperty("status").GetString() ?? "unknown";
                var message = root.GetProperty("message").GetString() ?? "Tarama tamamlandi.";
                return Task.FromResult(new FileScanResult(status, message, null));
            }
            catch (System.Text.Json.JsonException)
            {
                return Task.FromResult(new FileScanResult("unknown", "Tarama tamamlandi.", null));
            }
        }
    }
}
