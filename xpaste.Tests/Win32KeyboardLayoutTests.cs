using xpaste.Services.Input;

namespace xpaste.Tests;

/// <summary>
/// Tests for <see cref="Win32KeyboardLayout"/> against the machine's real active keyboard layout.
/// <para>
/// Assertions are deliberately layout-agnostic: they check invariants that hold on any Latin
/// layout rather than hard-coding US key positions.
/// </para>
/// </summary>
public class Win32KeyboardLayoutTests
{
    private readonly Win32KeyboardLayout _layout = Win32KeyboardLayout.ForForegroundWindow();

    [Fact]
    public void NonAnsiCharacter_DoesNotCollapseToQuestionMark()
    {
        // Regression guard. Declaring VkKeyScanEx without CharSet.Unicode binds to the ...A entry
        // point, which marshals the char through the ANSI code page first. Every character outside
        // cp1252 then arrives as '?', and the snippet is typed as literal question marks.
        if (!_layout.TryMapCharacter('?', out var questionMark))
            return; // '?' is unreachable on this layout; the comparison would be meaningless.

        bool mapped = _layout.TryMapCharacter('\u2713', out var check); // ✓

        Assert.False(mapped && check == questionMark,
            "U+2713 resolved to the '?' key — VkKeyScanEx is being marshalled as ANSI.");
    }

    [Fact]
    public void AsciiLetters_ResolveToKeysWithRealScanCodes()
    {
        // A zero scan code is what silently breaks injection into RDP and VM sessions.
        foreach (char c in "abcdefghijklmnopqrstuvwxyz")
        {
            Assert.True(_layout.TryMapCharacter(c, out var mapping), $"'{c}' is not typeable.");
            Assert.NotEqual(0, _layout.GetScanCode(mapping.VirtualKey));
        }
    }

    [Fact]
    public void Digits_ResolveToKeysWithRealScanCodes()
    {
        foreach (char c in "0123456789")
        {
            Assert.True(_layout.TryMapCharacter(c, out var mapping), $"'{c}' is not typeable.");
            Assert.NotEqual(0, _layout.GetScanCode(mapping.VirtualKey));
        }
    }

    [Fact]
    public void UppercaseRequiresShift_LowercaseDoesNot()
    {
        Assert.True(_layout.TryMapCharacter('a', out var lower));
        Assert.True(_layout.TryMapCharacter('A', out var upper));

        Assert.False(lower.Shift);
        Assert.True(upper.Shift);
        Assert.Equal(lower.VirtualKey, upper.VirtualKey);
    }

    [Fact]
    public void ModifierAndControlKeys_HaveScanCodes()
    {
        Assert.NotEqual(0, _layout.GetScanCode(KeystrokePlanner.VkLShift));
        Assert.NotEqual(0, _layout.GetScanCode(KeystrokePlanner.VkReturn));
        Assert.NotEqual(0, _layout.GetScanCode(KeystrokePlanner.VkTab));
    }

    [Fact]
    public void PlanningARealPassword_ProducesOnlyUsableStrokes()
    {
        // The full pipeline against the real layout: nothing may be emitted with a zero scan code
        // unless it is an intentional Unicode fallback.
        var strokes = KeystrokePlanner.Flatten(
            KeystrokePlanner.Plan("Corr3ct-H0rse!Battery$taple", _layout));

        Assert.NotEmpty(strokes);
        Assert.All(strokes, s => Assert.True(
            s.Kind == KeyStrokeKind.Unicode || s.ScanCode != 0,
            "A scan-code stroke with scan code 0 would be dropped by RDP."));
    }
}
