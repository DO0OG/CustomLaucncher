using CustomLauncher.Core;
using CustomLauncher.Models;

namespace CustomLauncher.Tests;

public sealed class ServerStatusPollingServiceTests
{
    [Fact]
    public async Task RunAsync_BacksOffAndResetsAfterSuccess()
    {
        var results = new Queue<ServerStatusInfo>(new[]
        {
            ServerStatusInfo.Unavailable(),
            ServerStatusInfo.Unavailable(),
            new ServerStatusInfo { RequestSucceeded = true, IsOnline = true },
        });
        var timers = new RecordingTimerFactory(ticksBeforeStop: 2);
        var service = new ServerStatusPollingService(
            _ => Task.FromResult(results.Dequeue()),
            timerFactory: timers,
            baseInterval: TimeSpan.FromSeconds(60),
            maxInterval: TimeSpan.FromMinutes(5));

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(
            new[] { TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(1) },
            timers.Intervals);
    }

    [Fact]
    public async Task RunAsync_DoesNotPollWhileInactive()
    {
        var calls = 0;
        var timers = new RecordingTimerFactory(ticksBeforeStop: 0);
        var service = new ServerStatusPollingService(
            _ => { calls++; return Task.FromResult(new ServerStatusInfo { RequestSucceeded = true }); },
            isActive: () => false,
            timerFactory: timers);

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Equal(TimeSpan.FromMinutes(5), Assert.Single(timers.Intervals));
    }

    private sealed class RecordingTimerFactory : IServerStatusTimerFactory
    {
        private readonly int ticksBeforeStop;
        private int ticks;

        public RecordingTimerFactory(int ticksBeforeStop) => this.ticksBeforeStop = ticksBeforeStop;

        public List<TimeSpan> Intervals { get; } = new();

        public IServerStatusTimer Create(TimeSpan interval)
        {
            Intervals.Add(interval);
            return new Timer(this);
        }

        private sealed class Timer : IServerStatusTimer
        {
            private readonly RecordingTimerFactory owner;

            public Timer(RecordingTimerFactory owner) => this.owner = owner;

            public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken) =>
                ValueTask.FromResult(owner.ticks++ < owner.ticksBeforeStop);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
