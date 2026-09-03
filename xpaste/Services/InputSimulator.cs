using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using xpaste.Models;
using xpaste.Services.Input;

namespace xpaste.Services;

/// <summary>Outcome of an attempt to inject text into the focused window.</summary>
/// <param name="Success">Whether the text was delivered.</param>
/// <param name="Detail">Human-readable explanation, suitable for the log and a tray notification.</param>
public readonly record struct InjectionResult(bool Success, string Detail)
{
    /// <summary>Creates a successful result.</summary>
    public static InjectionResult Ok(string detail) => new(true, detail);

    /// <summary>Creates a failed result.</summary>
    public static InjectionResult Fail(string detail) => new(false, detail);
}

/// <summary>
/// Injects snippet text into whatever window currently has focus.
/// <para>
/// The default strategy is <b>synthetic keystrokes built from real scan codes</b>. That is the only
/// approach that works uniformly across local password fields, SSH/terminal prompts and Remote
/// Desktop sessions, because it is indistinguishable from a physical keyboard at the point where
/// those consumers read input. It also keeps secrets off the clipboard entirely.
/// </para>
/// <para>
/// Clipboard paste remains available as an explicit opt-in for long, non-sensitive text.
/// </para>
/// </summary>
public static class InputSimulator
{
    /// <summary>
    /// How long to wait for the user to release the Ctrl+Shift hotkey before typing. Injecting while
    /// modifiers are physically held corrupts every character.
    /// </summary>
    private const int ModifierReleaseTimeoutMs = 2000;

    /// <summary>Settle time after the modifiers are released, so the target sees a clean key state.</summary>
    private const int PostModifierSettleMs = 30;

    /// <summary>Per-character pacing for local windows.</summary>
    private const double LocalCharDelayMs = 1.5;

    /// <summary>
    /// Per-character pacing for remote/virtualised targets. These marshal each keystroke over a
    /// wire or into a guest OS and drop input that arrives faster than they can forward it.
    /// </summary>
    private const double RemoteCharDelayMs = 12.0;

