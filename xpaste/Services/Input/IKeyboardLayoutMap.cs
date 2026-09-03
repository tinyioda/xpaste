namespace xpaste.Services.Input;

/// <summary>
/// The key combination that produces a given character on a particular keyboard layout.
/// </summary>
/// <param name="VirtualKey">Virtual-key code of the base key.</param>
/// <param name="Shift">Shift must be held.</param>
/// <param name="Ctrl">Ctrl must be held (together with <c>Alt</c> this means AltGr).</param>
/// <param name="Alt">Alt must be held (together with <c>Ctrl</c> this means AltGr).</param>
public readonly record struct CharKeyMapping(ushort VirtualKey, bool Shift, bool Ctrl, bool Alt);

/// <summary>
/// Abstraction over the Win32 keyboard-layout APIs (<c>VkKeyScanEx</c> / <c>MapVirtualKeyEx</c>).
/// Exists so <see cref="KeystrokePlanner"/> can be unit-tested against a deterministic fake layout.
/// </summary>
public interface IKeyboardLayoutMap
{
    /// <summary>
    /// Resolves the key combination that types <paramref name="character"/> on this layout.
    /// </summary>
    /// <returns><c>false</c> when the layout cannot produce the character at all.</returns>
    bool TryMapCharacter(char character, out CharKeyMapping mapping);

    /// <summary>
    /// Returns the scan code for <paramref name="virtualKey"/>, or <c>0</c> when the key
    /// does not exist on this layout.
    /// </summary>
    ushort GetScanCode(ushort virtualKey);
}
