using System.Reflection;
using System.Text;
using System.IO;

namespace OpenClawDebugger;

/// <summary>
/// Loads the remote Python agent from an embedded resource. Keeping the agent in a
/// standalone .py file makes it reviewable and keeps the SSH protocol easy to update.
/// </summary>
internal static class RemoteAgentProgram
{
    private const string ResourceName = "OpenClawDebugger.RemoteAgentProgram.py";
    private const string PluginOperationsResourceName = "OpenClawDebugger.RemotePluginOperations.py";
    private const string PluginOperationsMarker = "# OPENCLAW_PLUGIN_OPERATIONS";
    private static readonly Lazy<string> Script = new(LoadScript, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Main => Script.Value;

    private static string LoadScript()
    {
        var assembly = typeof(RemoteAgentProgram).Assembly;
        var source = ReadResource(assembly, ResourceName);
        var pluginOperations = ReadResource(assembly, PluginOperationsResourceName);
        if (source.IndexOf(PluginOperationsMarker, StringComparison.Ordinal) < 0 ||
            source.IndexOf(PluginOperationsMarker, StringComparison.Ordinal) != source.LastIndexOf(PluginOperationsMarker, StringComparison.Ordinal))
            throw new InvalidOperationException("远程代理插件操作插入点缺失或重复。");
        return source.Replace(PluginOperationsMarker, pluginOperations, StringComparison.Ordinal);
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("远程代理资源未嵌入：" + resourceName);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

internal static class RemoteAgentProtocol
{
    public const int Version = 3;
}
