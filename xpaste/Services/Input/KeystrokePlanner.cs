namespace xpaste.Services.Input;

/// <summary>
/// Translates a string into the sequence of synthetic key events required to type it on a
/// given keyboard layout.
/// <para>
/// This class is deliberately pure (no P/Invoke, no state) so the translation rules are
/// unit-testable. The Win32 side lives in <see cref="NativeInput"/>.
/// </para>
/// <para>
/// <b>Why scan codes matter:</b> Remote Desktop, Hyper-V/VMware consoles and most terminal
/// emulators forward the <i>scan code</i> of a key event, not its virtual key. A synthetic
/// event with <c>wScan == 0</c> therefore arrives at the remote end as "no key at all", which
/// is why virtual-key-only injection silently does nothing over RDP.
/// </para>
/// </summary>
public static class KeystrokePlanner
{
    /// <summary>Left Shift. Used as the shift modifier because it exists on every layout.</summary>
    public const ushort VkLShift = 0xA0;

    /// <summary>Right Alt. With the extended flag this is AltGr, which the OS expands to Ctrl+Alt.</summary>
    public const ushort VkRMenu = 0xA5;

    /// <summary>Enter.</summary>
    public const ushort VkReturn = 0x0D;

    /// <summary>Tab.</summary>
    public const ushort VkTab = 0x09;

    /// <summary>
    /// Virtual keys whose scan codes require the 0xE0 extended prefix. Printable characters never
    /// fall in this set, but Enter-on-numpad, Insert and AltGr do.
    /// </summary>
    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0x2D, // VK_INSERT
        0x2E, // VK_DELETE
        0x24, // VK_HOME
        0x23, // VK_END
        0x21, // VK_PRIOR
        0x22, // VK_NEXT
        0x25, // VK_LEFT
        0x26, // VK_UP
        0x27, // VK_RIGHT
        0x28, // VK_DOWN
        0xA3, // VK_RCONTROL
        0xA5, // VK_RMENU
    };

    /// <summary>
    /// Plans the key events needed to type <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="layout">Keyboard layout used to resolve characters to keys.</param>
    /// <returns>
    /// One group per source character (surrogate pairs and <c>\r\n</c> collapse into a single
    /// group). Grouping matters: each group is dispatched as one <c>SendInput</c> call so that a
    /// key and its modifiers can never be split across a pacing delay.
    /// </returns>
    public static IReadOnlyList<IReadOnlyList<KeyStroke>> Plan(string text, IKeyboardLayoutMap layout)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(layout);

        var groups = new List<IReadOnlyList<KeyStroke>>(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // Collapse CRLF into a single Enter so multi-line snippets do not double-submit.
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                groups.Add(PlanVirtualKey(VkReturn, layout));
                continue;
            }

            if (c == '\n') { groups.Add(PlanVirtualKey(VkReturn, layout)); continue; }
            if (c == '\t') { groups.Add(PlanVirtualKey(VkTab, layout)); continue; }

            // Astral-plane characters have no key on any layout — emit both halves together.
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                groups.Add(new[]
                {
                    KeyStroke.UnicodeDown(c),  KeyStroke.UnicodeUp(c),
                    KeyStroke.UnicodeDown(text[i + 1]), KeyStroke.UnicodeUp(text[i + 1]),
                });
                i++;
                continue;
            }

            groups.Add(TryPlanScanCode(c, layout, out var scanGroup)
                ? scanGroup
                : new[] { KeyStroke.UnicodeDown(c), KeyStroke.UnicodeUp(c) });
        }

        return groups;
    }

    /// <summary>Flattens the grouped plan into a single ordered sequence of key events.</summary>
    public static IReadOnlyList<KeyStroke> Flatten(IReadOnlyList<IReadOnlyList<KeyStroke>> groups)
        => groups.SelectMany(g => g).ToList();

    /// <summary>
    /// Builds the scan-code key events for a character, or returns <c>false</c> when the layout
    /// cannot type it safely (in which case the caller falls back to Unicode injection).
    /// </summary>
    private static bool TryPlanScanCode(char c, IKeyboardLayoutMap layout, out IReadOnlyList<KeyStroke> group)
    {
        group = Array.Empty<KeyStroke>();

        if (!layout.TryMapCharacter(c, out var map)) return false;

        // Ctrl+Alt together is AltGr, which is a legitimate way to type a character.
        // Ctrl-alone or Alt-alone would be an application shortcut, never a printable character —
        // synthesising those could trigger arbitrary commands in the target window.
        bool altGr = map.Ctrl && map.Alt;
        if ((map.Ctrl || map.Alt) && !altGr) return false;

        ushort keyScan = layout.GetScanCode(map.VirtualKey);
        if (keyScan == 0) return false;

        ushort shiftScan = 0;
        if (map.Shift)
        {
            shiftScan = layout.GetScanCode(VkLShift);
            if (shiftScan == 0) return false;
        }

        ushort altGrScan = 0;
        if (altGr)
        {
            altGrScan = layout.GetScanCode(VkRMenu);
            if (altGrScan == 0) return false;
        }

        bool keyExtended = ExtendedKeys.Contains(map.VirtualKey);
        var strokes = new List<KeyStroke>(6);

        if (map.Shift) strokes.Add(KeyStroke.Down(VkLShift, shiftScan));
        if (altGr) strokes.Add(KeyStroke.Down(VkRMenu, altGrScan, extended: true));

        strokes.Add(KeyStroke.Down(map.VirtualKey, keyScan, keyExtended));
        strokes.Add(KeyStroke.Up(map.VirtualKey, keyScan, keyExtended));

        if (altGr) strokes.Add(KeyStroke.Up(VkRMenu, altGrScan, extended: true));
        if (map.Shift) strokes.Add(KeyStroke.Up(VkLShift, shiftScan));

        group = strokes;
        return true;
    }

    /// <summary>
    /// Plans a press/release of a specific virtual key (Enter, Tab). Falls back to a
    /// virtual-key-only event when the layout reports no scan code.
    /// </summary>
    private static IReadOnlyList<KeyStroke> PlanVirtualKey(ushort virtualKey, IKeyboardLayoutMap layout)
    {
        ushort scan = layout.GetScanCode(virtualKey);
        bool extended = ExtendedKeys.Contains(virtualKey);
        return new[]
        {
            KeyStroke.Down(virtualKey, scan, extended),
            KeyStroke.Up(virtualKey, scan, extended),
        };
    }
}
