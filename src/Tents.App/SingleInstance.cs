namespace Tents.App;

/// <summary>
/// One Tents per session. A second start would put a second set of tents on the desktop and fight over the native
/// icons, so it exits at once.
/// </summary>
/// <remarks>
/// A named mutex in the session's <c>Local\</c> namespace: each signed-in user (and each Remote Desktop session) runs its
/// own Tents. The mutex is released by Windows when the process ends, also when it is killed.
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    public const string DefaultName = @"Local\Tents.App.SingleInstance";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Takes the session's Tents lock, or returns null when another Tents holds it.</summary>
    public static SingleInstance? TryAcquire(string name = DefaultName)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
