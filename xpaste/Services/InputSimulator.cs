using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace xpaste.Services;

/// <summary>
/// Injects text into the currently focused window using the best available method
/// for the target application:
/// <list type="bullet">
///   <item><description><b>mstsc (Remote Desktop)</b>: WM_CHAR posted directly to the focused
///   control HWND — bypasses the synthetic-input security that blocks SendInput in the RDP
///   password field.</description></item>
///   <item><description><b>PuTTY / KiTTY</b>: clipboard + Shift+Insert (PuTTY does not support Ctrl+V)</description></item>
///   <item><description><b>All other windows</b>: clipboard + Ctrl+V</description></item>
/// </list>
/// Previous clipboard contents are restored after a short delay (clipboard paths only).
/// <para>
/// <b>Struct sizing note:</b> On 64-bit Windows the <c>INPUT</c> struct must be exactly 40 bytes.
/// The union field is padded to 28 bytes (the size of <c>MOUSEINPUT</c>) to match the native ABI;
/// without this padding <c>SendInput</c> silently returns 0.
/// </para>
/// </summary>
public static class InputSimulator
{
    [DllImport("user32.dll", SetLastError = true)] private static extern uint   SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]                      private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]                      private static extern uint   GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]                      private static extern bool   GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
    [DllImport("user32.dll")]                      private static extern bool   PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit, Size = 28)]   // must match sizeof(MOUSEINPUT) on 64-bit so INPUT == 40 bytes
    private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint   cbSize;
        public uint   flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT   rcCaret;
    }

    private const uint INPUT_KEYBOARD  = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint WM_CHAR         = 0x0102;

    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_INSERT  = 0x2D;
    private const ushort VK_V       = 0x56;

    private enum PasteMode { CtrlV, ShiftInsert, WmChar }

    // mstsc password field blocks all synthetic SendInput — post WM_CHAR directly
    private static readonly HashSet<string> WmCharProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc",  // Remote Desktop Connection
    };

    // Terminals that use Shift+Insert instead of Ctrl+V
    private static readonly HashSet<string> ShiftInsertProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "putty",     // PuTTY
        "kitty",     // KiTTY (PuTTY fork)
        "puttycyg",  // PuTTYcyg
    };

    /// <summary>
    /// Injects <paramref name="text"/> into the currently focused window using the
    /// best method for the target application.
    /// </summary>
    public static void TypeText(string text)
    {
        PasteMode mode = GetPasteMode();
        AppLogger.Info($"Pasting via {mode} ({text.Length} chars)");

        if (mode == PasteMode.WmChar)
            TypeViaWmChar(text);
        else
            TypeViaClipboard(text, mode == PasteMode.ShiftInsert);
    }

    private static PasteMode GetPasteMode()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return PasteMode.CtrlV;
            GetWindowThreadProcessId(hwnd, out uint pid);
            var proc = Process.GetProcessById((int)pid);
            AppLogger.Info($"Foreground process: {proc.ProcessName} (PID {pid})");

            if (WmCharProcesses.Contains(proc.ProcessName))      return PasteMode.WmChar;
            if (ShiftInsertProcesses.Contains(proc.ProcessName)) return PasteMode.ShiftInsert;
            return PasteMode.CtrlV;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"GetPasteMode failed: {ex.Message}");
            return PasteMode.CtrlV;
        }
    }

    /// <summary>
    /// Posts <c>WM_CHAR</c> messages directly to the focused control HWND in the foreground
    /// window's thread. This bypasses the synthetic-input security that blocks <c>SendInput</c>
    /// in the mstsc password field.
    /// </summary>
    private static void TypeViaWmChar(string text)
    {
        // Release Ctrl+Shift held by the hotkey so they do not interfere
        var modRelease = new[] { MakeVkUp(VK_CONTROL), MakeVkUp(VK_SHIFT) };
        SendInput(2, modRelease, Marshal.SizeOf<INPUT>());

        try
        {
            // Locate the focused child control within the foreground window's thread
            var    hwnd     = GetForegroundWindow();
            uint   threadId = GetWindowThreadProcessId(hwnd, out uint _);
            var    gti      = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            GetGUIThreadInfo(threadId, ref gti);
            IntPtr target   = gti.hwndFocus != IntPtr.Zero ? gti.hwndFocus : hwnd;

            AppLogger.Info($"PostMessage WM_CHAR to hwnd=0x{target:X}, {text.Length} chars");
            foreach (char c in text)
                PostMessage(target, WM_CHAR, (IntPtr)c, (IntPtr)1);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"TypeViaWmChar failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the clipboard to <paramref name="text"/>, sends the appropriate paste keystroke,
    /// then restores the previous clipboard contents after a short delay.
    /// </summary>
    private static void TypeViaClipboard(string text, bool useShiftInsert)
    {
        // Must run on STA thread — use Dispatcher
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            string? previousText = null;
            try { previousText = Clipboard.GetText(); } catch { }

            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Clipboard.SetText failed: {ex.Message}");
                return;
            }

            INPUT[] inputs = useShiftInsert
                // Release Ctrl (held by hotkey), then Shift+Insert
                ? [MakeVkUp(VK_CONTROL), MakeVkDown(VK_SHIFT), MakeVkDown(VK_INSERT), MakeVkUp(VK_INSERT), MakeVkUp(VK_SHIFT)]
                // Release Ctrl+Shift (held by hotkey), then Ctrl+V
                : [MakeVkUp(VK_CONTROL), MakeVkUp(VK_SHIFT), MakeVkDown(VK_CONTROL), MakeVkDown(VK_V), MakeVkUp(VK_V), MakeVkUp(VK_CONTROL)];

            int  structSize = Marshal.SizeOf<INPUT>();
            uint sent       = SendInput((uint)inputs.Length, inputs, structSize);
            int  err        = Marshal.GetLastWin32Error();
            AppLogger.Info($"SendInput paste: sent={sent}, lastErr={err}");

            // Restore previous clipboard after a short delay
            Task.Delay(500).ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (previousText != null)
                            Clipboard.SetText(previousText);
                        else
                            Clipboard.Clear();
                    }
                    catch { }
                });
            });
        });
    }

    private static INPUT MakeVkDown(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk } }
    };

    private static INPUT MakeVkUp(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
    };
}
