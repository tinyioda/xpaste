using System.Globalization;
using System.Windows;
using xpaste.Converters;

namespace xpaste.Tests;

public class ConverterTests
{
    // ── SlotDisplayConverter ─────────────────────────────────────────────────

    private readonly SlotDisplayConverter _slotConv = new();
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    [Fact]
    public void SlotDisplay_Slot0_ReturnsUnassigned()
        => Assert.Equal("Unassigned", _slotConv.Convert(0, typeof(string), null!, _culture));

    [Theory]
    [InlineData(1, "Ctrl+Shift+1")]
    [InlineData(2, "Ctrl+Shift+2")]
    [InlineData(5, "Ctrl+Shift+5")]
    [InlineData(9, "Ctrl+Shift+9")]
    public void SlotDisplay_Slots1To9_ReturnsCorrectLabel(int slot, string expected)
        => Assert.Equal(expected, _slotConv.Convert(slot, typeof(string), null!, _culture));

    [Fact]
    public void SlotDisplay_Slot10_ReturnCtrlShift0()
        => Assert.Equal("Ctrl+Shift+0", _slotConv.Convert(10, typeof(string), null!, _culture));

    [Fact]
    public void SlotDisplay_OutOfRange_ReturnsUnassigned()
        => Assert.Equal("Unassigned", _slotConv.Convert(99, typeof(string), null!, _culture));

    [Fact]
    public void SlotDisplay_NullInput_ReturnsUnassigned()
        => Assert.Equal("Unassigned", _slotConv.Convert(null!, typeof(string), null!, _culture));

    // ── BoolToVisibilityConverter ────────────────────────────────────────────

    private readonly BoolToVisibilityConverter _visConv = new();

    [Fact]
    public void BoolToVis_True_ReturnsVisible()
        => Assert.Equal(Visibility.Visible, _visConv.Convert(true, typeof(Visibility), null!, _culture));

    [Fact]
    public void BoolToVis_False_ReturnsCollapsed()
        => Assert.Equal(Visibility.Collapsed, _visConv.Convert(false, typeof(Visibility), null!, _culture));

    [Fact]
    public void BoolToVis_NonEmptyString_ReturnsVisible()
        => Assert.Equal(Visibility.Visible, _visConv.Convert("hello", typeof(Visibility), null!, _culture));

    [Fact]
    public void BoolToVis_EmptyString_ReturnsCollapsed()
        => Assert.Equal(Visibility.Collapsed, _visConv.Convert("", typeof(Visibility), null!, _culture));

    [Fact]
    public void BoolToVis_NonNullObject_ReturnsVisible()
        => Assert.Equal(Visibility.Visible, _visConv.Convert(new object(), typeof(Visibility), null!, _culture));

    [Fact]
    public void BoolToVis_Null_ReturnsCollapsed()
        => Assert.Equal(Visibility.Collapsed, _visConv.Convert(null!, typeof(Visibility), null!, _culture));
}
