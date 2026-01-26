namespace Local_Network_Messenger.Integrations
{
    public static class IpcMessageTypes
    {
        public const string CryptoRequest = "crypto.request";
        public const string CryptoResponse = "crypto.response";
        public const string FileScanRequest = "scan.request";
        public const string FileScanResponse = "scan.response";
    }

    public sealed record IpcEnvelope<T>(string Id, string Type, T Payload);

    public sealed record IpcError(string Code, string Message);

    public sealed record CryptoIpcRequest(string Operation, string KeyId, string PayloadBase64);

    public sealed record CryptoIpcResponse(string PayloadBase64, IpcError? Error);

    public sealed record FileScanIpcRequest(string FileName, long SizeBytes, string? Sha256Base64);

    public sealed record FileScanIpcResponse(string Status, string? Details, IpcError? Error);
}
