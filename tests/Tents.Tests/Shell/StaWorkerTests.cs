using Tents.Shell.Threading;

namespace Tents.Tests.Shell;

public class StaWorkerTests
{
    [Fact]
    public async Task Work_RunsOnOneStaThread_NotTheCaller()
    {
        using var worker = new StaWorker("test worker");

        var apartment = await worker.InvokeAsync(() => Thread.CurrentThread.GetApartmentState());
        var first = await worker.InvokeAsync(() => Environment.CurrentManagedThreadId);
        var second = await worker.InvokeAsync(() => Environment.CurrentManagedThreadId);

        Assert.Equal(ApartmentState.STA, apartment);
        Assert.Equal(first, second);
        Assert.NotEqual(Environment.CurrentManagedThreadId, second);
    }

    [Fact]
    public async Task Work_RunsInOrder()
    {
        using var worker = new StaWorker("test worker");
        var seen = new List<int>();

        var tasks = Enumerable.Range(0, 200).Select(i => worker.InvokeAsync(() => seen.Add(i))).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(0, 200), seen);
    }

    [Fact]
    public async Task Exception_GoesToTheCaller_AndTheWorkerKeepsRunning()
    {
        using var worker = new StaWorker("test worker");

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.InvokeAsync(() => throw new InvalidOperationException()));
        Assert.Equal(42, await worker.InvokeAsync(() => 42));
    }

    [Fact]
    public async Task IsCurrentThread_IsTrueOnlyOnTheWorker()
    {
        using var worker = new StaWorker("test worker");

        Assert.True(await worker.InvokeAsync(() => worker.IsCurrentThread));
        Assert.False(worker.IsCurrentThread);
    }

    [Fact]
    public async Task WorkQueuedFromTheWorker_RunsAfterTheCurrentItem()
    {
        using var worker = new StaWorker("test worker");
        var order = new List<string>();

        Task? inner = null;
        await worker.InvokeAsync(() =>
        {
            inner = worker.InvokeAsync(() => order.Add("inner"));
            order.Add("outer");
        });
        await inner!;

        Assert.Equal(["outer", "inner"], order);
    }

    [Fact]
    public async Task AfterDispose_NewWorkIsRejected()
    {
        var worker = new StaWorker("test worker");
        await worker.InvokeAsync(() => { });

        worker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = worker.InvokeAsync(() => { }); });
    }

    [Fact]
    public void Dispose_BeforeAnyWork_StartsNoThread()
    {
        var worker = new StaWorker("test worker");

        worker.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = worker.InvokeAsync(() => 1); });
    }
}
