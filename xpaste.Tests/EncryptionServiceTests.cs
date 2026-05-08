using System.Security.Cryptography;
using xpaste.Services;

namespace xpaste.Tests;

public class EncryptionServiceTests
{
    // ── DeriveKey ────────────────────────────────────────────────────────────

    [Fact]
    public void DeriveKey_SamePasswordAndSalt_ProducesSameKey()
    {
        var salt = EncryptionService.GenerateSalt();
        var k1 = EncryptionService.DeriveKey("password", salt);
        var k2 = EncryptionService.DeriveKey("password", salt);
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void DeriveKey_DifferentSalt_ProducesDifferentKey()
    {
        var k1 = EncryptionService.DeriveKey("password", EncryptionService.GenerateSalt());
        var k2 = EncryptionService.DeriveKey("password", EncryptionService.GenerateSalt());
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void DeriveKey_DifferentPassword_ProducesDifferentKey()
    {
        var salt = EncryptionService.GenerateSalt();
        var k1 = EncryptionService.DeriveKey("password1", salt);
        var k2 = EncryptionService.DeriveKey("password2", salt);
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void DeriveKey_Returns32Bytes()
    {
        var key = EncryptionService.DeriveKey("test", EncryptionService.GenerateSalt());
        Assert.Equal(32, key.Length);
    }

    // ── GenerateSalt ─────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSalt_Returns16Bytes()
    {
        Assert.Equal(16, EncryptionService.GenerateSalt().Length);
    }

    [Fact]
    public void GenerateSalt_TwoCalls_ProduceDifferentValues()
    {
        Assert.NotEqual(EncryptionService.GenerateSalt(), EncryptionService.GenerateSalt());
    }

    // ── Encrypt / Decrypt ────────────────────────────────────────────────────

    [Fact]
    public void Encrypt_Decrypt_Roundtrip()
    {
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.Encrypt(key, "hello world");
        Assert.Equal("hello world", EncryptionService.Decrypt(key, c, iv, tag));
    }

    [Fact]
    public void Encrypt_Decrypt_EmptyString()
    {
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.Encrypt(key, "");
        Assert.Equal("", EncryptionService.Decrypt(key, c, iv, tag));
    }

    [Fact]
    public void Encrypt_Decrypt_UnicodeContent()
    {
        const string unicode = "P@$$w0rd! 日本語 émojis 🔑";
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.Encrypt(key, unicode);
        Assert.Equal(unicode, EncryptionService.Decrypt(key, c, iv, tag));
    }

    [Fact]
    public void Encrypt_Decrypt_LongContent()
    {
        var longText = new string('x', 10_000);
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.Encrypt(key, longText);
        Assert.Equal(longText, EncryptionService.Decrypt(key, c, iv, tag));
    }

    [Fact]
    public void Encrypt_SamePlaintext_ProducesDifferentCiphertext_EachCall()
    {
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c1, _, _) = EncryptionService.Encrypt(key, "secret");
        var (c2, _, _) = EncryptionService.Encrypt(key, "secret");
        // Fresh nonce each call → different ciphertext
        Assert.NotEqual(c1, c2);
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        var key = EncryptionService.DeriveKey("correct", EncryptionService.GenerateSalt());
        var wrongKey = EncryptionService.DeriveKey("wrong", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.Encrypt(key, "secret");
        Assert.ThrowsAny<CryptographicException>(() => EncryptionService.Decrypt(wrongKey, c, iv, tag));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.Encrypt(key, "secret");
        // Flip one byte in the base64
        var tampered = c[..^4] + "AAAA";
        Assert.ThrowsAny<CryptographicException>(() => EncryptionService.Decrypt(key, tampered, iv, tag));
    }

    [Fact]
    public void Decrypt_TamperedTag_ThrowsCryptographicException()
    {
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.Encrypt(key, "secret");
        // Flip a byte in the decoded tag (keep same length so AesGcm doesn't reject size)
        var tagBytes = Convert.FromBase64String(tag);
        tagBytes[0] ^= 0xFF;
        var tamperedTag = Convert.ToBase64String(tagBytes);
        Assert.ThrowsAny<CryptographicException>(() => EncryptionService.Decrypt(key, c, iv, tamperedTag));
    }

    // ── VerifyPassword ───────────────────────────────────────────────────────

    [Fact]
    public void VerifyPassword_CorrectKey_ReturnsTrue()
    {
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.CreateVerification(key);
        Assert.True(EncryptionService.VerifyPassword(key, c, iv, tag));
    }

    [Fact]
    public void VerifyPassword_WrongKey_ReturnsFalse()
    {
        var key = EncryptionService.DeriveKey("correct", EncryptionService.GenerateSalt());
        var wrongKey = EncryptionService.DeriveKey("wrong", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.CreateVerification(key);
        Assert.False(EncryptionService.VerifyPassword(wrongKey, c, iv, tag));
    }

    [Fact]
    public void VerifyPassword_TamperedBlob_ReturnsFalse()
    {
        var key = EncryptionService.DeriveKey("pw", EncryptionService.GenerateSalt());
        var (c, iv, tag) = EncryptionService.CreateVerification(key);
        Assert.False(EncryptionService.VerifyPassword(key, c[..^4] + "AAAA", iv, tag));
    }
}
