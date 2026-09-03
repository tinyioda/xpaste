namespace xpaste.Models;

/// <summary>
/// How a snippet is delivered to the focused window.
/// <para>
/// Serialised by value, so the numeric ordering must remain stable across releases.
/// </para>
/// </summary>
public enum PasteMethod
{
    /// <summary>
    /// Let xpaste choose. Currently always resolves to <see cref="Keystrokes"/>, which is the only
    /// method that works in RDP sessions, SSH/terminal password prompts and Windows password fields.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Type the snippet one character at a time as hardware-style scan codes. Works everywhere the
    /// user could type by hand, and never places the secret on the clipboard.
    /// </summary>
    Keystrokes = 1,

    /// <summary>
    /// Copy to the clipboard and send Ctrl+V. Fast for long, non-sensitive text, but the content is
    /// briefly readable by every process on the machine and it does not work in RDP credential
    /// prompts or terminals that ignore Ctrl+V.
    /// </summary>
    Clipboard = 2,
}
