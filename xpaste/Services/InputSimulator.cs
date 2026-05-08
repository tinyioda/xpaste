using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace xpaste.Services;

/// <summary>
/// Injects text into the currently focused window via clipboard paste.
/// Automatically selects the correct paste keystroke for the target window:
/// <list type="bullet">
///   <item><description><b>PuTTY / KiTTY</b>: Shift+Insert (PuTTY does not support Ctrl+V)</description></item>
///   <item><description><b>All other windows</b>: Ctrl+V</description></item>
/// </list>
/// Previous clipboard contents are restored after a short delay.
/// <para>
/// <b>Struct sizing note:</b> On 64-bit Windows the <c>INPUT</c> struct must be exactly 40 bytes.
/// The union field is padded to 28 bytes (the size of <c>MOUSEINPUT</c>) to match the native ABI;
/// without this padding <c>SendInput</c> silently returns 0.
/// </para>
/// </summary>
public static class InputSimulator
{
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit, Size = 28)]   // must match sizeof(MOUSEINPUT) on 64-bit so INPUT == 40 bytes
    private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_INSERT  = 0x2D;
    private const ushort VK_V       = 0x56;

    // Terminals that use Shift+Insert instead of Ctrl+V for paste
    private static readonly HashSet<string> ShiftInsertProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "putty",     // PuTTY
        "kitty",     // KiTTY (PuTTY fork)
        "puttycyg",  // PuTTYcyg
    };

    /// <summary>
    /// Pastes <paramref name="text"/> into the currently focused window via clipboard,
    /// using the appropriate paste keystroke for the target application.
    /// </summary>
    public static void TypeText(string text)
    {
        bool useShiftInsert = IsForegroundProcessShiftInsert();
        AppLogger.Info($"Pasting via clipboard+{(useShiftInsert ? "Shift+Insert" : "Ctrl+V")} ([REDACTED] {text.Length} chars)");
        TypeViaClipboard(text, useShiftInsert);
    }

    private static bool IsForegroundProcessShiftInsert()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            var proc = Process.GetProcessById((int)pid);
            bool match = ShiftInsertProcesses.Contains(proc.ProcessName);
            AppLogger.Info($"Foreground process: {proc.ProcessName} (PID {pid}), shiftInsert={match}");
            return match;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IsForegroundProcessShiftInsert failed: {ex.Message}");
            return false;
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
                ? [MakeVkDown(VK_SHIFT), MakeVkDown(VK_INSERT), MakeVkUp(VK_INSERT), MakeVkUp(VK_SHIFT)]
                : [MakeVkDown(VK_CONTROL), MakeVkDown(VK_V), MakeVkUp(VK_V), MakeVkUp(VK_CONTROL)];

            int structSize = Marshal.SizeOf<INPUT>();
            uint sent = SendInput((uint)inputs.Length, inputs, structSize);
            int err = Marshal.GetLastWin32Error();
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
