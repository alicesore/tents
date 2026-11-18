using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Shell.Threading;

/// <summary>
/// One dedicated single-threaded-apartment thread with a Win32 message loop, for shell work that must not run on a
/// tent UI thread: talking to Explorer's desktop view, extracting icons, showing shell menus.
/// </summary>
/// <remarks>
/// <para><b>Why an STA with a message loop.</b> Shell objects and shell extensions expect an STA, and several of them
/// (clipboard verbs, drag and drop, menus, <c>ShellExecuteEx</c> through DDE) need OLE and a pumping thread. The
/// thread calls <c>OleInitialize</c> and runs <c>GetMessage</c>.</para>
/// <para><b>Idle cost.</b> None: the thread is blocked in <c>GetMessage</c>. Work is handed over through a queue plus
/// one posted message to a message-only window. No timers, no polling. The thread starts on the first call.</para>
/// <para><b>Re-entrancy.</b> A work item that runs a modal loop (a context menu) keeps dispatching messages, so the
/// "work posted" message can arrive while an item runs. Items never nest: a nested message is ignored and the outer
/// drain loop picks up the new item when the current one returns. Items run one at a time, in order.</para>
/// <para><b>Ownership.</b> COM pointers created by a work item belong to this thread and must be released by a work
/// item too.</para>
/// </remarks>
public sealed class StaWorker : IDisposable
{
    private const uint _workPostedMessage = PInvoke.WM_APP + 1;
    private const string _windowClassName = "Tents.StaWorker";

    private static readonly Lock _classGate = new();
    private static bool _classRegistered;

    [ThreadStatic]
    private static StaWorker? _current;

    private readonly string _name;
    private readonly ConcurrentQueue<IWorkItem> _queue = new();
    private readonly Lock _gate = new();
    private Thread? _thread;
    private nint _hwnd;
    private int _posted;
    private bool _draining;
    private bool _disposed;

    /// <param name="name">Thread name, shown in debuggers and traces.</param>
    public StaWorker(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
    }

    /// <summary>True when called from this worker's thread.</summary>
    public bool IsCurrentThread => ReferenceEquals(_current, this);

    /// <summary>Runs <paramref name="work"/> on the worker thread. Never blocks the caller.</summary>
    /// <exception cref="ObjectDisposedException">The worker was disposed.</exception>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var item = new WorkItem<T>(work);
        Enqueue(item);
        return item.Task;
    }

    /// <inheritdoc cref="InvokeAsync{T}(Func{T})"/>
    public Task InvokeAsync(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return InvokeAsync(() =>
        {
            work();
            return true;
        });
    }

    /// <summary>
    /// Ends the thread after the items already queued have run. Items queued later fail with
    /// <see cref="ObjectDisposedException"/>. Does not wait for the thread.
    /// </summary>
    public void Dispose()
    {
        nint hwnd;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            hwnd = Volatile.Read(ref _hwnd);
        }

        if (hwnd != 0)
        {
            PInvoke.PostMessage((HWND)hwnd, PInvoke.WM_CLOSE, default, default);
        }
    }

    private void Enqueue(IWorkItem item)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _queue.Enqueue(item);
            if (_thread is null)
            {
                ShellPlatform.EnsureSupported();
                _thread = new Thread(Run) { Name = _name, IsBackground = true };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                return; // The thread drains the queue once its window exists.
            }
        }

        // The thread publishes its window before its first drain, so an item queued before the window existed is
        // picked up by that drain. Post at most one message until the window handles it.
        var hwnd = Volatile.Read(ref _hwnd);
        if (hwnd != 0 && Interlocked.Exchange(ref _posted, 1) == 0)
        {
            PInvoke.PostMessage((HWND)hwnd, _workPostedMessage, default, default);
        }
    }

    private unsafe void Run()
    {
        _current = this;
        var oleInitialized = false;
        try
        {
            PInvoke.OleInitialize(null).ThrowOnFailure();
            oleInitialized = true;
            RegisterWindowClass();
            HWND hwnd;
            fixed (char* className = _windowClassName)
            {
                hwnd = PInvoke.CreateWindowEx(0, className, null, 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, default, default, null);
            }

            if (hwnd.IsNull)
            {
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}.");
            }

            bool closeNow;
            lock (_gate)
            {
                Volatile.Write(ref _hwnd, (nint)hwnd);
                closeNow = _disposed;
            }

            Drain();
            if (closeNow)
            {
                PInvoke.DestroyWindow(hwnd);
            }

            MSG msg;
            while (PInvoke.GetMessage(&msg, default, 0, 0).Value > 0)
            {
                PInvoke.TranslateMessage(&msg);
                PInvoke.DispatchMessage(&msg);
            }
        }
        catch (Exception ex) // The thread could not start its loop. Fail the waiting callers instead of crashing.
        {
            lock (_gate)
            {
                _disposed = true;
            }

            FailPending(ex);
        }
        finally
        {
            FailPending(new ObjectDisposedException(nameof(StaWorker)));
            if (oleInitialized)
            {
                PInvoke.OleUninitialize();
            }

            _current = null;
        }
    }

    private void Drain()
    {
        Volatile.Write(ref _posted, 0);
        if (_draining)
        {
            return; // A modal loop inside the running item dispatched this message; the outer loop continues.
        }

        _draining = true;
        try
        {
            while (_queue.TryDequeue(out var item))
            {
                item.Run();
            }
        }
        finally
        {
            _draining = false;
        }
    }

    // Items queued after the loop ended (a race with Dispose) are failed, never left waiting.
    private void FailPending(Exception reason)
    {
        while (_queue.TryDequeue(out var item))
        {
            item.Fail(reason);
        }
    }

    private static unsafe void RegisterWindowClass()
    {
        lock (_classGate)
        {
            if (_classRegistered)
            {
                return;
            }

            fixed (char* className = _windowClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WindowProc,
                    hInstance = (HINSTANCE)PInvoke.GetModuleHandle((char*)null).Value,
                    lpszClassName = className,
                };
                if (PInvoke.RegisterClassEx(&wc) == 0)
                {
                    throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}.");
                }
            }

            _classRegistered = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            switch (message)
            {
                case _workPostedMessage:
                    _current?.Drain();
                    return default;
                case PInvoke.WM_CLOSE:
                    PInvoke.DestroyWindow(hwnd);
                    return default;
                case PInvoke.WM_DESTROY:
                    PInvoke.PostQuitMessage(0);
                    return default;
                default:
                    return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
            }
        }
        catch (Exception ex) when (ShellPlatform.ReportCallbackException(ex))
        {
            // An exception must not unwind into user32. Work items catch their own; this is a last resort.
            return default;
        }
    }

    private interface IWorkItem
    {
        void Run();

        void Fail(Exception reason);
    }

    private sealed class WorkItem<T>(Func<T> work) : IWorkItem
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _completion.Task;

        public void Run()
        {
            try
            {
                _completion.TrySetResult(work());
            }
            catch (Exception ex) // Handed to the caller through the task.
            {
                _completion.TrySetException(ex);
            }
        }

        public void Fail(Exception reason) => _completion.TrySetException(reason);
    }
}
