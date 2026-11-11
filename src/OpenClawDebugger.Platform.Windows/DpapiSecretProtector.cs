using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenClawDebugger;

/// <summary>
/// 基于 Windows DPAPI（Crypt32）的加密实现，与原 LocalSnapshotStore 内嵌实现完全一致。
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const int UiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob input, string? description, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input, IntPtr description, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

    public byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputPointer = Marshal.AllocHGlobal(Math.Max(input.Length, 1));
        try
        {
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var blob = new DataBlob { Length = input.Length, Data = inputPointer };
            DataBlob output;
            var success = protect
                ? CryptProtectData(ref blob, "OpenClaw Manager rollback snapshot", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(inputPointer); }
    }
}
