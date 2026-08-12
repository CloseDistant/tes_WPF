using System.Globalization;
using System.Text;

namespace RuinaoSoftwareWpf;

internal static class StimulationImpedanceStartPolicy
{
    public static StimulationImpedanceStartAssessment<TChannel> Evaluate<TChannel>(
        IEnumerable<TChannel> channels)
        where TChannel : IStimulationImpedanceChannel
    {
        var all = channels.ToArray();
        var warnings = all.Where(channel => channel.ImpedanceStatus == StimulationImpedanceStatus.Warning).ToArray();
        var critical = all.Where(channel => channel.ImpedanceStatus == StimulationImpedanceStatus.Critical).ToArray();
        var unavailable = all.Where(channel => channel.ImpedanceStatus == StimulationImpedanceStatus.Unavailable).ToArray();
        var eligible = all.Where(channel => channel.ImpedanceStatus is
            StimulationImpedanceStatus.Normal or StimulationImpedanceStatus.Warning).ToArray();
        return new StimulationImpedanceStartAssessment<TChannel>(eligible, warnings, critical, unavailable);
    }

    public static string BuildConfirmationMessage<TChannel>(
        StimulationImpedanceStartAssessment<TChannel> assessment)
        where TChannel : IStimulationImpedanceChannel
    {
        var message = new StringBuilder();
        if (assessment.WarningChannels.Count > 0)
        {
            message.AppendLine("通道阻抗偏高：");
            message.AppendLine();
            foreach (var channel in assessment.WarningChannels)
            {
                message.AppendLine($"{FormatChannelName(channel.Name)}：{FormatKiloOhms(channel.ImpedanceOhms!.Value)}kΩ");
            }
        }

        if (assessment.CriticalChannels.Count > 0 || assessment.UnavailableChannels.Count > 0)
        {
            if (message.Length > 0)
            {
                message.AppendLine();
            }

            message.AppendLine("以下通道不会启动：");
            message.AppendLine();
            foreach (var channel in assessment.CriticalChannels)
            {
                message.AppendLine($"{FormatChannelName(channel.Name)}：阻抗过高");
            }

            foreach (var channel in assessment.UnavailableChannels)
            {
                message.AppendLine($"{FormatChannelName(channel.Name)}：阻抗不可用");
            }
        }

        message.AppendLine();
        message.Append("是否仍要开始其余符合条件的通道？");
        return message.ToString();
    }

    public static string BuildSingleChannelBlockedMessage(IStimulationImpedanceChannel channel) =>
        channel.ImpedanceStatus == StimulationImpedanceStatus.Critical
            ? $"{FormatChannelName(channel.Name)}阻抗超过20kΩ，禁止启动刺激。"
            : $"{FormatChannelName(channel.Name)}阻抗不可用，禁止启动刺激。";

    public static string BuildSingleWarningConfirmationMessage(IStimulationImpedanceChannel channel) =>
        $"通道阻抗偏高：\n\n{FormatChannelName(channel.Name)}："
        + $"{FormatKiloOhms(channel.ImpedanceOhms!.Value)}kΩ\n\n是否仍要开始刺激？";

    private static string FormatKiloOhms(decimal impedanceOhms) =>
        (impedanceOhms / 1000m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatChannelName(string name) =>
        name.Replace(" ", string.Empty, StringComparison.Ordinal);
}
