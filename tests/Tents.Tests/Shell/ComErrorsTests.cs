using System.Runtime.InteropServices;
using Tents.Shell.Interop;

namespace Tents.Tests.Shell;

public class ComErrorsTests
{
    [Theory]
    [InlineData(unchecked((int)0x80010108))]
    [InlineData(unchecked((int)0x80010007))]
    [InlineData(unchecked((int)0x80010012))]
    [InlineData(unchecked((int)0x800706BA))]
    [InlineData(unchecked((int)0x800401FD))]
    public void ServerGone_IsDisconnected(int hresult) => Assert.True(ComErrors.IsDisconnected(hresult));

    [Theory]
    [InlineData(0)]
    [InlineData(unchecked((int)0x80004005))] // E_FAIL
    [InlineData(unchecked((int)0x80070005))] // E_ACCESSDENIED
    public void OtherErrors_AreNotDisconnected(int hresult) => Assert.False(ComErrors.IsDisconnected(hresult));

    [Theory]
    [InlineData(unchecked((int)0x80004005))] // E_FAIL -> COMException
    [InlineData(unchecked((int)0x80070057))] // E_INVALIDARG -> ArgumentException
    [InlineData(unchecked((int)0x80004001))] // E_NOTIMPL -> NotImplementedException
    [InlineData(unchecked((int)0x80070005))] // E_ACCESSDENIED -> UnauthorizedAccessException
    [InlineData(unchecked((int)0x80070002))] // ERROR_FILE_NOT_FOUND -> FileNotFoundException
    [InlineData(unchecked((int)0x80070003))] // ERROR_PATH_NOT_FOUND -> DirectoryNotFoundException
    [InlineData(unchecked((int)0x80004002))] // E_NOINTERFACE -> InvalidCastException
    [InlineData(unchecked((int)0x80010108))] // RPC_E_DISCONNECTED
    public void ExceptionsFromFailedHResults_AreCallFailures(int hresult)
    {
        var exception = Marshal.GetExceptionForHR(hresult);

        Assert.NotNull(exception);
        Assert.True(ComErrors.IsCallFailure(exception), exception.GetType().Name);
    }

    [Fact]
    public void ProgrammingErrors_AreNotCallFailures()
    {
        Assert.False(ComErrors.IsCallFailure(new KeyNotFoundException()));
        Assert.False(ComErrors.IsCallFailure(new FormatException()));
        Assert.False(ComErrors.IsCallFailure(new ObjectDisposedException("x")));
    }
}
