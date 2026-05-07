namespace xpaste.Models;

/// <summary>
/// Represents a single stored snippet. The content is always kept in encrypted form;
/// the plain-text is only held in memory by <see cref="xpaste.Services.SnippetStore"/> while the store is unlocked.
/// </summary>
public class Snippet
{
    /// <summary>Unique identifier for this snippet.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable label shown in the management UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Hotkey slot assignment (1–10). Slot 10 maps to <c>Ctrl+Shift+0</c>.
    /// A value of 0 means the snippet is unassigned and cannot be triggered by a hotkey.
    /// </summary>
    public int Slot { get; set; } = 0;

    /// <summary>AES-256-GCM encrypted content, Base64-encoded.</summary>
    public string CipherText { get; set; } = string.Empty;

    /// <summary>AES-GCM nonce (initialisation vector), Base64-encoded.</summary>
    public string IV { get; set; } = string.Empty;

    /// <summary>AES-GCM authentication tag, Base64-encoded.</summary>
    public string Tag { get; set; } = string.Empty;
}
