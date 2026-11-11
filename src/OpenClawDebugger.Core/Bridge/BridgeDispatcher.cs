using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace OpenClawDebugger;

public sealed class BridgeDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Func<string, string, JsonElement, IReadOnlyList<BridgeFile>, CancellationToken, Task<object?>> _dispatch;
    private readonly BridgeResponseWriter _responses;
    private readonly BridgeCommandContract _contract;
    private readonly Uri[] _allowedOrigins;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _operations = new(StringComparer.Ordinal);

    /// <summary>
    /// 默认只接受 Windows 上由 WebView2 虚拟主机映射的 https://openclaw.local。
    /// macOS 侧由回环 HTTP 提供界面，因此由壳在构造时补上自己的来源。
    /// </summary>
    public BridgeDispatcher(
        Func<string, string, JsonElement, IReadOnlyList<BridgeFile>, CancellationToken, Task<object?>> dispatch,
        BridgeResponseWriter responses,
        BridgeCommandContract contract,
        IEnumerable<string>? allowedOrigins = null)
    {
        _dispatch = dispatch;
        _responses = responses;
        _contract = contract;
        _allowedOrigins = (allowedOrigins ?? ["https://openclaw.local"])
            .Select(origin => new Uri(origin, UriKind.Absolute))
            .ToArray();
    }

    public async Task HandleAsync(BridgeRequestEnvelope envelope)
    {
        if (!IsAllowedSource(envelope.SourceUri)) return;
        string? id = null;
        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(envelope.Json, JsonOptions)
                ?? throw new InvalidDataException("无效的界面请求。");
            id = request.Id;
            if (request.ProtocolVersion != BridgeProtocol.Version)
                throw new InvalidDataException($"界面协议版本不匹配：需要 v{BridgeProtocol.Version}，收到 v{request.ProtocolVersion}。请重启应用。");
            _contract.ValidatePayload(request.Command, request.Payload);

            if (string.Equals(request.Command, "cancelOperation", StringComparison.Ordinal))
            {
                var target = request.Payload.GetProperty("operationId").GetString();
                if (!string.IsNullOrWhiteSpace(target) && _operations.TryGetValue(target, out var targetCancellation))
                    targetCancellation.Cancel();
                _responses.Respond(id, true, new { cancelled = !string.IsNullOrWhiteSpace(target) }, null);
                return;
            }

            var operationId = string.IsNullOrWhiteSpace(request.OperationId) ? request.Id : request.OperationId;
            using var operationCancellation = new CancellationTokenSource();
            var timeoutMs = _contract.GetCommand(request.Command).TimeoutMs;
            if (timeoutMs > 0) operationCancellation.CancelAfter(timeoutMs);
            if (!_operations.TryAdd(operationId, operationCancellation))
                throw new InvalidOperationException("重复的界面操作标识。");
            var operationLog = OperationLogStore.Current?.Begin(operationId, request.Command);
            try
            {
                var files = envelope.Files;
                var result = await _dispatch(operationId, request.Command, request.Payload, files, operationCancellation.Token);
                operationLog?.Complete();
                _responses.Respond(id, true, result, null);
            }
            catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
            {
                operationLog?.Complete("cancelled", false, "操作已取消");
                throw;
            }
            catch (Exception ex)
            {
                operationLog?.Complete("failed", false, ex.Message);
                throw;
            }
            finally
            {
                _operations.TryRemove(operationId, out _);
            }
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(id)) _responses.Respond(id, false, null, ex.Message);
        }
    }

    public void CancelAll()
    {
        foreach (var cancellation in _operations.Values)
        {
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private bool IsAllowedSource(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return false;
        foreach (var origin in _allowedOrigins)
        {
            if (string.Equals(origin.Scheme, uri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(origin.Host, uri.Host, StringComparison.OrdinalIgnoreCase) &&
                origin.Port == uri.Port)
                return true;
        }
        return false;
    }

    private sealed record BridgeRequest(
        string Id,
        string Command,
        JsonElement Payload,
        int ProtocolVersion = 0,
        string? OperationId = null);
}
