using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

#pragma warning disable IDE0130 // The partial class has to live in CsWin32's namespace.
namespace Windows.Win32;
#pragma warning restore IDE0130

/// <summary>
/// Completes CsWin32's vtable support: the IUnknown slots of every vtable Tents builds come from
/// <see cref="ComWrappers.GetIUnknownImpl"/>, so reference counting and QueryInterface are the runtime's.
/// </summary>
public static unsafe partial class ComHelpers
{
    static partial void PopulateIUnknownImpl<TComInterface>(System.Com.IUnknown.Vtbl* vtable)
        where TComInterface : unmanaged
    {
        ComWrappers.GetIUnknownImpl(out var queryInterface, out var addRef, out var release);
        vtable->QueryInterface_1 = (delegate* unmanaged[Stdcall]<System.Com.IUnknown*, Guid*, void**, HRESULT>)queryInterface;
        vtable->AddRef_2 = (delegate* unmanaged[Stdcall]<System.Com.IUnknown*, uint>)addRef;
        vtable->Release_3 = (delegate* unmanaged[Stdcall]<System.Com.IUnknown*, uint>)release;
    }
}
