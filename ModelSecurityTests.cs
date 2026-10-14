using System.IO;
using System.Text.Json;

namespace OpenClawDebugger;

internal static class ModelSecurityTests
{
    public static async Task AddModelRequiresHttpsAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, ".model-security-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var remote = new RecordingRemoteModelClient();
            using var service = new ModelService(remote, () => new ConnectionSettings(), () => true, root, _ => { });
            using (var http = JsonDocument.Parse("""{"modelRef":"provider/model","baseUrl":"http://provider.example/v1","apiKey":"test-key"}"""))
            {
                await AssertThrowsAsync<InvalidDataException>(() => service.AddAsync(http.RootElement));
                Assert(remote.AddedRequest is null, "HTTP 请求必须在密钥到达远程客户端前被拒绝。");
            }
            using (var https = JsonDocument.Parse("""{"modelRef":"provider/model","baseUrl":"https://provider.example/v1","apiKey":"test-key"}"""))
            {
                await service.AddAsync(https.RootElement);
                Assert(remote.AddedRequest?.BaseUrl == "https://provider.example/v1", "HTTPS 服务商地址应继续可用。");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("预期抛出 " + typeof(TException).Name);
    }

    private sealed class RecordingRemoteModelClient : IRemoteModelClient
    {
        public RemoteModelAddRequest? AddedRequest { get; private set; }

        public Task<RemoteModelFetchResult> GetSnapshotAsync(ConnectionSettings settings, string? knownConfigHash = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RemoteModelSnapshot> SetOrderAsync(ConnectionSettings settings, string primary, IReadOnlyList<string> fallbacks, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<RemoteModelSnapshot> AddModelAsync(ConnectionSettings settings, RemoteModelAddRequest request, CancellationToken cancellationToken = default)
        {
            AddedRequest = request;
            return Task.FromResult(new RemoteModelSnapshot(null, Array.Empty<RemoteModelInfo>(), Array.Empty<RemoteModelInfo>(), DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<RemoteModelLatencyResult>> TestLatencyAsync(ConnectionSettings settings, IReadOnlyList<string> modelIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
