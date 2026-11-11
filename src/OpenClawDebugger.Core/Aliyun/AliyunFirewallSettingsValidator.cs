using System.IO;
using System.Text.RegularExpressions;

namespace OpenClawDebugger;

public static class AliyunFirewallSettingsValidator
{
    public static void ValidateTarget(string regionId, string instanceId)
    {
        if (!Regex.IsMatch(regionId, "^[a-z0-9]+(?:-[a-z0-9]+)*$") || regionId.Length > 32)
            throw new InvalidDataException("阿里云地域 ID 格式无效，例如 cn-hangzhou。");
        if (!Regex.IsMatch(instanceId, "^[A-Za-z0-9-]{1,64}$"))
            throw new InvalidDataException("阿里云轻量服务器实例 ID 格式无效。");
    }
}
