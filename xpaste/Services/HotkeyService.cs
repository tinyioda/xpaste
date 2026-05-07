using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace xpaste.Services;

/// <summary>
/// Registers and manages system-wide hotkeys for xpaste.
/// <para>
/// Uses <c>RegisterHotKey(IntPtr.Zero, …)</c> so that <c>WM_HOTKEY</c> messages are posted
/// to the thread message queue rather than a specific window handle. This means hotkeys fire
/// reliably even when all windows are hidden.
/// <see cref="ComponentDispatcher.ThreadFilterMessage"/> intercepts the message on the WPF UI
/// thread before it reaches any window procedure.
/// </para>
/// </summary>
public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_CONTROL  = 0x0002;
    private const uint MOD_SHIFT    = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // VK codes for slots 1–9 then 0 (slot 10)
    private static readonly uint[] SlotVKeys = { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30 };
    private const uint VK_OEM_PLUS  = 0xBB; // the +/= key (Ctrl+Shift makes it toggle open)
    private const uint VK_OEM_MINUS = 0xBD; // the -/_ key (Ctrl+Shift makes it minimize)

    private const int ID_TOGGLE   = 100;
    private const int ID_MINIMIZE = 111;
    // Slot hotkey IDs: 101–110

    /// <summary>Fired when a snippet slot hotkey is pressed. The argument is the slot number (1–10).</summary>
    public event Action<int>? SlotActivated;

    /// <summary>Fired when <c>Ctrl+Shift++</c> is pressed to toggle the management window.</summary>
    public event Action? ToggleActivated;

    /// <summary>Fired when <c>Ctrl+Shift+-</c> is pressed to minimize the app to the tray.</summary>
    public event Action? MinimizeActivated;

    private bool _registered;

    /// <summary>
    /// Registers all hotkeys with the system. Must be called once after the WPF message loop has started.
    /// Logs the result of each <c>RegisterHotKey</c> call.
    /// </summary>
    public void Register()
    {
        // IntPtr.Zero → WM_HOTKEY is delivered to the thread queue, not a specific HWND.
        bool ok = RegisterHotKey(IntPtr.Zero, ID_TOGGLE, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_OEM_PLUS);
        AppLogger.Info($"RegisterHotKey toggle (Ctrl+Shift+Plus): {(ok ? "OK" : $"FAILED err={Marshal.GetLastWin32Error()}")}");
        ok = RegisterHotKey(IntPtr.Zero, ID_MINIMIZE, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_OEM_MINUS);
        AppLogger.Info($"RegisterHotKey minimize (Ctrl+Shift+Minus): {(ok ? "OK" : $"FAILED err={Marshal.GetLastWin32Error()}")}");
        for (int i = 0; i < 10; i++)
        {
            ok = RegisterHotKey(IntPtr.Zero, 101 + i, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, SlotVKeys[i]);
            AppLogger.Info($"RegisterHotKey slot {i + 1} (vk=0x{SlotVKeys[i]:X2}): {(ok ? "OK" : $"FAILED err={Marshal.GetLastWin32Error()}")}");
        }

        ComponentDispatcher.ThreadFilterMessage += OnThreadMessage;
        _registered = true;
    }

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg.message != WM_HOTKEY) return;

        int id = (int)msg.wParam;
        AppLogger.Info($"WM_HOTKEY received id={id}");
        if (id == ID_TOGGLE)
        {
            ToggleActivated?.Invoke();
            handled = true;
        }
        else if (id == ID_MINIMIZE)
        {
            MinimizeActivated?.Invoke();
            handled = true;
        }
        else if (id >= 101 && id <= 110)
        {
            int slot = id - 100;
            AppLogger.Info($"Slot hotkey fired: slot={slot}");
            SlotActivated?.Invoke(slot);
            handled = true;
        }
    }

    public void Dispose()
    {
        if (!_registered) return;
        ComponentDispatcher.ThreadFilterMessage -= OnThreadMessage;
        UnregisterHotKey(IntPtr.Zero, ID_TOGGLE);
        UnregisterHotKey(IntPtr.Zero, ID_MINIMIZE);
        for (int i = 0; i < 10; i++)
            UnregisterHotKey(IntPtr.Zero, 101 + i);
        _registered = false;
    }
}
