namespace RuinaoSoftwareWpf;

using System.IO;
using System.IO.Pipes;

/// <summary>
/// 使用命名 Mutex 保证当前 Windows 会话内只有一个主程序实例，
/// 并通过命名管道通知已有实例恢复主窗口。
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const byte ActivateCommand = 1;
    private const byte AcceptedResponse = 1;

    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource listenerCancellation = new();
    private Task? listenerTask;
    private bool ownsMutex;
    private int listenerStarted;
    private int listenerStopped;
    private int disposed;

    public SingleInstanceCoordinator(string mutexName, string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

        mutex = new Mutex(initiallyOwned: false, mutexName);
        this.pipeName = pipeName;
    }

    public event EventHandler? ActivationRequested;

    public bool TryAcquireOwnership(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (ownsMutex)
        {
            return true;
        }

        try
        {
            ownsMutex = mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // 原实例异常结束时，Windows 会把 Mutex 所有权交给当前等待线程。
            ownsMutex = true;
        }

        return ownsMutex;
    }

    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ownsMutex)
        {
            throw new InvalidOperationException("只有主实例可以监听窗口激活请求。");
        }

        if (Interlocked.CompareExchange(ref listenerStarted, 1, 0) != 0)
        {
            return;
        }

        listenerTask = Task.Run(() => ListenAsync(listenerCancellation.Token));
    }

    public void StopListening()
    {
        if (Interlocked.Exchange(ref listenerStopped, 1) == 0)
        {
            listenerCancellation.Cancel();
        }
    }

    public async Task<bool> TryActivateExistingAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);

        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutCancellation.Token).ConfigureAwait(false);

            var command = new[] { ActivateCommand };
            await client.WriteAsync(command, timeoutCancellation.Token).ConfigureAwait(false);
            await client.FlushAsync(timeoutCancellation.Token).ConfigureAwait(false);

            var response = new byte[1];
            var bytesRead = await client.ReadAsync(response, timeoutCancellation.Token).ConfigureAwait(false);
            return bytesRead == 1 && response[0] == AcceptedResponse;
        }
        catch (Exception exception) when (exception is OperationCanceledException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        StopListening();
        if (ownsMutex)
        {
            mutex.ReleaseMutex();
            ownsMutex = false;
        }

        mutex.Dispose();
        listenerCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var command = new byte[1];
                var bytesRead = await server.ReadAsync(command, cancellationToken).ConfigureAwait(false);
                if (bytesRead != 1 || command[0] != ActivateCommand)
                {
                    continue;
                }

                ActivationRequested?.Invoke(this, EventArgs.Empty);
                var response = new[] { AcceptedResponse };
                await server.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                await server.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                // 单次客户端中断不终止主实例监听，继续等待下一次启动通知。
            }
        }
    }
}
