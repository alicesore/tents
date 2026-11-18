namespace Tents.Shell.Interop;

/// <summary>HRESULT classification for calls into Explorer's process.</summary>
public static class ComErrors
{
    /// <summary>RPC_E_DISCONNECTED: the object invoked has disconnected from its clients.</summary>
    public const int RpcDisconnected = unchecked((int)0x80010108);

    /// <summary>RPC_E_SERVER_DIED: the server process ended during the call.</summary>
    public const int RpcServerDied = unchecked((int)0x80010007);

    /// <summary>RPC_E_SERVER_DIED_DNE: the server process ended and the call did not execute.</summary>
    public const int RpcServerDiedNotExecuted = unchecked((int)0x80010012);

    /// <summary>HRESULT_FROM_WIN32(RPC_S_SERVER_UNAVAILABLE): the RPC server is unavailable.</summary>
    public const int RpcServerUnavailable = unchecked((int)0x800706BA);

    /// <summary>CO_E_OBJNOTCONNECTED: the object is not connected to its server.</summary>
    public const int ObjectNotConnected = unchecked((int)0x800401FD);

    /// <summary>
    /// True when <paramref name="hresult"/> means the remote object is gone, typically because Explorer restarted.
    /// The caller then drops its proxy and acquires a new one. Values from
    /// <see href="https://learn.microsoft.com/windows/win32/com/com-error-codes-1">COM error codes</see>.
    /// </summary>
    public static bool IsDisconnected(int hresult) => hresult is RpcDisconnected or RpcServerDied
        or RpcServerDiedNotExecuted or RpcServerUnavailable or ObjectNotConnected;
}
