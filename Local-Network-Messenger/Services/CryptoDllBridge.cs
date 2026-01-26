using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Network_Messenger.Services
{
    public sealed class CryptoDllBridge : ICryptoBridge, IDisposable
    {
        private readonly IntPtr _handle;
        private readonly CryptoTransformDelegate _encrypt;
        private readonly CryptoTransformDelegate _decrypt;
        private readonly CryptoFreeDelegate _free;

        private delegate int CryptoTransformDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string keyId,
            [MarshalAs(UnmanagedType.LPStr)] string payloadBase64,
            out IntPtr outputBase64,
            out IntPtr errorMessage);

        private delegate void CryptoFreeDelegate(IntPtr ptr);

        public CryptoDllBridge(string dllPath)
        {
            _handle = NativeLibrary.Load(dllPath);
            _encrypt = GetDelegate<CryptoTransformDelegate>("lnm_encrypt");
            _decrypt = GetDelegate<CryptoTransformDelegate>("lnm_decrypt");
            _free = GetDelegate<CryptoFreeDelegate>("lnm_free");
        }

        public Task<CryptoResult> EncryptAsync(CryptoRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Invoke(_encrypt, request));
        }

        public Task<CryptoResult> DecryptAsync(CryptoRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Invoke(_decrypt, request));
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeLibrary.Free(_handle);
            }
        }

        private CryptoResult Invoke(CryptoTransformDelegate transformer, CryptoRequest request)
        {
            var keyId = request.KeyId ?? "default";
            var payload = request.PayloadBase64 ?? string.Empty;
            var status = transformer(keyId, payload, out var outputPtr, out var errorPtr);
            var output = ReadAndFree(outputPtr);
            var error = ReadAndFree(errorPtr);

            if (status != 0 || !string.IsNullOrWhiteSpace(error))
            {
                return new CryptoResult(output.Length == 0 ? payload : output, "error", error);
            }

            return new CryptoResult(output.Length == 0 ? payload : output, "ok", "OK");
        }

        private string ReadAndFree(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
            }
            finally
            {
                _free(ptr);
            }
        }

        private T GetDelegate<T>(string name) where T : Delegate
        {
            var ptr = NativeLibrary.GetExport(_handle, name);
            return Marshal.GetDelegateForFunctionPointer<T>(ptr);
        }
    }
}
