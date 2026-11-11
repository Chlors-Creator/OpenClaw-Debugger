using System.Security.Cryptography;
using System.Text;

namespace OpenClawDebugger;

/// <summary>
/// 系统级密钥存储。Windows 走凭据管理器，macOS 走 Keychain。
/// 只有不透明的目标名和索引留在应用私有目录，密钥本体不进文件。
/// </summary>
public interface ICredentialStore
{
    string? Read(string target);

    void Write(string target, string secret);

    void Delete(string target);
}

/// <summary>
/// 凭据目标名的推导规则。纯字符串逻辑，与平台无关，因此留在 Core，
/// 保证两个平台写入的是同一批目标名。
/// </summary>
public static class CredentialTargets
{
    public static string ModelApiKey(ConnectionSettings settings, string modelRef) =>
        "OpenClawDebugger/ModelApiKey/" + StableKey(settings.Target + ":" + settings.Port + ":" + modelRef);

    public static string SshSecret(ConnectionSettings settings) =>
        "OpenClawDebugger/SshSecret/" + StableKey(settings.Target + ":" + settings.Port);

    public static string AliyunSwasAccessKey() => "OpenClawDebugger/AliyunSwasAccessKey";

    private static string StableKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
