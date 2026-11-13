using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Encryption of usage stats synced to the server. The key is derived from an 8-digit PIN with Argon2id; the PIN and the
/// key never leave the user's computers. The salt string carries the parameters, so they can be raised later.
/// </summary>
public static class StatsSyncCrypto
{
    public const int PinLength = 8;

    /// <summary>About half a second on a current desktop CPU, and 64 MB per guess for anyone trying every PIN.</summary>
    public static readonly KdfParameters DefaultParameters = new(64 * 1024, 3, 4);

    private const string Scheme = "argon2id";
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int SaltSize = 16;
    private static readonly byte[] CheckText = "DeskPal stats sync"u8.ToArray();

    /// <param name="MemoryKiB">Memory per derivation.</param>
    public sealed record KdfParameters(int MemoryKiB, int Iterations, int Parallelism);

    public static bool IsValidPin(string? pin) => pin is { Length: PinLength } && pin.All(char.IsAsciiDigit);

    /// <summary>A new random salt with the parameters, e.g. "argon2id$m=65536,t=3,p=4$...".</summary>
    public static string NewSalt(KdfParameters? parameters = null)
    {
        var p = parameters ?? DefaultParameters;
        return string.Create(CultureInfo.InvariantCulture, $"{Scheme}$m={p.MemoryKiB},t={p.Iterations},p={p.Parallelism}${Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSize))}");
    }

    /// <exception cref="FormatException">The salt was not made by <see cref="NewSalt"/>.</exception>
    public static byte[] DeriveKey(string pin, string salt)
    {
        var parts = salt.Split('$');
        if (parts.Length != 3 || parts[0] != Scheme)
        {
            throw new FormatException("Unknown key derivation");
        }

        var values = parts[1].Split(',')
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => int.Parse(p[1], CultureInfo.InvariantCulture));
        if (!values.TryGetValue("m", out var memory) || !values.TryGetValue("t", out var iterations) || !values.TryGetValue("p", out var parallelism) ||
            memory is < 8 or > 1024 * 1024 || iterations is < 1 or > 100 || parallelism is < 1 or > 64)
        {
            throw new FormatException("Invalid key derivation parameters");
        }

        using var argon = new Argon2id(Encoding.UTF8.GetBytes(pin))
        {
            Salt = Convert.FromBase64String(parts[2]),
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(KeySize);
    }

    public static string KeyCheck(byte[] key) => Convert.ToBase64String(Encrypt(key, CheckText));

    /// <summary>Whether the key decrypts the check stored on the server, i.e. the PIN is right.</summary>
    public static bool Verify(byte[] key, string keyCheck)
    {
        try
        {
            return Decrypt(key, Convert.FromBase64String(keyCheck)).AsSpan().SequenceEqual(CheckText);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Compresses and encrypts one day.</summary>
    public static byte[] Seal(byte[] key, byte[] plain)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(plain);
        }

        return Encrypt(key, buffer.ToArray());
    }

    /// <exception cref="CryptographicException">Wrong key, or the data was changed.</exception>
    public static byte[] Open(byte[] key, byte[] sealedData)
    {
        using var gzip = new GZipStream(new MemoryStream(Decrypt(key, sealedData)), CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gzip.CopyTo(plain);
        return plain.ToArray();
    }

    private static byte[] Encrypt(byte[] key, byte[] plain)
    {
        var result = new byte[1 + NonceSize + TagSize + plain.Length];
        result[0] = FormatVersion;
        var nonce = result.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, result.AsSpan(1 + NonceSize + TagSize), result.AsSpan(1 + NonceSize, TagSize));
        return result;
    }

    private static byte[] Decrypt(byte[] key, byte[] data)
    {
        if (data.Length < 1 + NonceSize + TagSize || data[0] != FormatVersion)
        {
            throw new CryptographicException("Unknown format");
        }

        var plain = new byte[data.Length - 1 - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(data.AsSpan(1, NonceSize), data.AsSpan(1 + NonceSize + TagSize), data.AsSpan(1 + NonceSize, TagSize), plain);
        return plain;
    }
}