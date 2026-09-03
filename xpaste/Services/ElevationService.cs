using System.Diagnostics;
using System.Security.Principal;

namespace xpaste.Services;

/// <summary>
/// Reports and changes the process integrity level.
/// <para>
/// Windows User Interface Privilege Isolation (UIPI) forbids a medium-integrity process from
/// sending input to a higher-integrity window. If the target application runs elevated — an admin
/// console, an elevated Remote Desktop client, an installer — xpaste must run elevated too or
/// every keystroke is silently discarded.
/// </para>
/// </summary>
public static class ElevationService
{
    /// <summary><c>true</c> when the current process is running with an elevated administrator token.</summary>
    public static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not determine elevation state: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Relaunches xpaste with the <c>runas</c> verb so Windows prompts for elevation.
    /// </summary>
    /// <returns><c>true</c> if the elevated instance started and the caller should now exit.</returns>
    public static bool TryRestartElevated()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            AppLogger.Error("Cannot restart elevated: executable path is unknown.");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" });
            AppLogger.Info("Elevated instance started; shutting down the current one.");
            return true;
        }
        catch (Exception ex)
        {
            // The most common case is the user declining the UAC prompt.
            AppLogger.Warn($"Elevated restart cancelled or failed: {ex.Message}");
            return false;
        }
    }
}
