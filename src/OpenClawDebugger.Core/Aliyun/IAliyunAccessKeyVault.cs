namespace OpenClawDebugger;

/// <summary>阿里云 AccessKey 的本地保管。实现由各平台提供（凭据管理器 / Keychain）。</summary>
public interface IAliyunAccessKeyVault
{
    bool IsConfigured { get; }

    AliyunAccessKeyPair? Read();

    void Save(AliyunAccessKeyPair accessKey);
}
