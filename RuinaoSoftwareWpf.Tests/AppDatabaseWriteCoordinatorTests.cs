namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class AppDatabaseWriteCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_SerializesWritesToTheSameDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = CreateCoordinator();
        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        var secondEntered = NewSignal();

        var first = coordinator.ExecuteAsync("main.db", async () =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
        }, cancellationToken);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

        var second = coordinator.ExecuteAsync("main.db", () =>
        {
            secondEntered.TrySetResult();
            return Task.CompletedTask;
        }, cancellationToken);

        await Task.Delay(50, cancellationToken);
        Assert.False(secondEntered.Task.IsCompleted);

        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.True(secondEntered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotBlockADifferentDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = CreateCoordinator();
        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        var secondEntered = NewSignal();

        var first = coordinator.ExecuteAsync("main.db", async () =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task;
        }, cancellationToken);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);

        var second = coordinator.ExecuteAsync("audit.db", () =>
        {
            secondEntered.TrySetResult();
            return Task.CompletedTask;
        }, cancellationToken);

        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
    }

    private static AppDatabaseWriteCoordinator CreateCoordinator() =>
        new(new NullLoggingService(), new NullRuntimeTelemetryService());

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class NullLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }

    private sealed class NullRuntimeTelemetryService : IRuntimeTelemetryService
    {
        public RuntimeTelemetrySnapshot GetSnapshot() => new(
            DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public void SetEegQueue(int depth, int capacity) { }
        public void RecordEegQueueDelay(TimeSpan delay) { }
        public void RecordEegRejectedBatch() { }
        public void RecordDatabaseCommitDelay(TimeSpan delay) { }
        public void RecordDiskWrite(long bytes, TimeSpan elapsed) { }
        public void RecordPacketLoss(long count = 1) { }
        public void RecordUiFrame(TimeSpan frameTime) { }
    }
}
