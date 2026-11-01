using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Venly.Backend.Common.Crypto;

/// <summary>A secret sealed by <see cref="ISecretProtector"/>: the key-encryption-key id and the envelope bytes.</summary>
public sealed record ProtectedSecret(string KekId, byte[] Ciphertext);

/// <summary>A sealed secret could not be opened: unknown KEK, malformed envelope, tampering or a wrong AAD.</summary>
public sealed class SecretProtectionException(string message, Exception? inner = null)
    : CryptographicException(message, inner);

/// <summary>
/// Seals and opens small secrets (API keys) with AES-256-GCM under a configured key-encryption key (KEK). The
/// associated data binds a ciphertext to its context (e.g. tenant | environment | version), so an envelope copied
/// onto another row or cache entry fails to open. Writers always use the active KEK; readers use the KEK id stored
/// with the ciphertext, so rotating the KEK never strands existing rows.
/// </summary>
public interface ISecretProtector
{
    /// <summary>The KEK new secrets are sealed with.</summary>
    string ActiveKeyId { get; }

    ProtectedSecret Protect(string plaintext, string associatedData);

    /// <summary>Opens an envelope. Throws <see cref="SecretProtectionException"/> on any failure.</summary>
    string Unprotect(string kekId, byte[] ciphertext, string associatedData);
}

/// <summary>
/// The AES-256-GCM implementation of <see cref="ISecretProtector"/>. Envelope layout (version 1):
/// <c>[0x01][12-byte random nonce][16-byte tag][ciphertext]</c>. Never logs or echoes key material.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const byte EnvelopeVersion = 1;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private readonly Dictionary<string, byte[]> _keks;

    public AesGcmSecretProtector(IOptions<SecretProtectionOptions> options)
    {
        var opts = options.Value;
        var failures = SecretProtectionOptionsValidator.Check(opts, SecretProtectionOptions.DefaultSectionName);
        if (failures.Count > 0)
            throw new OptionsValidationException(
                nameof(SecretProtectionOptions), typeof(SecretProtectionOptions), failures);

        _keks = opts.EncryptionKeys.ToDictionary(
            kv => kv.Key, kv => Convert.FromBase64String(kv.Value.Trim()), StringComparer.Ordinal);
        ActiveKeyId = opts.ActiveKeyId.Trim();
    }

    public string ActiveKeyId { get; }

    public ProtectedSecret Protect(string plaintext, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);

        var key = _keks[ActiveKeyId];
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var envelope = new byte[HeaderSize + plain.Length];
        envelope[0] = EnvelopeVersion;
        var nonce = envelope.AsSpan(1, NonceSize);
        var tag = envelope.AsSpan(1 + NonceSize, TagSize);
        var cipher = envelope.AsSpan(HeaderSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(associatedData));
        CryptographicOperations.ZeroMemory(plain);
        return new ProtectedSecret(ActiveKeyId, envelope);
    }

    public string Unprotect(string kekId, byte[] ciphertext, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(associatedData);
        if (string.IsNullOrEmpty(kekId) || !_keks.TryGetValue(kekId, out var key))
            throw new SecretProtectionException($"Unknown key-encryption key '{kekId}'.");
        if (ciphertext is null || ciphertext.Length < HeaderSize || ciphertext[0] != EnvelopeVersion)
            throw new SecretProtectionException("Malformed secret envelope.");

        var nonce = ciphertext.AsSpan(1, NonceSize);
        var tag = ciphertext.AsSpan(1 + NonceSize, TagSize);
        var cipher = ciphertext.AsSpan(HeaderSize);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(associatedData));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException ex)
        {
            // Tag mismatch: tampered bytes, the wrong KEK, or an envelope bound to a different context (AAD).
            throw new SecretProtectionException("The secret envelope could not be authenticated.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>
    /// A display-safe fingerprint of a secret: the first 16 lower-case hex chars of its SHA-256. Identifies a key
    /// in lists and audit records without revealing it.
    /// </summary>
    public static string Fingerprint(string plaintext) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)))[..16];
}
