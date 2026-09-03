using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace xpaste.Services.Input;

/// <summary>
/// Win32 input plumbing: <c>SendInput</c> dispatch, physical modifier tracking, and the
/// environment probes used to explain <i>why</i> an injection was rejected.
/// <para>
/// <b>Struct sizing:</b> on x64 <c>INPUT</c> must be exactly 40 bytes — <c>DWORD type</c> plus
/// 4 bytes of padding plus a 32-byte union (the size of <c>MOUSEINPUT</c>). If the union is
/// declared smaller, <c>SendInput</c> silently returns 0 and nothing is typed.
/// </para>
/// </summary>
internal static class NativeInput
{
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseDesktop(IntPtr hDesktop);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    // 32 bytes == sizeof(MOUSEINPUT) on x64, which is what defines the union's size.
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP       = 0x0002;
    private const uint KEYEVENTF_UNICODE     = 0x0004;
    private const uint KEYEVENTF_SCANCODE    = 0x0008;

    private const int VK_SHIFT    = 0x10;
    private const int VK_CONTROL  = 0x11;
    private const int VK_MENU     = 0x12;
    private const int VK_LWIN     = 0x5B;
    private const int VK_RWIN     = 0x5C;

    private const int ERROR_ACCESS_DENIED = 5;
    private const uint DESKTOP_READOBJECTS = 0x0001;

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenIntegrityLevel = 25;

    /// <summary>Every modifier virtual key, including the left/right variants, used when force-releasing.</summary>
    private static readonly (ushort Vk, ushort Scan, bool Extended)[] ModifierReleaseKeys =
    {
        (0xA0, 0x2A, false), // VK_LSHIFT
        (0xA1, 0x36, false), // VK_RSHIFT
        (0xA2, 0x1D, false), // VK_LCONTROL
        (0xA3, 0x1D, true),  // VK_RCONTROL
        (0xA4, 0x38, false), // VK_LMENU
        (0xA5, 0x38, true),  // VK_RMENU
        (0x5B, 0x5B, true),  // VK_LWIN
        (0x5C, 0x5C, true),  // VK_RWIN
    };

    /// <summary>Virtual keys polled to decide whether the user is still physically holding the hotkey.</summary>
    private static readonly int[] PolledModifiers = { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN };

    /// <summary>Size of the marshalled <c>INPUT</c> struct; must be 40 on x64 and 28 on x86.</summary>
    internal static int InputStructSize { get; } = Marshal.SizeOf<INPUT>();

