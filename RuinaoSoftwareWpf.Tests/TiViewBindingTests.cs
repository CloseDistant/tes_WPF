using System.Xml.Linq;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class TiViewBindingTests
{
    [Fact]
    public void View_BlankClickCommitsFocusedParameterInput()
    {
        var rootGrid = LoadTiView()
            .Root?
            .Elements()
            .Single(element => element.Name.LocalName == "Grid");

        Assert.Equal(
            "CommitFocusedInputOnBlankClick",
            rootGrid?.Attribute("PreviewMouseLeftButtonDown")?.Value);
    }

    [Fact]
    public void ParameterDownloadProgress_ReadOnlyViewModelProperty_UsesOneWayBinding()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "RuinaoSoftwareWpf",
            "Views",
            "Stimulation",
            "TemporalInterference",
            "TiControlView.xaml");
        var document = XDocument.Load(path);
        var progressBinding = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProgressBar")
            .Select(element => element.Attribute("Value")?.Value)
            .Single(value => value?.Contains(
                nameof(TiControlViewModel.ParameterDownloadPercentage),
                StringComparison.Ordinal) == true);

        Assert.Contains("Mode=OneWay", progressBinding, StringComparison.Ordinal);
    }

    [Fact]
    public void ChannelCard_KeepsUnsupportedIntervalFieldsVisible()
    {
        var document = LoadTiView();
        var card = document
            .Descendants()
            .Single(element => element.Name.LocalName == "StimulationChannelCard");

        Assert.Equal("True", card.Attribute("ShowIntervalTime")?.Value);
        Assert.Equal("True", card.Attribute("ShowSingleDuration")?.Value);
        Assert.Equal("True", card.Attribute("EnableSimulatedWaveform")?.Value);
    }

    [Fact]
    public void DemoChannels_ShowDashForContinuousOnlyTimingFields()
    {
        var channels = new DemoTiGroupFactory()
            .CreateDemoGroups()
            .SelectMany(group => group.Channels);

        Assert.All(channels, channel =>
        {
            Assert.Equal("-", channel.IntervalDisplay);
            Assert.Equal("-", channel.SingleDurationDisplay);
            Assert.False(channel.AreIntervalFieldsEditable);
        });
    }

    private static XDocument LoadTiView()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "RuinaoSoftwareWpf",
            "Views",
            "Stimulation",
            "TemporalInterference",
            "TiControlView.xaml");
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
