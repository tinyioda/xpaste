using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace xpaste.Services;

/// <summary>
/// Injects text into the currently focused window using the most appropriate method:
/// <list type="bullet">
///   <item><description>
///     <b>RDP windows</b> (mstsc.exe / msrdc.exe): sets the clipboard and sends Ctrl+V.
///     RDP clipboard redirection forwards the clipboard content to the remote session reliably.
///   </description></item>
///   <item><description>
///     <b>All other windows</b>: uses <c>SendInput</c> with <c>KEYEVENTF_UNICODE</c> key events,
///     which types directly without touching the clipboard.
///   </description></item>
/// </list>
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
    private const uint KEYEVENTF_UNICODE  = 0x0004;
    private const uint KEYEVENTF_KEYUP    = 0x0002;

    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V       = 0x56;

    // Known RDP client process names
    private static readonly HashSet<string> RdpProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc",   // built-in Windows RDP client
        "msrdc",   // Microsoft Remote Desktop (Store app)
    };

    /// <summary>
    /// Types <paramref name="text"/> into the currently focused window.
    /// Automatically selects clipboard+Ctrl+V for RDP windows and SendInput for all others.
    /// </summary>
    /// <param name="text">Plain-text string to inject. Must not be null.</param>
    public static void TypeText(string text)
    {
        if (IsForegroundWindowRdp())
        {
            AppLogger.Info($"RDP window detected — using clipboard paste ([REDACTED] {text.Length} chars)");
            TypeViaClipboard(text);
        }
        else
        {
            TypeViaSendInput(text);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the current foreground window belongs to a known RDP client process.
    /// </summary>
    private static bool IsForegroundWindowRdp()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            GetWindowThreadProcessId(hwnd, out uint pid);
            var proc = Process.GetProcessById((int)pid);
            bool isRdp = RdpProcessNames.Contains(proc.ProcessName);
            AppLogger.Info($"Foreground process: {proc.ProcessName} (PID {pid}), isRdp={isRdp}");
            return isRdp;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"IsForegroundWindowRdp failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Pastes text via the clipboard: saves the current clipboard, sets the new content,
    /// sends Ctrl+V, then restores the original clipboard after a short delay.
    /// </summary>
    private static void TypeViaClipboard(string text)
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

            // Send Ctrl+V
            var inputs = new[]
            {
                MakeVkDown(VK_CONTROL),
                MakeVkDown(VK_V),
                MakeVkUp(VK_V),
                MakeVkUp(VK_CONTROL),
            };

            int structSize = Marshal.SizeOf<INPUT>();
            uint sent = SendInput((uint)inputs.Length, inputs, structSize);
            int err = Marshal.GetLastWin32Error();
            AppLogger.Info($"RDP Ctrl+V SendInput: sent={sent}, lastErr={err}");

            // Restore previous clipboard after RDP has had time to sync (~500 ms)
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

    /// <summary>
    /// Types text character-by-character using <c>KEYEVENTF_UNICODE</c> SendInput events.
    /// Releases Ctrl and Shift first so they don't corrupt the injected characters.
    /// </summary>
    private static void TypeViaSendInput(string text)
    {
        var inputs = new List<INPUT>();

        // Release Ctrl and Shift so they don't corrupt the typed text
        foreach (var vk in new[] { VK_CONTROL, VK_SHIFT })
            inputs.Add(MakeVkUp(vk));

        foreach (char c in text)
        {
            inputs.Add(MakeUnicode(c, false));
            inputs.Add(MakeUnicode(c, true));
        }

        var arr = inputs.ToArray();
        int structSize = Marshal.SizeOf<INPUT>();
        uint sent = SendInput((uint)arr.Length, arr, structSize);
        int err = Marshal.GetLastWin32Error();
        AppLogger.Info($"SendInput: structSize={structSize}, requested={arr.Length}, sent={sent}, lastErr={err} ([REDACTED] {text.Length} chars)");
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

    private static INPUT MakeUnicode(char c, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (keyUp ? KEYEVENTF_KEYUP : 0) } }
    };
}
