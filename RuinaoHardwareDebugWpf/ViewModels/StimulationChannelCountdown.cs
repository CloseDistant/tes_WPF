using System.Collections.Concurrent;
using System.Globalization;

namespace RuinaoHardwareDebugWpf;

/// <summary>按通道维护刺激总时长倒计时。</summary>
public sealed class StimulationChannelCountdown
{
    private readonly ConcurrentDictionary<ChannelConfig, CancellationTokenSource> active = new();

    public event Action<ChannelConfig>? Completed;

    public void Start(ChannelConfig channel)
    {
        Cancel(channel, reset: false);

        var totalSeconds = ParseDurationSeconds(channel.DurationS);
        channel.RemainingTime = Format(totalSeconds);
        if (totalSeconds <= 0)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        active[channel] = cancellation;
        _ = RunAsync(channel, totalSeconds, cancellation);
    }

    public void Cancel(ChannelConfig channel, bool reset)
    {
        if (active.TryRemove(channel, out var cancellation))
        {
            cancellation.Cancel();
        }

        if (reset)
        {
            channel.RemainingTime = "00:00:00";
        }
    }

    public void CancelAll(IEnumerable<ChannelConfig> channels, bool reset)
    {
        foreach (var channel in channels)
        {
            Cancel(channel, reset);
        }
    }

    private async Task RunAsync(ChannelConfig channel, int totalSeconds, CancellationTokenSource owner)
    {
        var remaining = totalSeconds;
        var completed = false;
        var wasCanceled = false;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (remaining > 0 && await timer.WaitForNextTickAsync(owner.Token))
            {
                remaining--;
                channel.RemainingTime = Format(remaining);
            }

            completed = remaining == 0;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            wasCanceled = owner.IsCancellationRequested;
            if (active.TryGetValue(channel, out var current) && ReferenceEquals(current, owner))
            {
                active.TryRemove(channel, out _);
            }

            owner.Dispose();
        }

        if (completed && !wasCanceled)
        {
            Completed?.Invoke(channel);
        }
    }

    private static int ParseDurationSeconds(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || seconds <= 0)
        {
            return 0;
        }

        return (int)Math.Ceiling(seconds);
    }

    private static string Format(int totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }
}
