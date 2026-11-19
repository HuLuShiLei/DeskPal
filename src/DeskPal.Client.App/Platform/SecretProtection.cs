using System.Security.Cryptography;

namespace DeskPal.Client.Platform;

/// <summary>
/// Encrypts secrets kept in local files (the login token, the stats sync keys) so they only open for the current
/// user on this computer. Windows uses DPAPI; macOS keeps a key in the login keychain.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] data);

    /// <exception cref="CryptographicException">The data was protected elsewhere, or the key is gone.</exception>
    byte[] Unprotect(byte[] data);
}

public static class SecretProtection
{
    private static ISecretProtector? _current;

    /// <summary>Set once at startup, before settings are read.</summary>
    public static ISecretProtector Current
    {
        get => _current ?? throw new InvalidOperationException("SecretProtection.Current is not set.");
        set => _current = value;
    }

    public static string ProtectToBase64(byte[] data) => Convert.ToBase64String(Current.Protect(data));

    /// <exception cref="CryptographicException">See <see cref="ISecretProtector.Unprotect"/>.</exception>
    /// <exception cref="FormatException">Not base64.</exception>
    public static byte[] UnprotectFromBase64(string data) => Current.Unprotect(Convert.FromBase64String(data));
}
