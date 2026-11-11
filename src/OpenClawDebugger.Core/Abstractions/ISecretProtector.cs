namespace OpenClawDebugger;

/// <summary>
/// 落盘前的对称加密。Windows 用 DPAPI（Crypt32），macOS 用 Keychain 托管的 AES-GCM 密钥。
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] ciphertext);
}
