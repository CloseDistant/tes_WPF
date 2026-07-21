namespace RuinaoSoftwareWpf;

using System.Diagnostics;
using System.Globalization;
using System.IO;

public sealed class FemWorkerSimulationService : ISimulationService
{
    private readonly ILoggingService logger;
    private readonly object syncRoot = new();
    private Process? process;
    private CancellationTokenSource? activeCts;

    public FemWorkerSimulationService(ILoggingService logger)
    {
        this.logger = logger;
    }

    public bool IsRunning
    {
        get
        {
            lock (syncRoot)
            {
                return process is { HasExited: false };
            }
        }
    }

    public async Task<FemSimulationResult> RunAsync(
        FemSimulationRequest request,
        IProgress<FemSimulationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string workerExecutable;
        try
        {
            workerExecutable = TrustedExecutablePath.RequireTrustedToolPath(request.WorkerExecutable);
        }
        catch (Exception exception) when (exception is IOException or System.Security.SecurityException)
        {
            logger.Warning($"FEM Worker 路径被拒绝：{exception.Message}");
            return new FemSimulationResult(false, null, request.OutputDirectory, "FEM Worker 路径不受信任。", TimeSpan.Zero);
        }

        lock (syncRoot)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("已有 FEM 任务正在运行。");
            }

            activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        Directory.CreateDirectory(request.OutputDirectory);
        var startedAt = Stopwatch.GetTimestamp();
        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(activeCts.Token, timeoutCts.Token);
        var startInfo = new ProcessStartInfo
        {
            FileName = workerExecutable,
            Arguments = request.Arguments,
            WorkingDirectory = request.OutputDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var worker = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        lock (syncRoot)
        {
            process = worker;
        }

        try
        {
            if (!worker.Start())
            {
                return new FemSimulationResult(false, null, request.OutputDirectory, "FEM Worker 启动失败。", Stopwatch.GetElapsedTime(startedAt));
            }

            progress?.Report(new FemSimulationProgress(0, "started", "FEM Worker 已启动。"));
            var outputTask = PumpProgressAsync(worker.StandardOutput, progress, linkedCts.Token);
            var errorTask = worker.StandardError.ReadToEndAsync(linkedCts.Token);
            var resourceTask = MonitorResourcesAsync(worker, request.MaximumWorkingSetBytes, linkedCts.Token);
            await worker.WaitForExitAsync(linkedCts.Token);
            await outputTask;
            var error = await errorTask;
            linkedCts.Cancel();
            try { await resourceTask; } catch (OperationCanceledException) { }

            var succeeded = worker.ExitCode == 0;
            var message = succeeded ? "FEM 计算完成。" : $"FEM Worker 失败：{error.Trim()}";
            progress?.Report(new FemSimulationProgress(succeeded ? 100 : 0, succeeded ? "completed" : "failed", message));
            return new FemSimulationResult(succeeded, worker.ExitCode, request.OutputDirectory, message, Stopwatch.GetElapsedTime(startedAt));
        }
        catch (OperationCanceledException)
        {
            KillWorker(worker);
            var reason = timeoutCts.IsCancellationRequested ? "FEM 计算超时。" : "FEM 计算已取消。";
            return new FemSimulationResult(false, worker.HasExited ? worker.ExitCode : null, request.OutputDirectory, reason, Stopwatch.GetElapsedTime(startedAt));
        }
        catch (Exception exception)
        {
            KillWorker(worker);
            logger.Error("FEM Worker 运行失败", exception);
            return new FemSimulationResult(false, null, request.OutputDirectory, exception.Message, Stopwatch.GetElapsedTime(startedAt));
        }
        finally
        {
            lock (syncRoot)
            {
                process = null;
                activeCts?.Dispose();
                activeCts = null;
            }

            worker.Dispose();
        }
    }

    public Task CancelAsync()
    {
        lock (syncRoot)
        {
            activeCts?.Cancel();
        }

        return Task.CompletedTask;
    }

    private static async Task PumpProgressAsync(
        StreamReader reader,
        IProgress<FemSimulationProgress>? progress,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var parts = line.Split('|', 3);
            if (parts.Length == 3 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                progress?.Report(new FemSimulationProgress(Math.Clamp(percent, 0, 100), parts[1], parts[2]));
            }
        }
    }

    private static async Task MonitorResourcesAsync(Process worker, long maximumWorkingSetBytes, CancellationToken cancellationToken)
    {
        while (!worker.HasExited)
        {
            worker.Refresh();
            if (maximumWorkingSetBytes > 0 && worker.WorkingSet64 > maximumWorkingSetBytes)
            {
                KillWorker(worker);
                throw new InvalidOperationException("FEM Worker 超过内存上限，已终止。");
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    private static void KillWorker(Process worker)
    {
        if (!worker.HasExited)
        {
            worker.Kill(entireProcessTree: true);
        }
    }
}
