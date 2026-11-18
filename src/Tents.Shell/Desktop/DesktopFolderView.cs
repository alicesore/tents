using System.Runtime.InteropServices;
using Tents.Shell.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.SystemServices;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace Tents.Shell.Desktop;

/// <summary>
/// The desktop's own shell view (<c>IFolderView2</c>), a proxy into Explorer's process.
/// </summary>
/// <remarks>
/// <para>Read-only except for <see cref="SetFolderFlags"/>, which only the icon layer switch (M1.8) calls.</para>
/// <para><b>Route.</b> <c>IShellWindows::FindWindowSW(CSIDL_DESKTOP, SWC_DESKTOP)</c>, then
/// <c>IServiceProvider::QueryService(SID_STopLevelBrowser)</c> for the desktop's <c>IShellBrowser</c>, then
/// <c>QueryActiveShellView</c>. Public description: Raymond Chen,
/// <see href="https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933">Manipulating the positions of desktop
/// icons</see>. Validated in M0 spike 2.</para>
/// <para><b>Items.</b> The list comes from <c>IFolderView::Items(SVGIO_ALLVIEW)</c>, which is exactly what the
/// desktop shows. Enumerating <c>FOLDERID_Desktop</c> instead returns items the desktop hides (22 against 4 in spike
/// 2). Each child ID list (relative to the desktop root) is turned into a shell item in this process, so names and
/// attributes do not cost a call into Explorer per item.</para>
/// <para><b>Threading.</b> Create, use and dispose on one STA thread (a <see cref="Threading.StaWorker"/>). After an
/// Explorer restart the proxy is dead: calls fail with a <see cref="ComErrors.IsDisconnected">disconnected</see>
/// HRESULT and the owner acquires a new view.</para>
/// </remarks>
internal sealed unsafe class DesktopFolderView : IDisposable
{
    private const int _batchSize = 64;

    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private IFolderView2* _view;

    private DesktopFolderView(IFolderView2* view) => _view = view;

    /// <summary>The raw view, for callers on the owning thread. Valid until <see cref="Dispose"/>.</summary>
    public IFolderView2* Pointer
    {
        get
        {
            CheckAccess();
            return _view;
        }
    }

    /// <summary>Acquires the desktop's view, or returns null when no desktop window is registered (no shell).</summary>
    public static DesktopFolderView? TryAcquire()
    {
        IShellWindows* windows = null;
        IDispatch* dispatch = null;
        ComServiceProvider* services = null;
        IShellBrowser* browser = null;
        IShellView* shellView = null;
        IFolderView2* view = null;
        try
        {
            ShellWindows.CreateInstance(out windows).ThrowOnFailure();

            VARIANT location = default; // CSIDL_DESKTOP is 0.
            location.vt = VARENUM.VT_I4;
            VARIANT root = default;
            int hwnd;
            dispatch = windows->FindWindowSW(
                &location,
                &root,
                ShellWindowTypeConstants.SWC_DESKTOP,
                &hwnd,
                ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
            if (dispatch is null)
            {
                return null; // S_FALSE: no desktop window is registered yet.
            }

            dispatch->QueryInterface(out services).ThrowOnFailure();
            services->QueryService(PInvoke.SID_STopLevelBrowser, out browser);
            browser->QueryActiveShellView(&shellView);
            if (shellView is null)
            {
                return null;
            }

            shellView->QueryInterface(out view).ThrowOnFailure();
            var result = new DesktopFolderView(view);
            view = null;
            return result;
        }
        finally
        {
            Com.Release(ref view);
            Com.Release(ref shellView);
            Com.Release(ref browser);
            Com.Release(ref services);
            Com.Release(ref dispatch);
            Com.Release(ref windows);
        }
    }

    /// <summary>The number of items in the view (<c>IFolderView::ItemCount(SVGIO_ALLVIEW)</c>).</summary>
    public int ItemCount()
    {
        CheckAccess();
        _view->ItemCount((uint)_SVGIO.SVGIO_ALLVIEW, out var count);
        return count;
    }

    /// <summary>The view's current <c>FOLDERFLAGS</c> (<c>IFolderView2::GetCurrentFolderFlags</c>).</summary>
    public uint GetFolderFlags()
    {
        CheckAccess();
        _view->GetCurrentFolderFlags(out var flags);
        return flags;
    }

    /// <summary>
    /// Changes the view's <c>FOLDERFLAGS</c> (<c>IFolderView2::SetCurrentFolderFlags</c>). The only call in this class
    /// that changes the desktop; used solely by <see cref="IconLayer.DesktopIconSwitch"/> for <c>FWF_NOICONS</c>.
    /// </summary>
    public void SetFolderFlags(uint mask, uint flags)
    {
        CheckAccess();
        _view->SetCurrentFolderFlags(mask, flags);
    }

    /// <summary>Lists every item the view holds, in view order.</summary>
    public DesktopSnapshot ReadItems()
    {
        CheckAccess();
        IEnumIDList* enumerator = null;
        var items = new List<DesktopItem>();
        var skipped = 0;
        try
        {
            var iid = IEnumIDList.IID_Guid;
            _view->Items((uint)_SVGIO.SVGIO_ALLVIEW, &iid, (void**)&enumerator);
            if (enumerator is null)
            {
                return DesktopSnapshot.Empty;
            }

            var batch = stackalloc ITEMIDLIST*[_batchSize];
            while (true)
            {
                uint fetched = 0;
                var hr = enumerator->Next(_batchSize, batch, &fetched);
                hr.ThrowOnFailure();
                for (var i = 0; i < fetched; i++)
                {
                    try
                    {
                        if (DesktopItemFactory.FromIdList(batch[i]) is { } item)
                        {
                            items.Add(item);
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    finally
                    {
                        PInvoke.CoTaskMemFree(batch[i]);
                    }
                }

                if (hr != HRESULT.S_OK || fetched == 0)
                {
                    break;
                }
            }
        }
        finally
        {
            Com.Release(ref enumerator);
        }

        return new DesktopSnapshot(items, skipped);
    }

    public void Dispose()
    {
        CheckAccess();
        Com.Release(ref _view);
    }

    private void CheckAccess()
    {
        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException("The desktop view must be used on the thread that acquired it.");
        }
    }
}