    /// <summary><c>true</c> while any Ctrl/Shift/Alt/Win key is physically held down.</summary>
    public static bool AreModifiersHeld()
        => PolledModifiers.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>
    /// Blocks until the user releases the modifiers that triggered the hotkey, or the timeout expires.
    /// <para>
    /// This is essential: <c>Ctrl+Shift+N</c> leaves Ctrl and Shift physically down, and the keyboard
    /// auto-repeats them. Injecting while they are held makes the target see <c>Ctrl+Shift+&lt;key&gt;</c>
    /// instead of the intended character, which is why typing into terminals and RDP produced nothing.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> if the modifiers were released naturally within the timeout.</returns>
    public static bool WaitForModifierRelease(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!AreModifiersHeld()) return true;
            Thread.Sleep(10);
        }

        return !AreModifiersHeld();
    }

    /// <summary>
    /// Synthesises a key-up for every modifier so that a stuck or still-held modifier cannot
    /// corrupt the characters we are about to type.
    /// </summary>
    public static void ForceReleaseModifiers()
    {
        var strokes = ModifierReleaseKeys
            .Select(m => KeyStroke.Up(m.Vk, m.Scan, m.Extended))
            .ToArray();
        Send(strokes);
    }

    /// <summary>
    /// Dispatches one group of key events in a single <c>SendInput</c> call.
    /// </summary>
    /// <returns>The number of events the system accepted.</returns>
    /// <exception cref="Win32Exception">Thrown when the system rejected the whole batch.</exception>
    public static uint Send(IReadOnlyList<KeyStroke> strokes)
    {
        if (strokes.Count == 0) return 0;

        var inputs = new INPUT[strokes.Count];
        for (int i = 0; i < strokes.Count; i++) inputs[i] = ToInput(strokes[i]);

        uint sent = SendInput((uint)inputs.Length, inputs, InputStructSize);
        if (sent != inputs.Length)
        {
            int err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err, $"SendInput accepted {sent}/{inputs.Length} events (error {err}).");
        }

        return sent;
    }

    private static INPUT ToInput(KeyStroke stroke)
    {
        uint flags = stroke.IsKeyUp ? KEYEVENTF_KEYUP : 0;
        ushort vk;
        ushort scan;

        if (stroke.Kind == KeyStrokeKind.Unicode)
        {
            flags |= KEYEVENTF_UNICODE;
            vk = 0;
            scan = stroke.ScanCode;
        }
        else if (stroke.ScanCode != 0)
        {
            // Hardware-style event. KEYEVENTF_SCANCODE makes the OS translate the scan code back to
            // a virtual key itself, which is exactly what a real keyboard produces — and what RDP,
            // VM consoles and terminal emulators forward.
            flags |= KEYEVENTF_SCANCODE;
            if (stroke.IsExtended) flags |= KEYEVENTF_EXTENDEDKEY;
            vk = stroke.VirtualKey;
            scan = stroke.ScanCode;
        }
        else
        {
            // Layout reported no scan code — fall back to a virtual-key event.
            if (stroke.IsExtended) flags |= KEYEVENTF_EXTENDEDKEY;
            vk = stroke.VirtualKey;
            scan = 0;
        }

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new INPUTUNION
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
            }
        };
    }

    /// <summary>Returns <c>true</c> when the given Win32 error means UIPI blocked us.</summary>
    public static bool IsAccessDenied(int win32Error) => win32Error == ERROR_ACCESS_DENIED;

    /// <summary>
    /// Detects the Windows <i>secure desktop</i> (UAC consent, lock screen, Ctrl+Alt+Del).
    /// No user-mode process can inject input there — this exists so the app can say so plainly
    /// instead of failing silently.
    /// </summary>
    public static bool IsSecureDesktopActive()
    {
        IntPtr desktop = IntPtr.Zero;
        try
        {
            desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
            return desktop == IntPtr.Zero;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (desktop != IntPtr.Zero) CloseDesktop(desktop);
        }
    }

    /// <summary>Returns the process name of the window currently holding focus, or <c>null</c>.</summary>
    public static string? GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GetForegroundProcessName failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reports whether the foreground window belongs to a process running at a <b>higher integrity
    /// level</b> than xpaste, which means Windows UIPI will discard our input.
    /// <para>
    /// This must be checked <i>before</i> injecting. <c>SendInput</c> reports success even when
    /// UIPI blocks it — MSDN is explicit that neither the return value nor <c>GetLastError</c>
    /// indicates the failure — so a reactive check could never detect this case.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> only when the target is definitively higher integrity. Anything indeterminate
    /// returns <c>false</c> so that an unknown process is still attempted rather than refused.
    /// </returns>
    public static bool IsForegroundWindowHigherIntegrity()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return false;

            uint? target = GetIntegrityLevel(pid);
            uint? ours = GetIntegrityLevel((uint)Environment.ProcessId);
            if (target is null || ours is null) return false;

            return target > ours;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Integrity-level check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads a process's mandatory integrity level RID (0x2000 medium, 0x3000 high, 0x4000 system),
    /// or <c>null</c> when it cannot be determined.
    /// </summary>
    private static uint? GetIntegrityLevel(uint processId)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return null;

        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out token)) return null;

            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out uint size);
            if (size == 0) return null;

            buffer = Marshal.AllocHGlobal((int)size);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) return null;

            // TOKEN_MANDATORY_LABEL starts with SID_AND_ATTRIBUTES, whose first field is the PSID.
            IntPtr sid = Marshal.ReadIntPtr(buffer);
            if (sid == IntPtr.Zero) return null;

            byte subAuthorityCount = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            if (subAuthorityCount == 0) return null;

            IntPtr rid = GetSidSubAuthority(sid, (uint)(subAuthorityCount - 1));
            return unchecked((uint)Marshal.ReadInt32(rid));
        }
        catch
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
            CloseHandle(process);
        }
    }
}
