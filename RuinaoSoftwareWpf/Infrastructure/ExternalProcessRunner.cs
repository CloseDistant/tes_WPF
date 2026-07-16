namespace RuinaoSoftwareWpf;

using System.Diagnostics;

internal sealed record ExternalProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ExternalProcessRunner
{
    public static async Task<ExternalProcessResult> RunAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动外部程序：{startInfo.FileName}");
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var outputTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"外部程序执行超时：{startInfo.FileName}，限制 {timeout.TotalSeconds:0} 秒。");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        return new ExternalProcessResult(process.ExitCode, outputTask.Result, errorTask.Result);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 进程可能已在检查后自行退出。
        }
    }
}
