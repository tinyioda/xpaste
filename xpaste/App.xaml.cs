using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using xpaste.Services;
using xpaste.ViewModels;
using xpaste.Views;

namespace xpaste;

/// <summary>
/// Application entry point. Manages the tray icon lifecycle, hotkey wiring,
/// master-password prompts, and top-level navigation between hidden/shown states.
/// </summary>
public partial class App : Application
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private TaskbarIcon? _trayIcon;
    private HotkeyService? _hotkeyService;
    private readonly SnippetStore _store = new();
    private MainWindow? _mainWindow;
    private MainViewModel? _vm;
    private Icon? _trayIconImage;
    private MenuItem? _startupItem;

    /// <summary>Bootstraps the application: prompts for the master password, builds the tray icon, registers hotkeys, and shows the main window.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLogger.Info("=== xpaste starting ===");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!_store.HasStore)
        {
            var dlg = new MasterPasswordDialog(_store, isFirstLaunch: true);
            if (dlg.ShowDialog() != true) { Shutdown(); return; }
        }
        else
        {
            var dlg = new MasterPasswordDialog(_store, isFirstLaunch: false);
            if (dlg.ShowDialog() != true) { Shutdown(); return; }
        }

        _vm = new MainViewModel(_store);
        _mainWindow = new MainWindow(_vm);
        MainWindow = _mainWindow;

        // Retrieve the TaskbarIcon declared in App.xaml resources and set its icon
        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIconImage = CreateTrayIcon();
        _trayIcon.Icon = _trayIconImage;
        _trayIcon.ForceCreate();

        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "Open xpaste" };
        openItem.Click += (_, _) => ShowMain();
        var changePwdItem = new MenuItem { Header = "Change Master Password…" };
        changePwdItem.Click += (_, _) => ChangePassword();
        var startupItem = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = StartupService.IsEnabled()
        };
        _startupItem = startupItem;
        startupItem.Click += (_, _) =>
        {
            if (StartupService.IsEnabled())
                StartupService.Disable();
            else
                StartupService.Enable();
            var enabled = StartupService.IsEnabled();
            startupItem.IsChecked = enabled;
            if (_vm != null) _vm.AutoStart = enabled;
        };        var logItem = new MenuItem { Header = "View Log" };
        logItem.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe",
            $"/select,\"{System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xpaste", "xpaste.log")}\"");
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(openItem);
        menu.Items.Add(changePwdItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(startupItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(logItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenu = menu;
        _trayIcon.TrayLeftMouseUp += (_, _) => ShowMain();
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMain();

        _mainWindow.Show();
        _hotkeyService = new HotkeyService();
        _hotkeyService.Register();
        _hotkeyService.ToggleActivated += ToggleMain;
        _hotkeyService.MinimizeActivated += () => _mainWindow?.Hide();
        _hotkeyService.SlotActivated += OnSlotActivated;
    }

    /// <summary>Shows and activates the main window, prompting to unlock first if necessary.</summary>
    private void ShowMain()
    {
        if (!_store.IsUnlocked)
        {
            var dlg = new MasterPasswordDialog(_store, isFirstLaunch: false);
            if (dlg.ShowDialog() != true) return;
            _vm?.Refresh();
        }

        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    /// <summary>Toggles the main window between visible and hidden.</summary>
    private void ToggleMain()
    {
        if (_mainWindow?.IsVisible == true)
            _mainWindow.Hide();
        else
            ShowMain();
    }

    /// <summary>
    /// Handles a hotkey slot activation. Yields off the WM_HOTKEY handler via a short delay
    /// before calling <see cref="InputSimulator.TypeText"/> to avoid SendInput being blocked.
    /// </summary>
    private async void OnSlotActivated(int slot)
    {
        AppLogger.Info($"OnSlotActivated: slot={slot}, storeUnlocked={_store.IsUnlocked}");
        if (!_store.IsUnlocked) { AppLogger.Warn("Store is locked — ignoring slot activation"); return; }

        var content = _store.GetContentBySlot(slot);
        if (string.IsNullOrEmpty(content)) { AppLogger.Warn($"No snippet assigned to slot {slot}"); return; }

        AppLogger.Info($"Scheduling TypeText for slot {slot} ([REDACTED] {content.Length} chars)");
        await Task.Delay(50);
        AppLogger.Info($"Calling TypeText for slot {slot}");
        InputSimulator.TypeText(content);
    }

    /// <summary>Syncs the tray "Start with Windows" checkmark to the given value.</summary>
    internal void SyncStartupTrayItem(bool enabled)
    {
        if (_startupItem != null) _startupItem.IsChecked = enabled;
    }

    /// <summary>Opens the change-password dialog, unlocking the store first if needed.</summary>
    private void ChangePassword()
    {
        if (!_store.IsUnlocked)
        {
            var unlockDlg = new MasterPasswordDialog(_store, isFirstLaunch: false);
            if (unlockDlg.ShowDialog() != true) return;
            _vm?.Refresh();
        }

        var dlg = new ChangePasswordDialog(_store);
        if (dlg.ShowDialog() == true)
        {
            AppLogger.Info("Master password changed successfully");
            MessageBox.Show("Master password changed successfully.", "xpaste",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Gracefully shuts down hotkeys, the tray icon, and the main window.</summary>
    private void ExitApp()
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        if (_mainWindow != null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }

        Shutdown();
    }

    /// <summary>Final cleanup on application exit.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _trayIconImage?.Dispose();
        base.OnExit(e);
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 150, 136));
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new System.Drawing.Font("Arial", 11f, System.Drawing.FontStyle.Bold, GraphicsUnit.Point);
            using var textBrush = new SolidBrush(System.Drawing.Color.White);
            var sf = new System.Drawing.StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("XP", font, textBrush, new System.Drawing.RectangleF(0, 0, 32, 32), sf);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
