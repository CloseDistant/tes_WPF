namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void TryAcquireOwnership_RejectsAnotherThreadWhileOwnerIsAlive()
    {
        var identity = Guid.NewGuid().ToString("N");
        using var owner = new OwnedCoordinatorHost(
            identity,
            TestContext.Current.CancellationToken);
        using var contender = CreateCoordinator(identity);

        Assert.False(contender.TryAcquireOwnership(TimeSpan.Zero));
    }

    [Fact]
    public void TryAcquireOwnership_SucceedsAfterOwnerExits()
    {
        var identity = Guid.NewGuid().ToString("N");
        using (var owner = new OwnedCoordinatorHost(
                   identity,
                   TestContext.Current.CancellationToken))
        {
        }

        using var replacement = CreateCoordinator(identity);

        Assert.True(replacement.TryAcquireOwnership(TimeSpan.Zero));
    }

    [Fact]
    public async Task TryActivateExistingAsync_NotifiesOwningInstance()
    {
        var identity = Guid.NewGuid().ToString("N");
        using var owner = new OwnedCoordinatorHost(
            identity,
            TestContext.Current.CancellationToken);
        using var secondary = CreateCoordinator(identity);
        var activationReceived = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Coordinator.ActivationRequested += (_, _) => activationReceived.TrySetResult();

        var activated = await secondary.TryActivateExistingAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(activated);
        await activationReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TryActivateExistingAsync_ReturnsFalseWhenNoOwnerIsListening()
    {
        var identity = Guid.NewGuid().ToString("N");
        using var coordinator = CreateCoordinator(identity);

        var activated = await coordinator.TryActivateExistingAsync(
            TimeSpan.FromMilliseconds(200),
            TestContext.Current.CancellationToken);

        Assert.False(activated);
    }

    private static SingleInstanceCoordinator CreateCoordinator(string identity)
    {
        return new SingleInstanceCoordinator(
            $@"Local\RuinaoSoftwareWpf.Tests.{identity}",
            $"RuinaoSoftwareWpf.Tests.Activation.{identity}");
    }

    private sealed class OwnedCoordinatorHost : IDisposable
    {
        private readonly ManualResetEventSlim releaseOwner = new();
        private readonly Thread ownerThread;

        public OwnedCoordinatorHost(string identity, CancellationToken cancellationToken)
        {
            using var ownerReady = new ManualResetEventSlim();
            Exception? ownerException = null;
            ownerThread = new Thread(() =>
            {
                try
                {
                    using var coordinator = CreateCoordinator(identity);
                    Coordinator = coordinator;
                    if (!coordinator.TryAcquireOwnership(TimeSpan.Zero))
                    {
                        throw new InvalidOperationException("测试主实例未能取得 Mutex 所有权。");
                    }

                    coordinator.StartListening();
                    ownerReady.Set();
                    releaseOwner.Wait();
                }
                catch (Exception exception)
                {
                    ownerException = exception;
                    ownerReady.Set();
                }
            })
            {
                IsBackground = true
            };

            ownerThread.Start();
            if (!ownerReady.Wait(TimeSpan.FromSeconds(5), cancellationToken))
            {
                throw new TimeoutException("等待测试主实例启动超时。");
            }

            if (ownerException is not null)
            {
                throw new InvalidOperationException("测试主实例启动失败。", ownerException);
            }
        }

        public SingleInstanceCoordinator Coordinator { get; private set; } = null!;

        public void Dispose()
        {
            releaseOwner.Set();
            if (!ownerThread.Join(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("等待测试主实例退出超时。");
            }

            releaseOwner.Dispose();
        }
    }
}
