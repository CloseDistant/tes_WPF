using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class PrescriptionVisibilityTests
{
    [Fact]
    public void IsPrescriptionVisible_ReturnsFalseWhenItsStimulationTypeIsHidden()
    {
        IReadOnlySet<string> visibleTypes = new HashSet<string>(["tDCS", "tPCS"], StringComparer.Ordinal);

        var tiVisible = PrescriptionViewModel.IsPrescriptionVisible(
            CreatePrescription("TI"),
            visibleTypes);
        var tdcsVisible = PrescriptionViewModel.IsPrescriptionVisible(
            CreatePrescription("tDCS"),
            visibleTypes);

        Assert.False(tiVisible);
        Assert.True(tdcsVisible);
    }

    private static PrescriptionDefinition CreatePrescription(string stimulationType) => new(
        Id: stimulationType,
        Name: "测试处方",
        Indication: "测试适应症",
        StimulationType: stimulationType,
        CurrentMilliamp: 2,
        DeliveryMode: PrescriptionDeliveryModes.Continuous,
        TotalDurationMinutes: 20,
        IntervalMinutes: null,
        SessionDurationMinutes: null,
        Course: "测试疗程",
        RampUpSeconds: 30,
        RampDownSeconds: 30,
        EvidenceGrade: "测试证据",
        IsBuiltin: false);
}
