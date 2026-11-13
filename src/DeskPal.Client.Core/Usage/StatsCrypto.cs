using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeskPal.Shared;
using DeskPal.Shared.Stats;

namespace DeskPal.Client.Core.Usage;

/// <summary>
/// Keeps stats readable only by the two members. Both sides use one-time ECDH P-256 keys; the AES-GCM key is derived
/// with SHA-256. The server only sees public keys and ciphertext. It does not stop a server that swaps the keys.
/// </summary>
public static class StatsCrypto
{
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("DeskPal stats v1");

    /// <summary>The owner's side: encrypts the report for the requester's public key.</summary>
    public static StatsResponsePayload Encrypt(Guid requestId, string requesterPublicKey, UsageReport report)
    {
        using var mine = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(Convert.FromBase64String(requesterPublicKey), out _);
        var key = mine.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256, null, Info);

        var plain = Pack(report);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var sealedData = new byte[plain.Length + AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plain, sealedData.AsSpan(0, plain.Length), sealedData.AsSpan(plain.Length), requestId.ToByteArray());
        return new StatsResponsePayload(requestId, Convert.ToBase64String(mine.ExportSubjectPublicKeyInfo()), Convert.ToBase64String(nonce), Convert.ToBase64String(sealedData));
    }

    public static byte[] Pack(UsageReport report)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal))
        {
            JsonSerializer.Serialize(brotli, report, DeskPalJson.Options);
        }

        return output.ToArray();
    }

    public static UsageReport Unpack(byte[] packed)
    {
        using var brotli = new BrotliStream(new MemoryStream(packed), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<UsageReport>(brotli, DeskPalJson.Options) ?? throw new InvalidDataException("The stats payload is empty");
    }

    /// <summary>The requester's one-time key. Disposed after the response arrives or the request times out.</summary>
    public sealed class RequestKey : IDisposable
    {
        private readonly ECDiffieHellman _key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

        /// <exception cref="CryptographicException">The response was altered or not meant for this key.</exception>
        public UsageReport Decrypt(StatsResponsePayload response)
        {
            if (response.PublicKey is null || response.Nonce is null || response.Cipher is null)
            {
                throw new CryptographicException("The response has no cipher text");
            }

            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(Convert.FromBase64String(response.PublicKey), out _);
            var key = _key.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256, null, Info);
            var sealedData = Convert.FromBase64String(response.Cipher);
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            if (sealedData.Length < tagSize)
            {
                throw new CryptographicException("The cipher text has the wrong length");
            }

            var plain = new byte[sealedData.Length - tagSize];
            using var aes = new AesGcm(key, tagSize);
            aes.Decrypt(Convert.FromBase64String(response.Nonce), sealedData.AsSpan(0, plain.Length), sealedData.AsSpan(plain.Length), plain, response.RequestId.ToByteArray());
            return Unpack(plain);
        }

        public void Dispose() => _key.Dispose();
    }
}