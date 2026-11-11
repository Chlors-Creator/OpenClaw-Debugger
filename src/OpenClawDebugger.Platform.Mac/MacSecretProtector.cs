using System.Security.Cryptography;
using System.Text;

namespace OpenClawDebugger;

/// <summary>
/// macOS 上的快照加密：AES-GCM，主密钥随机生成后托管在 Keychain 里，
/// 与 Windows 上 DPAPI 的作用等价。输出格式为 nonce | tag | ciphertext。
/// </summary>
public sealed class MacSecretProtector(ICredentialStore credentialStore) : ISecretProtector
{
    private const string MasterKeyTarget = "OpenClawDebugger/SnapshotMasterKey";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly object _keyGate = new();

    public byte[] Protect(byte[] plaintext)
    {
        var key = GetOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceSize);
        ciphertext.CopyTo(output, NonceSize + TagSize);
        return output;
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (ciphertext.Length < NonceSize + TagSize)
            throw new CryptographicException("快照数据长度非法。");

        var key = GetOrCreateKey();
        var nonce = ciphertext.AsSpan(0, NonceSize);
        var tag = ciphertext.AsSpan(NonceSize, TagSize);
        var payload = ciphertext.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[payload.Length];
        using (var aes = new AesGcm(key, TagSize))
            aes.Decrypt(nonce, payload, tag, plaintext);
        return plaintext;
    }

    private byte[] GetOrCreateKey()
    {
        lock (_keyGate)
        {
            var stored = credentialStore.Read(MasterKeyTarget);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                var existing = Convert.FromBase64String(stored);
                if (existing.Length == KeySize) return existing;
            }

            var created = RandomNumberGenerator.GetBytes(KeySize);
            credentialStore.Write(MasterKeyTarget, Convert.ToBase64String(created));
            return created;
        }
    }
}
