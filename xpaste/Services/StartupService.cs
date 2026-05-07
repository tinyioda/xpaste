using Microsoft.Win32;

namespace xpaste.Services;

/// <summary>
/// Manages the Windows auto-start registry entry for xpaste.
/// Reads and writes <c>HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run</c>
/// so the app launches automatically at login without requiring administrator privileges.
/// </summary>
public static class StartupService
{
    private const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "xpaste";

    /// <summary>Returns <c>true</c> if an auto-start entry for xpaste exists in the registry.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) != null;
    }

    /// <summary>
    /// Creates (or updates) the auto-start registry value pointing to the current executable.
    /// </summary>
    public static void Enable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
    }

    /// <summary>Removes the auto-start registry value if it exists.</summary>
    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
