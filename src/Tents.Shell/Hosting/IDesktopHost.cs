namespace Tents.Shell.Hosting;

/// <summary>What <see cref="IDesktopHost.Reattach"/> found.</summary>
public enum ReattachResult
{
    /// <summary>The window was already attached to the current desktop window. It was sent to the bottom again.</summary>
    AlreadyAttached,

    /// <summary>The window was attached to the new desktop window (after an Explorer restart).</summary>
    Reattached,

    /// <summary>The shell has no desktop window right now. Nothing changed; try again on the next <c>TaskbarCreated</c>.</summary>
    NoDesktop,

    /// <summary>The window no longer exists (destroyed with its old owner). Create a new one.</summary>
    WindowGone,
}

/// <summary>
/// How a tent window is attached to the desktop: behind every normal window, visible on Show Desktop, never activated by
/// a click (spike 1, <c>docs/spikes/01-desktop-layer.md</c>).
/// </summary>
/// <remarks>
/// One implementation per strategy, so the strategy can be switched per Windows version without touching the tent
/// window: <see cref="OwnedPinHost"/> (<c>owned-pin</c>) is the default; spike 1 keeps <c>bottom-react2</c> as the
/// fallback if Windows 10 22H2 needs it. All members are called on the tent window's UI thread.
/// </remarks>
public interface IDesktopHost
{
    /// <summary>The strategy's name as spike 1 uses it, for logs.</summary>
    string Name { get; }

    /// <summary>
    /// The owner to give a tent window when it is created, or 0 for none. Spike 1: set the owner at creation, not later.
    /// </summary>
    nint GetOwnerForNewWindow();

    /// <summary>Applies the window styles the strategy needs. Call once, after the handle exists and before it is shown.</summary>
    void Prepare(nint hwnd);

    /// <summary>
    /// Window procedure hook for a tent window. Returns true when the message is fully handled and
    /// <paramref name="result"/> is its result; false lets the window process it as usual (possibly after the host
    /// adjusted the message's data).
    /// </summary>
    bool ProcessMessage(nint hwnd, int message, nint wParam, nint lParam, out nint result);

    /// <summary>
    /// Attaches an existing tent window to the shell's current desktop window again. Call when <c>TaskbarCreated</c>
    /// arrives. Idempotent: <c>TaskbarCreated</c> is also broadcast for reasons other than an Explorer restart (Windows 10
    /// sends it when the primary display's DPI changes).
    /// </summary>
    ReattachResult Reattach(nint hwnd);
}
