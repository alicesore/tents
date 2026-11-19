namespace Tents.Tests;

/// <summary>Runs test code on a fresh STA thread (windows, OLE and WPF need one; xunit's threads are MTA).</summary>
internal static class StaThread
{
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
        }
    }
}
