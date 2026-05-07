namespace xpaste.Models;

/// <summary>
/// Root object serialised to <c>%AppData%\xpaste\snippets.json</c>.
/// Contains the PBKDF2 salt, a password-verification blob, and the list of encrypted snippets.
/// </summary>
public class SnippetStoreFile
{
    /// <summary>Random PBKDF2 salt used when deriving the AES key, Base64-encoded.</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// AES-GCM ciphertext of a known verification string.
    /// Decrypting this with the correct key and comparing to the expected plaintext
    /// confirms the master password without exposing any snippet content.
    /// </summary>
    public string VerifyCipherText { get; set; } = string.Empty;

    /// <summary>Nonce used when encrypting the verification blob, Base64-encoded.</summary>
    public string VerifyIV { get; set; } = string.Empty;

    /// <summary>GCM authentication tag for the verification blob, Base64-encoded.</summary>
    public string VerifyTag { get; set; } = string.Empty;

    /// <summary>All stored snippets in their encrypted form.</summary>
    public List<Snippet> Snippets { get; set; } = new();
}
