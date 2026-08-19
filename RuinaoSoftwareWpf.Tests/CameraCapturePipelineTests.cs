namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class CameraCapturePipelineTests
{
    [Fact]
    public void FixedIntervalFrameSampler_LateInputDoesNotAccumulateSamplingDrift()
    {
        var sampler = new FixedIntervalFrameSampler(TimeSpan.FromMilliseconds(80), 1000);

        Assert.True(sampler.ShouldSample(0));
        Assert.False(sampler.ShouldSample(30));
        Assert.False(sampler.ShouldSample(60));
        Assert.True(sampler.ShouldSample(90));
        Assert.False(sampler.ShouldSample(120));
        Assert.False(sampler.ShouldSample(150));
        Assert.True(sampler.ShouldSample(160));
    }

    [Fact]
    public void ReplacingDisposableSlot_AlwaysKeepsLatestValueAndDisposesReplacedValue()
    {
        var slot = new ReplacingDisposableSlot<TestDisposable>();
        var first = new TestDisposable();
        var second = new TestDisposable();

        slot.Publish(first);
        slot.Publish(second);

        Assert.True(first.IsDisposed);
        Assert.Same(second, slot.Take());
        Assert.False(second.IsDisposed);
        Assert.Null(slot.Take());
    }

    [Fact]
    public void ReplacingDisposableSlot_ClearDisposesPendingValue()
    {
        var slot = new ReplacingDisposableSlot<TestDisposable>();
        var pending = new TestDisposable();
        slot.Publish(pending);

        slot.Clear();

        Assert.True(pending.IsDisposed);
        Assert.Null(slot.Take());
    }

    private sealed class TestDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
