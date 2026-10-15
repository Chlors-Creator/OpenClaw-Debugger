using System.Text.Json;

namespace OpenClawDebugger;

public sealed class CustomArtifactBridgeHandler(CustomArtifactService service)
{
    public void Register(BridgeCommandRouter router)
    {
        router.Map("getCustomArtifacts", async (_, token) => await service.GetAsync(token));
        router.Map("deleteCustomArtifact", async (payload, token) => await service.DeleteAsync(payload, token));
        router.Map("uploadCustomArtifact", (_, _) => Task.FromException<object?>(
            new InvalidOperationException("自定义上传请求缺少本地文件对象。")));
    }

    public Task<object> UploadFileAsync(JsonElement payload, string localPath, CancellationToken cancellationToken) =>
        service.UploadAsync(payload, localPath, cancellationToken);
}
