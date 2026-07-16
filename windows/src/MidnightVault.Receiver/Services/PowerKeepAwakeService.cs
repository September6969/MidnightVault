using System.Runtime.InteropServices;

namespace MidnightVault.Receiver.Services;

/// <summary>
/// 上传活跃时定期重置系统空闲计时器，避免夜间传输被自动睡眠中断。
/// </summary>
public sealed class PowerKeepAwakeService(
    ILogger<PowerKeepAwakeService> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan ActivityWindow = TimeSpan.FromMinutes(20);
    private readonly AutoResetEvent _signal = new(initialState: false);
    private readonly CancellationTokenSource _stopping = new();
    private Thread? _worker;
    private long _activeUntilUtcTicks;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "MidnightVault 电源保持器"
        };
        _worker.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        _signal.Set();
        _worker?.Join(TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public void NotifyUploadActivity()
    {
        var requested = DateTimeOffset.UtcNow.Add(ActivityWindow).UtcTicks;
        while (true)
        {
            var current = Interlocked.Read(ref _activeUntilUtcTicks);
            if (current >= requested ||
                Interlocked.CompareExchange(ref _activeUntilUtcTicks, requested, current) == current)
            {
                break;
            }
        }
        _signal.Set();
    }

    public void Dispose()
    {
        _stopping.Dispose();
        _signal.Dispose();
    }

    private void Run()
    {
        var keepingAwake = false;
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var shouldKeepAwake = DateTimeOffset.UtcNow.UtcTicks <=
                    Interlocked.Read(ref _activeUntilUtcTicks);
                var flags = ExecutionState.Continuous;
                if (shouldKeepAwake) flags |= ExecutionState.SystemRequired;

                if (shouldKeepAwake || keepingAwake != shouldKeepAwake)
                {
                    if (SetThreadExecutionState(flags) == 0)
                    {
                        logger.LogWarning("无法更新 Windows 电源保持状态。");
                    }
                    keepingAwake = shouldKeepAwake;
                }
                _signal.WaitOne(TimeSpan.FromSeconds(30));
            }
        }
        finally
        {
            if (keepingAwake) SetThreadExecutionState(ExecutionState.Continuous);
        }
    }

    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);
}
