using System.IO;
using System.Text.Json;

namespace OpenClawDebugger;

/// <summary>校验插件管理请求，并将已连接的远程操作交给插件领域客户端。</summary>
public sealed class PluginService
{
    private readonly IRemotePluginClient _remote;
    private readonly Func<ConnectionSettings> _settings;
    private readonly Func<bool> _isConnected;
    private readonly Action<bool> _setBusy;

    public PluginService(
        IRemotePluginClient remote,
        Func<ConnectionSettings> settings,
        Func<bool> isConnected,
        Action<bool> setBusy)
    {
        _remote = remote;
        _settings = settings;
        _isConnected = isConnected;
        _setBusy = setBusy;
    }

    public Task<object> GetAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(new RemotePluginRequest("plugins_inventory"), cancellationToken);

    public Task<object> SearchAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var query = RequiredString(payload, "query");
        if (string.IsNullOrWhiteSpace(query)) throw new InvalidDataException("请输入插件搜索词。");
        return ExecuteAsync(new RemotePluginRequest("plugins_search", Query: query.Trim()), cancellationToken);
    }

    public Task<object> InspectAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var pluginId = ValidatePluginId(RequiredString(payload, "pluginId"));
        return ExecuteAsync(new RemotePluginRequest("plugins_inspect", PluginId: pluginId), cancellationToken);
    }

    public Task<object> SetEnabledAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var pluginId = ValidatePluginId(RequiredString(payload, "pluginId"));
        var enabled = RequiredBoolean(payload, "enabled");
        var accepted = RequiredBoolean(payload, "acceptCapabilities");
        var action = enabled ? "plugins_enable" : "plugins_disable";
        return ExecuteAsync(new RemotePluginRequest(action, PluginId: pluginId, AcceptCapabilities: accepted), cancellationToken);
    }

    public Task<object> ReloadAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var pluginId = ValidatePluginId(RequiredString(payload, "pluginId"));
        var accepted = RequiredBoolean(payload, "acceptCapabilities");
        return ExecuteAsync(new RemotePluginRequest("plugins_reload", PluginId: pluginId, AcceptCapabilities: accepted), cancellationToken);
    }

    public Task<object> UpdateAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var pluginId = ValidatePluginId(RequiredString(payload, "pluginId"));
        var accepted = RequiredBoolean(payload, "acceptCapabilities");
        return ExecuteAsync(new RemotePluginRequest("plugins_update", PluginId: pluginId, AcceptCapabilities: accepted), cancellationToken);
    }

    public Task<object> UninstallAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var pluginId = ValidatePluginId(RequiredString(payload, "pluginId"));
        return ExecuteAsync(new RemotePluginRequest("plugins_uninstall", PluginId: pluginId), cancellationToken);
    }

    public Task<object> InstallAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var packageSpec = ValidatePackageSpec(RequiredString(payload, "packageSpec"));
        var accepted = RequiredBoolean(payload, "acceptCapabilities");
        return ExecuteAsync(new RemotePluginRequest(
            "plugins_install", PackageSpec: packageSpec, AcceptCapabilities: accepted), cancellationToken);
    }

    private async Task<object> ExecuteAsync(RemotePluginRequest request, CancellationToken cancellationToken)
    {
        if (!_isConnected()) throw new InvalidOperationException("请先连接服务器，再管理插件。");
        _setBusy(true);
        try
        {
            return await _remote.ExecuteAsync(_settings(), request, cancellationToken);
        }
        finally { _setBusy(false); }
    }

    private static string RequiredString(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("插件管理参数无效：" + name);
        return value.GetString() ?? "";
    }

    private static bool RequiredBoolean(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("插件管理参数无效：" + name);
        return value.GetBoolean();
    }

    private static string ValidatePluginId(string pluginId)
    {
        if (pluginId.Length == 0 || !IsIdStartCharacter(pluginId[0]))
            throw new InvalidDataException("插件 ID 格式无效。");
        if (pluginId.Any(character => !IsIdCharacter(character)))
            throw new InvalidDataException("插件 ID 格式无效。");
        return pluginId;
    }

    private static bool IsIdCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '@' or '.' or '_' or '/' or '-';

    private static bool IsIdStartCharacter(char value) => char.IsAsciiLetterOrDigit(value) || value == '@';

    private static string ValidatePackageSpec(string packageSpec)
    {
        if (string.IsNullOrWhiteSpace(packageSpec) || packageSpec.StartsWith('-', StringComparison.Ordinal) ||
            packageSpec.Any(char.IsControl))
            throw new InvalidDataException("请输入插件包来源；来源不能以连字符开头或包含控制字符。");
        return packageSpec;
    }
}
