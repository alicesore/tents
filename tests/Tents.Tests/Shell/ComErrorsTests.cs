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
}
