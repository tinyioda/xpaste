using xpaste.Services.Input;

namespace xpaste.Tests;

/// <summary>
/// Tests for <see cref="KeystrokePlanner"/>.
/// <para>
/// The behaviour these lock down is what makes snippets work in RDP sessions, SSH/terminal
/// password prompts and Windows password fields: every synthesised key event must carry a real
/// scan code, must not leave modifiers latched, and must never synthesise a Ctrl/Alt shortcut.
/// </para>
/// </summary>
public class KeystrokePlannerTests
{
    private readonly FakeUsKeyboardLayout _layout = new();

    private IReadOnlyList<KeyStroke> Flatten(string text)
        => KeystrokePlanner.Flatten(KeystrokePlanner.Plan(text, _layout));

    // ── Grouping ─────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_EmptyString_ProducesNoGroups()
    {
        Assert.Empty(KeystrokePlanner.Plan("", _layout));
    }

    [Fact]
    public void Plan_ProducesOneGroupPerCharacter()
    {
        Assert.Equal(5, KeystrokePlanner.Plan("hello", _layout).Count);
    }

    [Fact]
    public void Plan_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => KeystrokePlanner.Plan(null!, _layout));
    }

    [Fact]
    public void Plan_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => KeystrokePlanner.Plan("a", null!));
    }

    // ── Scan codes: the RDP-critical guarantee ───────────────────────────────

    [Fact]
    public void Plan_EveryScanCodeStroke_HasNonZeroScanCode()
    {
        // A zero scan code is forwarded to a remote session as "no key at all", which is exactly
        // why virtual-key-only injection silently did nothing over RDP.
        var strokes = Flatten("Pa$$w0rd! secret");

        Assert.NotEmpty(strokes);
        Assert.All(strokes.Where(s => s.Kind == KeyStrokeKind.ScanCode),
            s => Assert.NotEqual(0, s.ScanCode));
    }

    [Fact]
    public void Plan_AsciiPassword_UsesScanCodesNotUnicode()
    {
        var strokes = Flatten("Tr0ub4dor&3");
        Assert.All(strokes, s => Assert.Equal(KeyStrokeKind.ScanCode, s.Kind));
    }

    [Fact]
    public void Plan_LowercaseLetter_EmitsDownThenUpWithoutShift()
    {
        var strokes = Flatten("a");

        Assert.Equal(2, strokes.Count);
        Assert.Equal(KeyStroke.Down(0x41, 0x1E), strokes[0]);
        Assert.Equal(KeyStroke.Up(0x41, 0x1E), strokes[1]);
    }

    [Fact]
    public void Plan_UppercaseLetter_WrapsKeyInShiftDownAndUp()
    {
        var strokes = Flatten("A");

        Assert.Equal(4, strokes.Count);
        Assert.Equal(KeyStroke.Down(KeystrokePlanner.VkLShift, 0x2A), strokes[0]);
        Assert.Equal(KeyStroke.Down(0x41, 0x1E), strokes[1]);
        Assert.Equal(KeyStroke.Up(0x41, 0x1E), strokes[2]);
        Assert.Equal(KeyStroke.Up(KeystrokePlanner.VkLShift, 0x2A), strokes[3]);
    }

    [Fact]
    public void Plan_ShiftedSymbol_UsesBaseKeyWithShift()
    {
        // '$' is Shift+4, so the base key must be the digit 4 (scan 0x05), not some symbol key.
        var strokes = Flatten("$");

        Assert.Equal(0x34, strokes[1].VirtualKey);
        Assert.Equal(0x05, strokes[1].ScanCode);
        Assert.Equal(KeystrokePlanner.VkLShift, strokes[0].VirtualKey);
    }

    [Fact]
    public void Plan_Digit_DoesNotUseShift()
    {
        var strokes = Flatten("7");

        Assert.Equal(2, strokes.Count);
        Assert.DoesNotContain(strokes, s => s.VirtualKey == KeystrokePlanner.VkLShift);
    }

    // ── Modifier hygiene ─────────────────────────────────────────────────────

    [Fact]
    public void Plan_EveryKeyDown_HasMatchingKeyUp()
    {
        var strokes = Flatten("Mixed CASE 123 !@#");

        var downs = strokes.Where(s => !s.IsKeyUp).ToList();
        var ups = strokes.Where(s => s.IsKeyUp).ToList();

        Assert.Equal(downs.Count, ups.Count);
    }

    [Fact]
    public void Plan_NoGroupLeavesAModifierHeld()
    {
        // Each group must be self-contained: a modifier held across a pacing delay would corrupt
        // every subsequent character.
        foreach (var group in KeystrokePlanner.Plan("Abc!€\u2713", _layout))
        {
            int held = 0;
            foreach (var stroke in group) held += stroke.IsKeyUp ? -1 : 1;
            Assert.Equal(0, held);
        }
    }

    [Fact]
    public void Plan_ShiftIsReleasedAfterTheKey()
    {
        var strokes = Flatten("Z");

        int keyUpIndex = strokes.ToList().FindIndex(s => s.IsKeyUp && s.VirtualKey == 0x5A);
        int shiftUpIndex = strokes.ToList().FindIndex(s => s.IsKeyUp && s.VirtualKey == KeystrokePlanner.VkLShift);

        Assert.True(keyUpIndex < shiftUpIndex, "Shift must be released after the character key.");
    }

    // ── Unicode fallback ─────────────────────────────────────────────────────

    [Fact]
    public void Plan_UnmappableCharacter_FallsBackToUnicode()
    {
        var strokes = Flatten("\u2713"); // ✓ has no key on a US layout

        Assert.Equal(2, strokes.Count);
        Assert.All(strokes, s => Assert.Equal(KeyStrokeKind.Unicode, s.Kind));
        Assert.Equal('\u2713', (char)strokes[0].ScanCode);
    }

    [Fact]
    public void Plan_UnicodeFallback_LeavesVirtualKeyZero()
    {
        var strokes = Flatten("\u2713");
        Assert.All(strokes, s => Assert.Equal(0, s.VirtualKey));
    }

    [Fact]
    public void Plan_SurrogatePair_EmittedAsOneGroupWithBothCodeUnits()
    {
        const string emoji = "\uD83D\uDE00"; // U+1F600
        var groups = KeystrokePlanner.Plan(emoji, _layout);

        Assert.Single(groups);
        Assert.Equal(4, groups[0].Count);
        Assert.Equal('\uD83D', (char)groups[0][0].ScanCode);
        Assert.Equal('\uDE00', (char)groups[0][2].ScanCode);
    }

    // ── Never synthesise shortcuts ───────────────────────────────────────────

    [Fact]
    public void Plan_CtrlOnlyMapping_FallsBackToUnicodeInsteadOfSendingAShortcut()
    {
        // '\u0003' maps to Ctrl+C. Synthesising that would fire the target's Copy command
        // rather than typing anything.
        var strokes = Flatten("\u0003");

        Assert.All(strokes, s => Assert.Equal(KeyStrokeKind.Unicode, s.Kind));
        Assert.DoesNotContain(strokes, s => s.VirtualKey == 0x43);
    }

    // ── AltGr ────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_AltGrCharacter_UsesExtendedRightAlt()
    {
        var strokes = Flatten("€");

        var altGrDown = strokes[0];
        Assert.Equal(KeystrokePlanner.VkRMenu, altGrDown.VirtualKey);
        Assert.True(altGrDown.IsExtended, "AltGr requires the extended-key flag.");
        Assert.False(altGrDown.IsKeyUp);

        var altGrUp = strokes[^1];
        Assert.Equal(KeystrokePlanner.VkRMenu, altGrUp.VirtualKey);
        Assert.True(altGrUp.IsKeyUp);
    }

    [Fact]
    public void Plan_AltGrCharacter_TypesTheBaseKey()
    {
        var strokes = Flatten("€");
        Assert.Contains(strokes, s => s.VirtualKey == 0x35 && s.ScanCode == 0x06 && !s.IsKeyUp);
    }

    // ── Whitespace ───────────────────────────────────────────────────────────

    [Fact]
    public void Plan_LineFeed_EmitsSingleReturnKey()
    {
        var groups = KeystrokePlanner.Plan("\n", _layout);

        Assert.Single(groups);
        Assert.Equal(KeystrokePlanner.VkReturn, groups[0][0].VirtualKey);
        Assert.Equal(0x1C, groups[0][0].ScanCode);
    }

    [Fact]
    public void Plan_CarriageReturnLineFeed_CollapsesToOneReturn()
    {
        // Two Enter presses in a password or a form field would submit twice.
        var groups = KeystrokePlanner.Plan("a\r\nb", _layout);

        Assert.Equal(3, groups.Count);
        Assert.Equal(KeystrokePlanner.VkReturn, groups[1][0].VirtualKey);
    }

    [Fact]
    public void Plan_LoneCarriageReturn_EmitsReturn()
    {
        var groups = KeystrokePlanner.Plan("\r", _layout);

        Assert.Single(groups);
        Assert.Equal(KeystrokePlanner.VkReturn, groups[0][0].VirtualKey);
    }

    [Fact]
    public void Plan_Tab_EmitsTabKey()
    {
        var groups = KeystrokePlanner.Plan("\t", _layout);

        Assert.Single(groups);
        Assert.Equal(KeystrokePlanner.VkTab, groups[0][0].VirtualKey);
        Assert.Equal(0x0F, groups[0][0].ScanCode);
    }

    [Fact]
    public void Plan_Space_UsesSpaceScanCode()
    {
        var strokes = Flatten(" ");
        Assert.Equal(0x39, strokes[0].ScanCode);
    }

    // ── Flatten ──────────────────────────────────────────────────────────────

    [Fact]
    public void Flatten_PreservesGroupOrder()
    {
        var groups = KeystrokePlanner.Plan("ab", _layout);
        var flat = KeystrokePlanner.Flatten(groups);

        Assert.Equal(4, flat.Count);
        Assert.Equal(0x41, flat[0].VirtualKey); // a
        Assert.Equal(0x42, flat[2].VirtualKey); // b
    }
}