    /// <summary>
    /// Foreground processes that tunnel keystrokes to another machine or guest OS and therefore
    /// need slower pacing.
    /// </summary>
    private static readonly HashSet<string> RemoteTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc",           // Remote Desktop Connection
        "msrdc",           // Windows App / Remote Desktop client
        "msrdcw",          // Remote Desktop client window host
        "RdcMan",          // Remote Desktop Connection Manager
        "vmconnect",       // Hyper-V VMConnect
        "vmware",          // VMware Workstation
        "vmware-view",     // VMware Horizon
        "VirtualBoxVM",    // VirtualBox
        "freerdp",
        "wfica32",         // Citrix Workspace
        "vncviewer",
        "TeamViewer",
        "AnyDesk",
    };

    /// <summary>Injects <paramref name="text"/> using the automatic strategy.</summary>
    public static InjectionResult TypeText(string text) => TypeText(text, PasteMethod.Auto);

    /// <summary>
    /// Guards against overlapping injections. Two concurrent runs would interleave their keystrokes
    /// and scramble both snippets â€” unacceptable when one of them is a password.
    /// </summary>
    private static int _busy;

    /// <summary>
    /// Injects <paramref name="text"/> into the focused window.
    /// </summary>
    /// <param name="text">The snippet content to deliver.</param>
    /// <param name="method">Delivery strategy; <see cref="PasteMethod.Auto"/> picks keystrokes.</param>
    /// <remarks>Call this from a background thread â€” it blocks while waiting for modifier release.</remarks>
    public static InjectionResult TypeText(string text, PasteMethod method)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            AppLogger.Warn("An injection is already running â€” ignoring the overlapping hotkey.");
            return InjectionResult.Fail("xpaste is still typing the previous snippet.");
        }

        try
        {
            return Inject(text, method);
        }
        catch (Exception ex)
        {
            // TypeText is awaited from an `async void` hotkey handler, so an escaping exception
            // would be rethrown on the UI thread and terminate the app. Always return a result.
            AppLogger.Error("Injection failed unexpectedly", ex);
            return InjectionResult.Fail($"xpaste could not type the snippet: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static InjectionResult Inject(string text, PasteMethod method)
    {
        if (string.IsNullOrEmpty(text)) return InjectionResult.Fail("Snippet is empty.");

        if (NativeInput.IsSecureDesktopActive())
        {
            const string msg = "Windows is showing a secure screen (UAC prompt, lock screen or Ctrl+Alt+Del). " +
                               "No application can type into it â€” enter the password manually.";
            AppLogger.Warn(msg);
            return InjectionResult.Fail(msg);
        }

        AppLogger.Info($"Injection requested: {method}, target '{NativeInput.GetForegroundProcessName() ?? "unknown"}' " +
                       $"([REDACTED] {text.Length} chars)");

        if (!PrepareKeyboardState())
        {
            const string msg = "Ctrl+Shift is still held down. Release the hotkey and try again â€” " +
                               "typing while a modifier is down would corrupt the snippet.";
            AppLogger.Warn(msg);
            return InjectionResult.Fail(msg);
        }

        // Re-resolve the target after the wait: focus can move while the user releases the hotkey,
        // and both the pacing profile and the keyboard layout depend on who actually has focus now.
        string target = NativeInput.GetForegroundProcessName() ?? "unknown";

        // Must be checked up front. SendInput reports success even when UIPI discards the input,
        // so detecting this after the fact is impossible â€” and a false "typed it" is exactly the
        // silent failure this whole component exists to eliminate.
        if (NativeInput.IsForegroundWindowHigherIntegrity())
        {
            string msg = $"'{target}' runs with higher privileges than xpaste, so Windows silently " +
                         "discards our keystrokes. Use the tray menu â†’ Restart as Administrator, then try again.";
            AppLogger.Warn(msg);
            return InjectionResult.Fail(msg);
        }

        return method == PasteMethod.Clipboard
            ? PasteViaClipboard(text)
            : TypeViaKeystrokes(text, target);
    }

    /// <summary>
    /// Waits for the hotkey's modifiers to be released, then force-releases anything still latched.
    /// <para>
    /// Refusing to type is deliberate. The keyboard auto-repeats a held modifier, so a synthetic
    /// key-up is undone within milliseconds â€” proceeding anyway would push mangled text into a
    /// password field, which is worse than doing nothing.
    /// </para>
    /// </summary>
    /// <returns><c>false</c> if the modifiers were still held when the timeout expired.</returns>
    private static bool PrepareKeyboardState()
    {
        if (!NativeInput.WaitForModifierRelease(ModifierReleaseTimeoutMs))
        {
            AppLogger.Warn($"Modifiers still held after {ModifierReleaseTimeoutMs} ms â€” aborting.");
            return false;
        }

        // Even after a natural release the target may have latched a modifier from the auto-repeat
        // that ran while the hotkey was down, so clear the state explicitly.
        TryForceReleaseModifiers();
        Thread.Sleep(PostModifierSettleMs);
        return true;
    }

    /// <summary>
    /// Types the text character by character using hardware-style scan codes.
    /// </summary>
    private static InjectionResult TypeViaKeystrokes(string text, string target)
    {
        var layout = Win32KeyboardLayout.ForForegroundWindow();
        var plan = KeystrokePlanner.Plan(text, layout);

        int unicodeFallbacks = plan
            .SelectMany(g => g)
            .Count(s => s.Kind == KeyStrokeKind.Unicode && !s.IsKeyUp);

        bool remote = RemoteTargets.Contains(target);
        double delay = remote ? RemoteCharDelayMs : LocalCharDelayMs;

        AppLogger.Info(
            $"Keystroke plan: {plan.Count} groups, {unicodeFallbacks} unicode fallbacks, " +
            $"{delay} ms/char, INPUT struct {NativeInput.InputStructSize} bytes, remote={remote}");

        if (remote && unicodeFallbacks > 0)
            AppLogger.Warn($"{unicodeFallbacks} character(s) have no key on the active layout and may not " +
                           "reach the remote session. Match the local and remote keyboard layouts.");

        try
        {
            for (int i = 0; i < plan.Count; i++)
            {
                NativeInput.Send(plan[i]);
                if (i < plan.Count - 1) Pace(delay);
            }
        }
        catch (Win32Exception ex)
        {
            return InjectionResult.Fail(DescribeSendFailure(ex, target));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Keystroke injection failed", ex);
            return InjectionResult.Fail($"Could not type into '{target}': {ex.Message}");
        }
        finally
        {
            // Never leave a modifier latched in the target window.
            TryForceReleaseModifiers();
        }

        AppLogger.Info($"Typed {plan.Count} characters into '{target}'.");
        return InjectionResult.Ok($"Typed into {target}.");
    }

    /// <summary>Turns a <c>SendInput</c> rejection into an explanation the user can act on.</summary>
    private static string DescribeSendFailure(Win32Exception ex, string target)
    {
        AppLogger.Error($"SendInput rejected while typing into '{target}'", ex);

        if (NativeInput.IsAccessDenied(ex.NativeErrorCode) || NativeInput.IsForegroundWindowHigherIntegrity())
        {
            return $"'{target}' runs with higher privileges than xpaste, so Windows blocked the input. " +
                   "Use the tray menu â†’ Restart as Administrator, then try again.";
        }

        return $"Windows rejected the keystrokes for '{target}': {ex.Message}";
    }

    private static void TryForceReleaseModifiers()
    {
        try { NativeInput.ForceReleaseModifiers(); }
        catch (Exception ex) { AppLogger.Warn($"Could not release modifiers: {ex.Message}"); }
    }

    /// <summary>
    /// Busy-waits for very short delays and sleeps for longer ones. <see cref="Thread.Sleep(int)"/>
    /// has ~15 ms granularity, which would make per-character pacing unusably slow.
    /// </summary>
    private static void Pace(double milliseconds)
    {
        if (milliseconds <= 0) return;
        if (milliseconds >= 10) { Thread.Sleep((int)milliseconds); return; }

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < milliseconds) Thread.SpinWait(40);
    }


    /// <summary>
    /// Copies the text to the clipboard, sends Ctrl+V, then restores whatever was there before.
    /// Only used when the user explicitly selects <see cref="PasteMethod.Clipboard"/>.
    /// </summary>
    private static InjectionResult PasteViaClipboard(string text)
    {
        var app = Application.Current;
        if (app == null) return InjectionResult.Fail("Application is shutting down.");

        string? failure = null;

        // The clipboard is STA-only, so every access is marshalled to the UI thread.
        app.Dispatcher.Invoke(() =>
        {
            CaptureClipboardForRestore();

            try
            {
                Clipboard.SetText(text);

                // Verify the write actually took. If another process is holding the clipboard the
                // set can fail without throwing, and sending Ctrl+V would then paste whatever was
                // there before — potentially a different secret.
                if (!Clipboard.ContainsText() || Clipboard.GetText() != text)
                    failure = "The clipboard did not accept the snippet — another application is holding it.";
            }
            catch (Exception ex) { failure = $"Could not write to the clipboard: {ex.Message}"; }
        });

        if (failure != null)
        {
            AppLogger.Warn(failure);
            ScheduleClipboardRestore(app, text);
            return InjectionResult.Fail(failure);
        }

        const ushort vkControl = 0xA2, scanControl = 0x1D;
        const ushort vkV = 0x56, scanV = 0x2F;

        try
        {
            NativeInput.Send(new[]
            {
                KeyStroke.Down(vkControl, scanControl),
                KeyStroke.Down(vkV, scanV),
                KeyStroke.Up(vkV, scanV),
                KeyStroke.Up(vkControl, scanControl),
            });
        }
        catch (Win32Exception ex)
        {
            ScheduleClipboardRestore(app, text);
            return InjectionResult.Fail(DescribeSendFailure(ex, NativeInput.GetForegroundProcessName() ?? "unknown"));
        }

        ScheduleClipboardRestore(app, text);
        return InjectionResult.Ok("Pasted from clipboard.");
    }

    /// <summary>Serialises access to the pending-restore state below.</summary>
    private static readonly object ClipboardGate = new();

    /// <summary>The user's clipboard contents, held until the pending restore runs.</summary>
    private static IDataObject? _savedClipboard;

    /// <summary>True between capturing the user's clipboard and restoring it.</summary>
    private static bool _restorePending;

    /// <summary>Supersedes the previously scheduled restore when a second paste happens quickly.</summary>
    private static int _restoreGeneration;

    /// <summary>
    /// Snapshots the current clipboard so it can be put back afterwards.
    /// <para>
    /// Deliberately skipped when a restore is already pending: otherwise a second snippet pasted
    /// within the restore window would capture the <i>first snippet</i> as "the user's clipboard"
    /// and strand it there permanently.
    /// </para>
    /// </summary>
    private static void CaptureClipboardForRestore()
    {
        lock (ClipboardGate)
        {
            if (_restorePending) return;
            _savedClipboard = SnapshotClipboard();
            _restorePending = true;
        }
    }

    /// <summary>
    /// Copies every clipboard format into an object we own. The live COM object returned by
    /// <c>GetDataObject</c> becomes useless once we overwrite the clipboard, and copying all
    /// formats preserves images and file drops that <c>GetText</c> would silently discard.
    /// </summary>
    private static IDataObject? SnapshotClipboard()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            if (current == null) return null;

            var snapshot = new DataObject();
            bool captured = false;

            foreach (string format in current.GetFormats())
            {
                try
                {
                    object? data = current.GetData(format);
                    if (data == null) continue;
                    snapshot.SetData(format, data);
                    captured = true;
                }
                catch { /* some formats cannot be read or re-serialised */ }
            }

            return captured ? snapshot : null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not snapshot the clipboard: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Puts the user's clipboard back once the target has had time to read the paste.
    /// </summary>
    /// <param name="ourText">
    /// What we placed on the clipboard. The restore is skipped if the clipboard no longer holds it,
    /// which means something else took ownership and is now the rightful owner.
    /// </param>
    private static void ScheduleClipboardRestore(Application app, string ourText)
    {
        int generation;
        lock (ClipboardGate)
        {
            generation = ++_restoreGeneration;
        }

        Task.Delay(700).ContinueWith(_ =>
        {
            try
            {
                app.Dispatcher.Invoke(() => RestoreClipboard(generation, ourText));
            }
            catch { /* dispatcher shut down */ }
        });
    }

    private static void RestoreClipboard(int generation, string ourText)
    {
        IDataObject? saved;
        lock (ClipboardGate)
        {
            // A newer paste has been scheduled; let its restore do the work instead.
            if (generation != _restoreGeneration) return;
            if (!_restorePending) return;

            saved = _savedClipboard;
            _savedClipboard = null;
            _restorePending = false;
        }

        try
        {
            // Only restore if we still own the clipboard. If anything else has taken it over —
            // including a non-text format the user copied meanwhile — leave it alone.
            if (!Clipboard.ContainsText() || Clipboard.GetText() != ourText) return;

            if (saved != null) Clipboard.SetDataObject(saved, copy: true);
            else Clipboard.Clear();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not restore the clipboard: {ex.Message}");
        }
    }
}
