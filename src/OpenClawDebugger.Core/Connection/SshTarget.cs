namespace OpenClawDebugger;

/// <summary>
/// 概览页可切换的 SSH 目标预设。每一项都是完整的连接参数，
/// 因为不同服务器的 OpenClaw 目录布局不同，只换地址会读错目录。
/// </summary>
public sealed class SshTarget
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public int Port { get; set; } = 22;
    public string WorkspacePath { get; set; } = "";
    public string StickersPath { get; set; } = "";
}
