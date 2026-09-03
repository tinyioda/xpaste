using xpaste.Services;

namespace xpaste.Tests;

/// <summary>
/// Pins the hotkey labels shown in the UI to a single definition.
/// <para>
/// The modifier used to be repeated as a literal in the converter, the slot dropdown, the
/// validation message, the footer and the help overlay. These tests exist so that changing the
/// combination in one place cannot leave the UI advertising a shortcut the app never registered.
/// </para>
/// </summary>
public class HotkeysTests
{
    [Theory]
    [InlineData(1, "Alt+Shift+1")]
    [InlineData(5, "Alt+Shift+5")]
    [InlineData(9, "Alt+Shift+9")]
    public void ForSlot_UsesTheModifierAndDigit(int slot, string expected)
    {
        Assert.Equal(expected, Hotkeys.ForSlot(slot));
    }

    [Fact]
    public void ForSlot_Slot10_MapsToZero()
    {
        // There is no "10" key — slot 10 lives on the 0 key.
        Assert.Equal("Alt+Shift+0", Hotkeys.ForSlot(10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void ForSlot_OutOfRange_IsUnassigned(int slot)
    {
        Assert.Equal("Unassigned", Hotkeys.ForSlot(slot));
    }

    [Fact]
    public void EverySlotLabel_StartsWithTheModifier()
    {
        for (int slot = 1; slot <= 10; slot++)
            Assert.StartsWith(Hotkeys.ModifierLabel, Hotkeys.ForSlot(slot));
    }

    [Fact]
    public void CompositeLabels_AllDeriveFromTheModifier()
    {
        Assert.StartsWith(Hotkeys.ModifierLabel, Hotkeys.ToggleLabel);
        Assert.StartsWith(Hotkeys.ModifierLabel, Hotkeys.MinimizeLabel);
        Assert.StartsWith(Hotkeys.ModifierLabel, Hotkeys.SlotRangeLabel);
        Assert.Contains(Hotkeys.ModifierLabel, Hotkeys.ModifierRequirementLabel);
        Assert.Contains(Hotkeys.ModifierLabel, Hotkeys.FooterSummary);
    }

    [Fact]
    public void NoLabelStillAdvertisesTheOldCtrlShiftCombination()
    {
        string[] labels =
        {
            Hotkeys.ModifierLabel, Hotkeys.ToggleLabel, Hotkeys.MinimizeLabel,
            Hotkeys.SlotRangeLabel, Hotkeys.ModifierRequirementLabel, Hotkeys.FooterSummary,
            Hotkeys.ForSlot(1), Hotkeys.ForSlot(10),
        };

        Assert.All(labels, l => Assert.DoesNotContain("Ctrl", l));
    }
}
