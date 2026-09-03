using xpaste.Services.Input;

namespace xpaste.Tests;

/// <summary>
/// A deterministic stand-in for a US-English keyboard layout.
/// <para>
/// Lets <see cref="KeystrokePlanner"/> be tested without user32, and — more importantly — pins down
/// the scan codes, which are the detail that decides whether a snippet survives being forwarded
/// into an RDP session.
/// </para>
/// </summary>
internal sealed class FakeUsKeyboardLayout : IKeyboardLayoutMap
{
    /// <summary>Set-1 scan codes for the keys this fake supports.</summary>
    private static readonly Dictionary<ushort, ushort> ScanCodes = new()
    {
        [0x41] = 0x1E, [0x42] = 0x30, [0x43] = 0x2E, [0x44] = 0x20, [0x45] = 0x12, // A B C D E
        [0x46] = 0x21, [0x47] = 0x22, [0x48] = 0x23, [0x49] = 0x17, [0x4A] = 0x24, // F G H I J
        [0x4B] = 0x25, [0x4C] = 0x26, [0x4D] = 0x32, [0x4E] = 0x31, [0x4F] = 0x18, // K L M N O
        [0x50] = 0x19, [0x51] = 0x10, [0x52] = 0x13, [0x53] = 0x1F, [0x54] = 0x14, // P Q R S T
        [0x55] = 0x16, [0x56] = 0x2F, [0x57] = 0x11, [0x58] = 0x2D, [0x59] = 0x15, // U V W X Y
        [0x5A] = 0x2C,                                                             // Z
        [0x30] = 0x0B, [0x31] = 0x02, [0x32] = 0x03, [0x33] = 0x04, [0x34] = 0x05, // 0 1 2 3 4
        [0x35] = 0x06, [0x36] = 0x07, [0x37] = 0x08, [0x38] = 0x09, [0x39] = 0x0A, // 5 6 7 8 9
        [0x20] = 0x39,                                                             // Space
        [KeystrokePlanner.VkReturn] = 0x1C,
        [KeystrokePlanner.VkTab] = 0x0F,
        [KeystrokePlanner.VkLShift] = 0x2A,
        [KeystrokePlanner.VkRMenu] = 0x38,
    };

    /// <summary>Virtual key for the digit row, used for the shifted symbols above it.</summary>
    private static readonly Dictionary<char, ushort> ShiftedSymbols = new()
    {
        ['!'] = 0x31, ['@'] = 0x32, ['#'] = 0x33, ['$'] = 0x34, ['%'] = 0x35,
        ['^'] = 0x36, ['&'] = 0x37, ['*'] = 0x38, ['('] = 0x39, [')'] = 0x30,
    };

    public bool TryMapCharacter(char character, out CharKeyMapping mapping)
    {
        // AltGr character, as produced by e.g. the US-International layout.
        if (character == '€')
        {
            mapping = new CharKeyMapping(0x35, Shift: false, Ctrl: true, Alt: true);
            return true;
        }

        // Control characters map to Ctrl+letter — a real shortcut, never a printable character.
        if (character == '\u0003')
        {
            mapping = new CharKeyMapping(0x43, Shift: false, Ctrl: true, Alt: false);
            return true;
        }

        if (character is >= 'a' and <= 'z')
        {
            mapping = new CharKeyMapping((ushort)(character - 'a' + 0x41), Shift: false, Ctrl: false, Alt: false);
            return true;
        }

        if (character is >= 'A' and <= 'Z')
        {
            mapping = new CharKeyMapping((ushort)(character - 'A' + 0x41), Shift: true, Ctrl: false, Alt: false);
            return true;
        }

        if (character is >= '0' and <= '9')
        {
            mapping = new CharKeyMapping((ushort)(character - '0' + 0x30), Shift: false, Ctrl: false, Alt: false);
            return true;
        }

        if (character == ' ')
        {
            mapping = new CharKeyMapping(0x20, Shift: false, Ctrl: false, Alt: false);
            return true;
        }

        if (ShiftedSymbols.TryGetValue(character, out ushort vk))
        {
            mapping = new CharKeyMapping(vk, Shift: true, Ctrl: false, Alt: false);
            return true;
        }

        mapping = default;
        return false;
    }

    public ushort GetScanCode(ushort virtualKey)
        => ScanCodes.TryGetValue(virtualKey, out ushort scan) ? scan : (ushort)0;
}
