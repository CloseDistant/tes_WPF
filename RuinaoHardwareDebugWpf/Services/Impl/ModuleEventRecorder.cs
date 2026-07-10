namespace RuinaoHardwareDebugWpf;

using System.Text.Json;

public sealed class ModuleEventRecorder : IModuleEventRecorder
{
    private readonly ICaptureMediaRecorder mediaRecorder;
    private readonly ILoggingService logger;
    private readonly object syncRoot = new();
    private Task pendingWrite = Task.CompletedTask;

    public ModuleEventRecorder(ICaptureMediaRecorder mediaRecorder, ILoggingService logger)
    {
        this.mediaRecorder = mediaRecorder;
        this.logger = logger;
    }

    public void Enqueue(
        string eventType,
        string message,
        object payload,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        var session = mediaRecorder.CurrentSession;
        if (session is null)
        {
            return;
        }

        lock (syncRoot)
        {
            pendingWrite = WriteAfterPreviousAsync(pendingWrite, session, eventType, message, payloadJson, startedAt, endedAt);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (syncRoot)
        {
            pending = pendingWrite;
        }

        await pending.WaitAsync(cancellationToken);
    }

    private async Task WriteAfterPreviousAsync(
        Task previous,
        CaptureSessionInfo session,
        string eventType,
        string message,
        string payloadJson,
        DateTimeOffset? startedAt,
        DateTimeOffset? endedAt)
    {
        try
        {
            await previous.ConfigureAwait(false);
            await mediaRecorder.RecordModuleEventAsync(session, eventType, message, payloadJson, startedAt, endedAt).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Error($"模块事件记录失败：eventType={eventType}", exception);
        }
    }
}
