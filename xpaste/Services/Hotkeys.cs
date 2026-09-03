namespace xpaste.Services;

/// <summary>
/// Single source of truth for the hotkey combination xpaste uses.
/// <para>
/// The modifier appears in the registration (<see cref="HotkeyService"/>), the snippet list badges,
/// the edit-form dropdown, validation messages, the footer and the help overlay. Keeping one
/// definition here is what stops the UI from advertising a shortcut the app never registered.
/// </para>
/// <para>
/// <b>Alt+Shift</b> is used rather than Ctrl+Shift because Ctrl+Shift+&lt;digit&gt; is claimed by a
/// large number of applications (browsers, IDEs, Office), which intercept the key before a global
/// hotkey ever sees it.
/// </para>
/// </summary>
public static class Hotkeys
{
    /// <summary>The modifier combination every xpaste hotkey requires.</summary>
    public const string ModifierLabel = "Alt+Shift";

    /// <summary>Label for the window toggle hotkey.</summary>
    public const string ToggleLabel = ModifierLabel + "++";

    /// <summary>Label for the minimize-to-tray hotkey (en dash reads better in the UI).</summary>
    public const string MinimizeLabel = ModifierLabel + "+–";

    /// <summary>Label covering the whole range of snippet slots.</summary>
    public const string SlotRangeLabel = ModifierLabel + "+1 through 0";

    /// <summary>Reminder shown at the top of the help overlay.</summary>
    public const string ModifierRequirementLabel = "All hotkeys require " + ModifierLabel;

    /// <summary>One-line summary shown in the main window footer.</summary>
    public const string FooterSummary =
        ModifierLabel + "+1–0  type snippet  •  " +
        ModifierLabel + "++  open  •  " +
        ModifierLabel + "+-  minimize";

    /// <summary>
    /// Returns the display label for a snippet slot, e.g. <c>5</c> → <c>"Alt+Shift+5"</c> and
    /// <c>10</c> → <c>"Alt+Shift+0"</c>. Any value outside 1–10 is <c>"Unassigned"</c>.
    /// </summary>
    public static string ForSlot(int slot)
        => slot is < 1 or > 10
            ? "Unassigned"
            : $"{ModifierLabel}+{(slot == 10 ? "0" : slot.ToString())}";
}
