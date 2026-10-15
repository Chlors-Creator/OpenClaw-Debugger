using System.IO;
using System.Text.Json;

namespace OpenClawDebugger;

public interface IAliyunAccessKeyVault
{
    bool IsConfigured { get; }
    AliyunAccessKeyPair? Read();
    void Save(AliyunAccessKeyPair accessKey);
}

public sealed class WindowsAliyunAccessKeyVault : IAliyunAccessKeyVault
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public bool IsConfigured => Read() is not null;

    public AliyunAccessKeyPair? Read()
    {
        var stored = WindowsCredentialStore.Read(WindowsCredentialStore.AliyunSwasAccessKeyTarget());
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
        WindowsCredentialStore.Write(WindowsCredentialStore.AliyunSwasAccessKeyTarget(), serialized);
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
