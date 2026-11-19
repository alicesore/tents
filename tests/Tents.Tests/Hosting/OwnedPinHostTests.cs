using System.Windows.Interop;
using Tents.Shell.Hosting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Tests.Hosting;

/// <summary>
/// The owned-pin rules. Windows used here are hidden test windows of this process, never shown, and the "desktop" they
/// are owned by is another hidden test window, so nothing touches the real desktop and the tests run in CI.
/// </summary>
public sealed unsafe class OwnedPinHostTests
{
    private const int _wsPopup = unchecked((int)0x80000000);

    [Fact]
    public void PinToBottom_RewritesAZOrderChangeToBottom()
    {
        var position = new WINDOWPOS { hwndInsertAfter = (HWND)(nint)(-1), flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE };

        Assert.True(ZOrderPin.PinToBottom(ref position));
        Assert.Equal(ZOrderPin.HwndBottom, (nint)position.hwndInsertAfter.Value);
        Assert.Equal(SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE, position.flags);
    }

    [Fact]
    public void PinToBottom_LeavesAChangeWithoutZOrderAlone()
    {
        var position = new WINDOWPOS { hwndInsertAfter = (HWND)(nint)42, flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER, x = 5 };

        Assert.False(ZOrderPin.PinToBottom(ref position));
        Assert.Equal(42, (nint)position.hwndInsertAfter.Value);
        Assert.Equal(5, position.x);
    }

    [Fact]
    public void ProcessMessage_MouseActivate_NeverActivates()
    {
        var host = new OwnedPinHost(() => 0);

        Assert.True(host.ProcessMessage(0, OwnedPinHost.WmMouseActivate, 0, 0, out var result));
        Assert.Equal(OwnedPinHost.MaNoActivate, result);
    }

    [Fact]
    public void ProcessMessage_WindowPosChanging_PinsButLetsTheWindowProcessIt()
    {
        var host = new OwnedPinHost(() => 0);
        var position = new WINDOWPOS { hwndInsertAfter = default, flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE };

        var handled = host.ProcessMessage(0, OwnedPinHost.WmWindowPosChanging, 0, (nint)(&position), out _);

        Assert.False(handled);
        Assert.Equal(ZOrderPin.HwndBottom, (nint)position.hwndInsertAfter.Value);
    }

    [Fact]
    public void ProcessMessage_OtherMessages_AreNotHandled()
    {
        var host = new OwnedPinHost(() => 0);

        Assert.False(host.ProcessMessage(0, 0x0200 /* WM_MOUSEMOVE */, 0, 0, out _));
        Assert.False(host.ProcessMessage(0, OwnedPinHost.WmWindowPosChanging, 0, 0, out _)); // no WINDOWPOS: ignored
    }

    [Fact]
    public void Reattach_WithoutAWindow_ReportsWindowGone()
    {
        var host = new OwnedPinHost(() => 0);

        Assert.Equal(ReattachResult.WindowGone, host.Reattach(0));
    }

    [Fact]
    public void Prepare_AddsToolWindowAndNoActivate() => StaThread.Run(() =>
    {
        using var window = HiddenWindow("tent");
        var host = new OwnedPinHost(() => 0);

        host.Prepare(window.Handle);

        var style = PInvoke.GetWindowLongPtr((HWND)window.Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        Assert.Equal(OwnedPinHost.WsExToolWindow, style & OwnedPinHost.WsExToolWindow);
        Assert.Equal(OwnedPinHost.WsExNoActivate, style & OwnedPinHost.WsExNoActivate);
    });

    [Fact]
    public void Reattach_SetsTheCurrentDesktopWindowAsOwner_AndIsIdempotent() => StaThread.Run(() =>
    {
        using var oldDesktop = HiddenWindow("old desktop");
        using var newDesktop = HiddenWindow("new desktop");
        using var tent = HiddenWindow("tent", owner: oldDesktop.Handle);
        var desktop = newDesktop.Handle;
        var host = new OwnedPinHost(() => desktop);
        Assert.Equal(oldDesktop.Handle, OwnedPinHost.GetOwner(tent.Handle));

        Assert.Equal(ReattachResult.Reattached, host.Reattach(tent.Handle));
        Assert.Equal(newDesktop.Handle, OwnedPinHost.GetOwner(tent.Handle));
        Assert.Equal(ReattachResult.AlreadyAttached, host.Reattach(tent.Handle));
        Assert.Equal(newDesktop.Handle, OwnedPinHost.GetOwner(tent.Handle));
    });

    [Fact]
    public void Reattach_WithoutADesktopWindow_ChangesNothing() => StaThread.Run(() =>
    {
        using var desktop = HiddenWindow("desktop");
        using var tent = HiddenWindow("tent", owner: desktop.Handle);
        var host = new OwnedPinHost(() => 0);

        Assert.Equal(ReattachResult.NoDesktop, host.Reattach(tent.Handle));
        Assert.Equal(desktop.Handle, OwnedPinHost.GetOwner(tent.Handle));
    });

    [Fact]
    public void Reattach_AfterTheWindowWasDestroyed_ReportsWindowGone() => StaThread.Run(() =>
    {
        using var desktop = HiddenWindow("desktop");
        var tent = HiddenWindow("tent", owner: desktop.Handle);
        var handle = tent.Handle;
        tent.Dispose();
        var host = new OwnedPinHost(() => desktop.Handle);

        Assert.Equal(ReattachResult.WindowGone, host.Reattach(handle));
    });

    [Fact]
    public void GetOwnerForNewWindow_IsTheDesktopWindowLookup()
    {
        var host = new OwnedPinHost(() => 1234);

        Assert.Equal(1234, host.GetOwnerForNewWindow());
        Assert.Equal("owned-pin", host.Name);
    }

    // A never-shown popup window of this process.
    private static HwndSource HiddenWindow(string name, nint owner = 0) =>
        new(new HwndSourceParameters("Tents test " + name) { WindowStyle = _wsPopup, Width = 10, Height = 10, ParentWindow = owner });
}
