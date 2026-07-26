using CustomLauncher.Models;

namespace CustomLauncher.Core;

public interface IServerStatusTimer : IAsyncDisposable
{
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken);
}

public interface IServerStatusTimerFactory
{
    IServerStatusTimer Create(TimeSpan interval);
}

public sealed class PeriodicServerStatusTimerFactory : IServerStatusTimerFactory
{
    public IServerStatusTimer Create(TimeSpan interval) => new Timer(interval);

    private sealed class Timer : IServerStatusTimer
    {
        private readonly PeriodicTimer timer;

        public Timer(TimeSpan interval) => timer = new PeriodicTimer(interval);

        public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken) =>
            timer.WaitForNextTickAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            timer.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class ServerStatusPollingService : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<ServerStatusInfo>> checkStatus;
    private readonly Func<bool> isActive;
    private readonly IServerStatusTimerFactory timerFactory;
    private readonly TimeSpan baseInterval;
    private readonly TimeSpan maxInterval;
    private readonly TimeSpan inactiveInterval;
    private readonly object sync = new();
    private CancellationTokenSource? cancellation;
    private Task? pollingTask;

    public ServerStatusPollingService(
        Func<CancellationToken, Task<ServerStatusInfo>> checkStatus,
        Func<bool>? isActive = null,
        IServerStatusTimerFactory? timerFactory = null,
        TimeSpan? baseInterval = null,
        TimeSpan? maxInterval = null,
        TimeSpan? inactiveInterval = null)
    {
        this.checkStatus = checkStatus ?? throw new ArgumentNullException(nameof(checkStatus));
        this.isActive = isActive ?? (() => true);
        this.timerFactory = timerFactory ?? new PeriodicServerStatusTimerFactory();
        this.baseInterval = baseInterval ?? TimeSpan.FromSeconds(60);
        this.maxInterval = maxInterval ?? TimeSpan.FromMinutes(5);
        this.inactiveInterval = inactiveInterval ?? TimeSpan.FromMinutes(5);

        if (this.baseInterval < TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(nameof(baseInterval), "The status API must not be polled more frequently than once per minute.");
        }

        if (this.maxInterval < this.baseInterval || this.inactiveInterval < this.baseInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInterval), "Maximum and inactive intervals cannot be shorter than the base interval.");
        }
    }

    public event EventHandler<ServerStatusInfo>? StatusChanged;

    public void Start()
    {
        lock (sync)
        {
            if (pollingTask is { IsCompleted: false })
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            pollingTask = RunAsync(cancellation.Token);
        }
    }

    public async Task StopAsync()
    {
        Task? task;
        CancellationTokenSource? source;
        lock (sync)
        {
            task = pollingTask;
            source = cancellation;
            pollingTask = null;
            cancellation = null;
        }

        if (source is null)
        {
            return;
        }

        source.Cancel();
        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            source.Dispose();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = baseInterval;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (isActive())
            {
                var status = await checkStatus(cancellationToken).ConfigureAwait(false);
                StatusChanged?.Invoke(this, status);
                interval = status.RequestSucceeded ? baseInterval : DoubleWithCap(interval, maxInterval);
            }
            else
            {
                interval = inactiveInterval;
            }

            await using var timer = timerFactory.Create(interval);
            if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static TimeSpan DoubleWithCap(TimeSpan value, TimeSpan cap)
    {
        var doubledTicks = value.Ticks > long.MaxValue / 2 ? long.MaxValue : value.Ticks * 2;
        return TimeSpan.FromTicks(Math.Min(doubledTicks, cap.Ticks));
    }
}
