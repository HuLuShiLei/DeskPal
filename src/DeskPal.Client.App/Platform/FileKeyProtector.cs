using System.Security.Cryptography;

namespace DeskPal.Client.Platform;

/// <summary>
/// Protects the local secrets with a random key kept in a file only the current user can read (mode 600),
/// next to the settings. Like DPAPI's per-user scope on Windows, any program running as this user can read it.
/// Used on macOS and Linux. The macOS keychain is not used: without code signing every update would make macOS ask
/// for the login password. On Linux a keyring is not always there (window managers without a desktop).
/// </summary>
public sealed class FileKeyProtector : ISecretProtector
{
    private const byte Version = 1;
    private readonly Lazy<byte[]> _key;

    public FileKeyProtector(string directory)
    {
        _key = new Lazy<byte[]>(() => LoadOrCreate(Path.Combine(directory, "local.key")));
    }

    public byte[] Protect(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_key.Value, 16))
        {
            aes.Encrypt(nonce, data, cipher, tag);
        }

        return [Version, .. nonce, .. tag, .. cipher];
    }

    public byte[] Unprotect(byte[] data)
    {
        if (data.Length < 1 + 12 + 16 || data[0] != Version)
        {
            throw new CryptographicException("Unknown format.");
        }

        var plain = new byte[data.Length - 29];
        using var aes = new AesGcm(_key.Value, 16);
        aes.Decrypt(data.AsSpan(1, 12), data.AsSpan(29), data.AsSpan(13, 16), plain);
        return plain;
    }

    private static byte[] LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length == 32)
            {
                return existing;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var key = RandomNumberGenerator.GetBytes(32);
        var temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        {
            stream.Write(key);
        }

        File.Move(temp, path, overwrite: true);
        return key;
    }
}
