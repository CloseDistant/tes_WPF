namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class LegacyEegAcquisitionServiceAdapterTests
{
    [Fact]
    public void AddMarker_RejectsEmptyCode()
    {
        using var legacy = new MockEegAcquisitionService();
        var adapter = new LegacyEegAcquisitionServiceAdapter(legacy);

        Assert.Throws<ArgumentException>(() => adapter.AddMarker(
            new EegMarkerDefinition(
                string.Empty,
                "刺激",
                "F8",
                "#FFB53D3F"),
            "test"));
    }

    [Fact]
    public async Task AddMarker_PreservesProvidedCodeWhenMarkersAreReadBack()
    {
        using var legacy = new MockEegAcquisitionService();
        var adapter = new LegacyEegAcquisitionServiceAdapter(legacy);
        await adapter.StartAsync(
            "marker-code-regression",
            TestContext.Current.CancellationToken);

        adapter.AddMarker(
            new EegMarkerDefinition(
                "clinical-event-001",
                "可本地化的显示名称",
                "F8",
                "#FFB53D3F"),
            "test");

        var marker = Assert.Single(adapter.GetMarkers());
        Assert.Equal("clinical-event-001", marker.Code);
        Assert.Equal("可本地化的显示名称", marker.DisplayName);

        await adapter.StopAsync(TestContext.Current.CancellationToken);
    }
}
