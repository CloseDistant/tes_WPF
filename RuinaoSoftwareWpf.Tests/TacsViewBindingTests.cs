using System.Xml.Linq;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TacsViewBindingTests
{
    [Fact]
    public void View_UsesFlatTdcsStyleChannelPairsAndDedicatedTacsCardMode()
    {
        var document = LoadView();
        var channelSelector = document
            .Descendants()
            .First(element => element.Name.LocalName == "ItemsControl"
                && element.Attribute("ItemsSource")?.Value.Contains("ChannelPairs", StringComparison.Ordinal) == true);
        var card = document
            .Descendants()
            .Single(element => element.Name.LocalName == "StimulationChannelCard");

        Assert.NotNull(channelSelector);
        Assert.Equal("tACS", card.Attribute("AlternatingCurrentModeCode")?.Value);
        Assert.Equal("True", card.Attribute("ShowCarrierFrequency")?.Value);
        Assert.Equal("True", card.Attribute("ShowIntervalTime")?.Value);
        Assert.Equal("True", card.Attribute("ShowSingleDuration")?.Value);
    }

    [Fact]
    public void ParameterDownloadProgress_UsesOneWayBinding()
    {
        var progressBinding = LoadView()
            .Descendants()
            .Where(element => element.Name.LocalName == "ProgressBar")
            .Select(element => element.Attribute("Value")?.Value)
            .Single(value => value?.Contains(
                nameof(TacsControlViewModel.ParameterDownloadPercentage),
                StringComparison.Ordinal) == true);

        Assert.Contains("Mode=OneWay", progressBinding, StringComparison.Ordinal);
    }

    private static XDocument LoadView()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "RuinaoSoftwareWpf",
            "Views",
            "Stimulation",
            "AlternatingCurrent",
            "TacsControlView.xaml");
        return XDocument.Load(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ruinao.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到包含 Ruinao.slnx 的仓库根目录。");
    }
}
