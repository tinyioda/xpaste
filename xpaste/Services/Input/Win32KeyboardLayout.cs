using System.Runtime.InteropServices;

namespace xpaste.Services.Input;

/// <summary>
/// <see cref="IKeyboardLayoutMap"/> backed by the real Win32 keyboard-layout APIs.
/// <para>
/// The layout is resolved from the <b>foreground window's thread</b> rather than our own, so a
/// snippet is typed using the layout the target window is actually using. Getting this wrong is
/// what turns a password into mojibake on non-US layouts.
/// </para>
/// </summary>
internal sealed class Win32KeyboardLayout : IKeyboardLayoutMap
{
    // CharSet.Unicode is mandatory here. The default (Ansi) would bind to VkKeyScanExA and marshal
    // the char down to the ANSI code page first, silently turning any character outside cp1252
    // into '?' before the lookup ever happens.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char ch, IntPtr dwhkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint MAPVK_VK_TO_VSC = 0;

    private readonly IntPtr _hkl;

    private Win32KeyboardLayout(IntPtr hkl) => _hkl = hkl;

    /// <summary>Creates a layout map for whichever window currently has focus.</summary>
    public static Win32KeyboardLayout ForForegroundWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                uint threadId = GetWindowThreadProcessId(hwnd, out _);
                if (threadId != 0) return new Win32KeyboardLayout(GetKeyboardLayout(threadId));
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not read foreground keyboard layout: {ex.Message}");
        }

        // idThread == 0 means "the calling thread's layout".
        return new Win32KeyboardLayout(GetKeyboardLayout(0));
    }

    /// <inheritdoc/>
    public bool TryMapCharacter(char character, out CharKeyMapping mapping)
    {
        short result = VkKeyScanEx(character, _hkl);
        if (result == -1)
        {
            mapping = default;
            return false;
        }

        ushort virtualKey = (ushort)(result & 0xFF);
        int shiftState = (result >> 8) & 0xFF;

        mapping = new CharKeyMapping(
            virtualKey,
            Shift: (shiftState & 1) != 0,
            Ctrl: (shiftState & 2) != 0,
            Alt: (shiftState & 4) != 0);
        return true;
    }

    /// <inheritdoc/>
    public ushort GetScanCode(ushort virtualKey) => (ushort)MapVirtualKeyEx(virtualKey, MAPVK_VK_TO_VSC, _hkl);
}
