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
    private const string SkillOperationsResourceName = "OpenClawDebugger.RemoteSkillOperations.py";
    private const string PluginOperationsMarker = "# OPENCLAW_PLUGIN_OPERATIONS";
    private const string SkillOperationsMarker = "# OPENCLAW_SKILL_OPERATIONS";
    private static readonly Lazy<string> Script = new(LoadScript, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Main => Script.Value;

    private static string LoadScript()
    {
        var assembly = typeof(RemoteAgentProgram).Assembly;
        var source = ReadResource(assembly, ResourceName);
        var pluginOperations = ReadResource(assembly, PluginOperationsResourceName);
        var skillOperations = ReadResource(assembly, SkillOperationsResourceName);
        source = ReplaceMarker(source, PluginOperationsMarker, pluginOperations, "插件");
        return ReplaceMarker(source, SkillOperationsMarker, skillOperations, "技能");
    }

    private static string ReplaceMarker(string source, string marker, string operations, string label)
    {
        var first = source.IndexOf(marker, StringComparison.Ordinal);
        if (first < 0 || first != source.LastIndexOf(marker, StringComparison.Ordinal))
            throw new InvalidOperationException("远程代理" + label + "操作插入点缺失或重复。");
        return source.Replace(marker, operations, StringComparison.Ordinal);
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
