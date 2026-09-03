using System.Runtime.InteropServices;
using xpaste.Services.Input;

namespace xpaste.Tests;

/// <summary>
/// Guards the Win32 marshalling contract for <c>SendInput</c>.
/// <para>
/// These are cheap checks for expensive, silent bugs: when the <c>INPUT</c> struct is the wrong
/// size, <c>SendInput</c> returns 0 and no keystroke is ever delivered — with no exception and
/// no error dialog.
/// </para>
/// </summary>
public class NativeInputTests
{
    [Fact]
    public void InputStructSize_MatchesTheNativeAbi()
    {
        // x64: DWORD type (4) + 4 bytes padding + 32-byte union == 40.
        // x86: DWORD type (4) + 24-byte union == 28.
        int expected = IntPtr.Size == 8 ? 40 : 28;
        Assert.Equal(expected, NativeInput.InputStructSize);
    }

    [Fact]
    public void InputStructSize_IsPointerAligned()
    {
        Assert.Equal(0, NativeInput.InputStructSize % IntPtr.Size);
    }

    [Fact]
    public void AccessDenied_IsRecognisedAsTheUipiBlockCode()
    {
        Assert.True(NativeInput.IsAccessDenied(5));
        Assert.False(NativeInput.IsAccessDenied(0));
        Assert.False(NativeInput.IsAccessDenied(87));
    }

    [Fact]
    public void Send_EmptySequence_IsANoOp()
    {
        Assert.Equal(0u, NativeInput.Send(Array.Empty<KeyStroke>()));
    }

    [Fact]
    public void AreModifiersHeld_DoesNotThrow()
    {
        // Value depends on the physical keyboard, so only the call contract is asserted.
        _ = NativeInput.AreModifiersHeld();
    }

    [Fact]
    public void IsSecureDesktopActive_DoesNotThrow()
    {
        _ = NativeInput.IsSecureDesktopActive();
    }

    [Fact]
    public void IsForegroundWindowHigherIntegrity_DoesNotThrow()
    {
        // The test host has no foreground window of its own, so this must resolve to "not blocked"
        // rather than throwing or defaulting to a refusal.
        Assert.False(NativeInput.IsForegroundWindowHigherIntegrity());
    }

    [Fact]
    public void Marshalling_KeyboardInputIsNotLargerThanTheUnion()
    {
        // KEYBDINPUT is 24 bytes on x64 and must fit inside the 32-byte MOUSEINPUT-sized union.
        int keyboardInputSize = IntPtr.Size == 8 ? 24 : 16;
        Assert.True(keyboardInputSize <= NativeInput.InputStructSize - IntPtr.Size);
    }
}
