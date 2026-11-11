using System.IO;
using System.Text.Json;

namespace OpenClawDebugger;

/// <summary>
/// 把阿里云 AccessKey 序列化后交给系统凭据存储。与旧实现的行为完全一致，
/// 唯一区别是凭据存储由构造函数注入，不再硬编码平台实现。
/// </summary>
public sealed class CredentialAliyunAccessKeyVault(ICredentialStore credentialStore) : IAliyunAccessKeyVault
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool IsConfigured => Read() is not null;

    public AliyunAccessKeyPair? Read()
    {
        var stored = credentialStore.Read(CredentialTargets.AliyunSwasAccessKey());
        if (stored is null) return null;
        var accessKey = JsonSerializer.Deserialize<AliyunAccessKeyPair>(stored, JsonOptions)
            ?? throw new InvalidDataException("保存的阿里云凭据格式无效，请重新保存。");
        Validate(accessKey);
        return accessKey;
    }

    public void Save(AliyunAccessKeyPair accessKey)
    {
        Validate(accessKey);
        var serialized = JsonSerializer.Serialize(accessKey, JsonOptions);
        credentialStore.Write(CredentialTargets.AliyunSwasAccessKey(), serialized);
    }

    private static void Validate(AliyunAccessKeyPair accessKey)
    {
        if (string.IsNullOrWhiteSpace(accessKey.AccessKeyId) || accessKey.AccessKeyId.Length > 256 ||
            accessKey.AccessKeyId.Any(char.IsControl))
            throw new InvalidDataException("阿里云 AccessKey ID 格式无效。");
        if (string.IsNullOrWhiteSpace(accessKey.AccessKeySecret) || accessKey.AccessKeySecret.Length > 256 ||
            accessKey.AccessKeySecret.Any(char.IsControl))
            throw new InvalidDataException("阿里云 AccessKey Secret 格式无效。");
    }
}
