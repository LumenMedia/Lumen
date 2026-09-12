using System.Runtime.InteropServices;

namespace Lumen.Services;

internal static class SecureTokenStore
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn,
        out IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string Protect(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var input = new DATA_BLOB { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.pbData, bytes.Length);
            if (!CryptProtectData(ref input, "Lumen Jellyfin session", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var encrypted = new byte[output.cbData];
                Marshal.Copy(output.pbData, encrypted, 0, output.cbData);
                return Convert.ToBase64String(encrypted);
            }
            finally { LocalFree(output.pbData); }
        }
        finally { Marshal.FreeHGlobal(input.pbData); }
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try
        {
            var bytes = Convert.FromBase64String(protectedValue);
            var input = new DATA_BLOB { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(bytes.Length) };
            try
            {
                Marshal.Copy(bytes, 0, input.pbData, bytes.Length);
                if (!CryptUnprotectData(ref input, out var description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                    return null;
                try
                {
                    var plain = new byte[output.cbData];
                    Marshal.Copy(output.pbData, plain, 0, output.cbData);
                    return System.Text.Encoding.UTF8.GetString(plain);
                }
                finally
                {
                    if (description != IntPtr.Zero) LocalFree(description);
                    LocalFree(output.pbData);
                }
            }
            finally { Marshal.FreeHGlobal(input.pbData); }
        }
        catch { return null; }
    }
}
