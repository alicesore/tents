using Tents.App;

namespace Tents.Tests.App;

public sealed class SingleInstanceTests
{
    [Fact]
    public void ASecondInstance_IsRefused_UntilTheFirstEnds()
    {
        var name = @"Local\Tents.Tests." + Guid.NewGuid().ToString("N");
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var firstAcquired = false;

        // A mutex is owned by a thread and must be released there, so the first instance lives on its own thread, as a
        // separate process would.
        var first = new Thread(() =>
        {
            using var instance = SingleInstance.TryAcquire(name);
            firstAcquired = instance is not null;
            acquired.Set();
            release.Wait();
        });
        first.Start();
        acquired.Wait();

        Assert.True(firstAcquired);
        Assert.Null(OnOtherThread(() => SingleInstance.TryAcquire(name)));

        release.Set();
        first.Join();
        var third = OnOtherThread(() =>
        {
            var instance = SingleInstance.TryAcquire(name);
            instance?.Dispose();
            return instance;
        });
        Assert.NotNull(third);
    }

    private static T OnOtherThread<T>(Func<T> work)
    {
        T result = default!;
        var thread = new Thread(() => result = work());
        thread.Start();
        thread.Join();
        return result;
    }
}
