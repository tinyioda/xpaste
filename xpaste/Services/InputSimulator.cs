using System.Runtime.InteropServices;
using System.Windows;

namespace xpaste.Services;

/// <summary>
/// Injects text into the currently focused window using clipboard + Ctrl+V.
/// This method works reliably across all window types including password fields,
/// which intentionally block synthetic <c>SendInput</c> keystrokes for security.
/// The previous clipboard contents are restored after a short delay.
/// <para>
/// <b>Struct sizing note:</b> On 64-bit Windows the <c>INPUT</c> struct must be exactly 40 bytes.
/// The union field is padded to 28 bytes (the size of <c>MOUSEINPUT</c>) to match the native ABI;
/// without this padding <c>SendInput</c> silently returns 0.
/// </para>
/// </summary>
public static class InputSimulator
{
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit, Size = 28)]   // must match sizeof(MOUSEINPUT) on 64-bit so INPUT == 40 bytes
    private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE  = 0x0004;
    private const uint KEYEVENTF_KEYUP    = 0x0002;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V       = 0x56;

    /// <summary>
    /// Pastes <paramref name="text"/> into the currently focused window via clipboard + Ctrl+V.
    /// Works with all window types including password fields.
    /// </summary>
    /// <param name="text">Plain-text string to inject. Must not be null.</param>
    public static void TypeText(string text)
    {
        AppLogger.Info($"Pasting via clipboard ([REDACTED] {text.Length} chars)");
        TypeViaClipboard(text);
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
