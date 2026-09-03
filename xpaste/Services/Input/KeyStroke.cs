namespace xpaste.Services.Input;

/// <summary>How a single synthetic key event should be delivered to the system.</summary>
public enum KeyStrokeKind
{
    /// <summary>
    /// A hardware-style scan-code event. This is the only form that survives being forwarded
    /// by Remote Desktop, VM consoles and terminal emulators, because those forward the
    /// <i>scan code</i> rather than the virtual key.
    /// </summary>
    ScanCode,

    /// <summary>
    /// A <c>KEYEVENTF_UNICODE</c> event carrying a single UTF-16 code unit. Used for characters
    /// the active keyboard layout cannot produce. Delivered locally but generally
    /// <b>not</b> forwarded into RDP/VM sessions.
    /// </summary>
    Unicode,
}

/// <summary>
/// A single synthetic key event, expressed independently of the Win32 <c>INPUT</c> structure
/// so that the planning logic can be unit-tested without touching user32.
/// </summary>
/// <param name="Kind">Whether this is a scan-code or Unicode event.</param>
/// <param name="VirtualKey">Virtual-key code (0 for Unicode events).</param>
/// <param name="ScanCode">Scan code, or the UTF-16 code unit for Unicode events.</param>
/// <param name="IsKeyUp">True for a key-release event.</param>
/// <param name="IsExtended">True when the scan code needs the 0xE0 extended prefix (AltGr, Insert, arrows…).</param>
public readonly record struct KeyStroke(
    KeyStrokeKind Kind,
    ushort VirtualKey,
    ushort ScanCode,
    bool IsKeyUp,
    bool IsExtended)
{
    /// <summary>Creates a scan-code key-press event.</summary>
    public static KeyStroke Down(ushort virtualKey, ushort scanCode, bool extended = false)
        => new(KeyStrokeKind.ScanCode, virtualKey, scanCode, IsKeyUp: false, IsExtended: extended);

    /// <summary>Creates a scan-code key-release event.</summary>
    public static KeyStroke Up(ushort virtualKey, ushort scanCode, bool extended = false)
        => new(KeyStrokeKind.ScanCode, virtualKey, scanCode, IsKeyUp: true, IsExtended: extended);

    /// <summary>Creates a Unicode key-press event for a single UTF-16 code unit.</summary>
    public static KeyStroke UnicodeDown(char codeUnit)
        => new(KeyStrokeKind.Unicode, 0, codeUnit, IsKeyUp: false, IsExtended: false);

    /// <summary>Creates a Unicode key-release event for a single UTF-16 code unit.</summary>
    public static KeyStroke UnicodeUp(char codeUnit)
        => new(KeyStrokeKind.Unicode, 0, codeUnit, IsKeyUp: true, IsExtended: false);
}
