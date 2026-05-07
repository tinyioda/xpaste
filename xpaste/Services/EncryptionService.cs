using System.Security.Cryptography;
using System.Text;

namespace xpaste.Services;

/// <summary>
/// Provides AES-256-GCM encryption/decryption and PBKDF2 key derivation for snippet content.
/// All methods are stateless and static; no key material is held in this class.
/// </summary>
public class EncryptionService
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 200_000;
    private const string VerifyPlaintext = "xpaste-verify-v1";

    /// <summary>
    /// Derives a 256-bit AES key from a master password using PBKDF2-SHA256
    /// with <c>200,000</c> iterations.
    /// </summary>
    /// <param name="password">The master password entered by the user.</param>
    /// <param name="salt">Random salt; must be stored alongside the ciphertext.</param>
    /// <returns>32-byte derived key.</returns>
    public static byte[] DeriveKey(string password, byte[] salt)
    {
        var key = new byte[KeySize];
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, key, Iterations, HashAlgorithmName.SHA256);
        return key;
    }

    /// <summary>Generates a cryptographically random 16-byte salt.</summary>
    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with AES-256-GCM using a fresh random nonce.
    /// </summary>
    /// <param name="key">32-byte AES key.</param>
    /// <param name="plaintext">Plain-text content to encrypt.</param>
    /// <returns>A tuple of (ciphertext, nonce/IV, GCM tag), all Base64-encoded.</returns>
    public static (string cipher, string iv, string tag) Encrypt(byte[] key, string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MinSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plaintextBytes.Length];
        var tagBytes = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, cipherBytes, tagBytes);

        return (Convert.ToBase64String(cipherBytes),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tagBytes));
    }

    /// <summary>
    /// Decrypts AES-256-GCM ciphertext. Throws <see cref="CryptographicException"/>
    /// if the GCM authentication tag does not match (wrong key or tampered data).
    /// </summary>
    /// <param name="key">32-byte AES key.</param>
    /// <param name="cipher">Base64-encoded ciphertext.</param>
    /// <param name="iv">Base64-encoded nonce.</param>
    /// <param name="tag">Base64-encoded GCM authentication tag.</param>
    /// <returns>Decrypted plain-text string.</returns>
    public static string Decrypt(byte[] key, string cipher, string iv, string tag)
    {
        var cipherBytes = Convert.FromBase64String(cipher);
        var nonce = Convert.FromBase64String(iv);
        var tagBytes = Convert.FromBase64String(tag);
        var plaintextBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, cipherBytes, tagBytes, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <summary>Encrypts a well-known verification string so the stored blob can confirm a correct master password.</summary>
    /// <param name="key">Derived AES key to verify.</param>
    /// <returns>Verification blob as (ciphertext, nonce, tag), all Base64-encoded.</returns>
    public static (string c, string i, string t) CreateVerification(byte[] key)
        => Encrypt(key, VerifyPlaintext);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="key"/> successfully decrypts the verification blob
    /// and the result matches the expected plaintext.
    /// A <see cref="CryptographicException"/> (wrong GCM tag) is caught and returns <c>false</c>.
    /// </summary>
    public static bool VerifyPassword(byte[] key, string cipher, string iv, string tag)
    {
        try
        {
            var result = Decrypt(key, cipher, iv, tag);
            return result == VerifyPlaintext;
        }
        catch
        {
            return false;
        }
    }
}
