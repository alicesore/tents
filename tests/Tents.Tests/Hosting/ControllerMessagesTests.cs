using Tents.Shell.Hosting;

namespace Tents.Tests.Hosting;

public sealed class ControllerMessagesTests
{
    private const int _taskbarCreated = 0xC123;
    private readonly ControllerMessages _messages = new(_taskbarCreated);

    [Fact]
    public void TaskbarCreated_IsExplorerRestarted() =>
        Assert.Equal(ControllerEvent.ExplorerRestarted, _messages.Classify(_taskbarCreated, 0));

    [Theory]
    [InlineData(ControllerMessages.WmDisplayChange, 32, ControllerEvent.DisplayChanged)]
    [InlineData(ControllerMessages.WmWtsSessionChange, ControllerMessages.WtsSessionLock, ControllerEvent.SessionLocked)]
    [InlineData(ControllerMessages.WmWtsSessionChange, ControllerMessages.WtsSessionUnlock, ControllerEvent.SessionUnlocked)]
    [InlineData(ControllerMessages.WmWtsSessionChange, 0x1, ControllerEvent.None)] // WTS_CONSOLE_CONNECT
    [InlineData(ControllerMessages.WmPowerBroadcast, ControllerMessages.PbtApmResumeAutomatic, ControllerEvent.Resumed)]
    [InlineData(ControllerMessages.WmPowerBroadcast, 0x7, ControllerEvent.None)] // PBT_APMRESUMESUSPEND, not used
    [InlineData(ControllerMessages.WmPowerBroadcast, 0x4, ControllerEvent.None)] // PBT_APMSUSPEND
    [InlineData(ControllerMessages.WmEndSession, 1, ControllerEvent.SessionEnding)]
    [InlineData(ControllerMessages.WmEndSession, 0, ControllerEvent.None)]
    [InlineData(ControllerMessages.WmClose, 0, ControllerEvent.CloseRequested)]
    [InlineData(0x0200, 0, ControllerEvent.None)] // WM_MOUSEMOVE
    public void Classify_MapsTheNotificationsTentsUses(int message, int wParam, ControllerEvent expected) =>
        Assert.Equal(expected, _messages.Classify(message, wParam));

    [Fact]
    public void Constructor_RejectsAnUnregisteredMessage() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ControllerMessages(0));

    [Fact]
    public void Register_ReturnsTheSessionWideTaskbarCreatedId()
    {
        var a = ControllerMessages.Register();
        var b = ControllerMessages.Register();

        Assert.InRange(a.TaskbarCreatedMessage, 0xC000, 0xFFFF);
        Assert.Equal(a.TaskbarCreatedMessage, b.TaskbarCreatedMessage);
    }
}
