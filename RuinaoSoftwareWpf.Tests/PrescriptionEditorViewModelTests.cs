using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class PrescriptionEditorViewModelTests
{
    [Fact]
    public void SwitchingToPulseCurrent_ForcesIntervalModeAndClearsTimingFields()
    {
        var editor = new PrescriptionEditorViewModel(CreateDirectCurrentPrescription(), false, ["tDCS", "tPCS"]);

        editor.StimulationType = PrescriptionDefinition.PulseCurrentStimulationType;

        Assert.True(editor.IsPulseCurrent);
        Assert.False(editor.IsDeliveryModeEnabled);
        Assert.True(editor.IsIntervalMode);
        Assert.False(editor.IsRampDownEnabled);
        Assert.Equal(PrescriptionDeliveryModes.Interval, editor.DeliveryMode);
        Assert.Equal("治疗时间 (s)", editor.TotalDurationLabel);
        Assert.Equal("间隔宽度 (ms)", editor.IntervalLabel);
        Assert.Equal("脉冲宽度 (ms)", editor.SessionDurationLabel);
        Assert.Equal("上升宽度 (ms)", editor.RampUpLabel);
        Assert.Equal("渐降时间", editor.RampDownLabel);
        Assert.Equal("/", editor.RampDownSecondsEntry);
        Assert.Equal(string.Empty, editor.TotalDurationMinutes);
        Assert.Equal(string.Empty, editor.IntervalMinutesEntry);
        Assert.Equal(string.Empty, editor.SessionDurationMinutesEntry);
        Assert.Equal(string.Empty, editor.RampUpSeconds);
    }

    [Fact]
    public void SwitchingFromPulseCurrentToDirectCurrent_ClearsPulseCurrentTimingFields()
    {
        var editor = new PrescriptionEditorViewModel(CreatePulseCurrentPrescription(), false, ["tDCS", "tPCS"]);

        editor.StimulationType = "tDCS";

        Assert.False(editor.IsPulseCurrent);
        Assert.True(editor.IsDeliveryModeEnabled);
        Assert.Equal(string.Empty, editor.TotalDurationMinutes);
        Assert.Equal(string.Empty, editor.IntervalMinutesEntry);
        Assert.Equal(string.Empty, editor.SessionDurationMinutesEntry);
        Assert.Equal(string.Empty, editor.RampUpSeconds);
        Assert.Equal(string.Empty, editor.RampDownSecondsEntry);
    }

    [Fact]
    public void TryBuildPulseCurrent_UsesSecondsAndIntegerMilliseconds()
    {
        var editor = new PrescriptionEditorViewModel(CreateEmptyPrescription(), true, ["tDCS", "tPCS"])
        {
            Name = "tPCS处方",
            Indication = "测试适应症",
            StimulationType = PrescriptionDefinition.PulseCurrentStimulationType,
            CurrentMilliamp = "2",
            TotalDurationMinutes = "1200",
            SessionDurationMinutesEntry = "10",
            RampUpSeconds = "5",
            IntervalMinutesEntry = "20",
            Course = "10次",
            EvidenceGrade = "A级"
        };

        var built = editor.TryBuild(out var prescription);

        Assert.True(built, editor.ErrorMessage);
        Assert.Equal(PrescriptionDefinition.PulseCurrentStimulationType, prescription.StimulationType);
        Assert.Equal(PrescriptionDeliveryModes.Interval, prescription.DeliveryMode);
        Assert.Equal(2, prescription.CurrentMilliamp);
        Assert.Equal(1200, prescription.PulseTreatmentDurationSeconds);
        Assert.Equal(10, prescription.PulseWidthMilliseconds);
        Assert.Equal(5, prescription.PulseRiseWidthMilliseconds);
        Assert.Equal(20, prescription.PulseIntervalWidthMilliseconds);
        Assert.Equal(0, prescription.TotalDurationMinutes);
        Assert.Null(prescription.IntervalMinutes);
        Assert.Null(prescription.SessionDurationMinutes);
        Assert.Equal(0, prescription.RampUpSeconds);
        Assert.Equal(0, prescription.RampDownSeconds);
    }

    [Fact]
    public void TryBuildPulseCurrent_RejectsDecimalMillisecondFields()
    {
        var editor = new PrescriptionEditorViewModel(CreateEmptyPrescription(), true, ["tPCS"])
        {
            Name = "tPCS处方",
            Indication = "测试适应症",
            StimulationType = PrescriptionDefinition.PulseCurrentStimulationType,
            CurrentMilliamp = "2",
            TotalDurationMinutes = "1200",
            SessionDurationMinutesEntry = "0.001",
            RampUpSeconds = "5",
            IntervalMinutesEntry = "20"
        };

        var built = editor.TryBuild(out _);

        Assert.False(built);
        Assert.Contains("脉冲宽度", editor.ErrorMessage);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Indication")]
    [InlineData("Course")]
    [InlineData("EvidenceGrade")]
    public void TryBuild_RejectsControlCharactersInTextFields(string fieldName)
    {
        var editor = CreateValidPulseCurrentEditor();
        var invalidValue = "有效内容\u0001";
        switch (fieldName)
        {
            case "Name":
                editor.Name = invalidValue;
                break;
            case "Indication":
                editor.Indication = invalidValue;
                break;
            case "Course":
                editor.Course = invalidValue;
                break;
            case "EvidenceGrade":
                editor.EvidenceGrade = invalidValue;
                break;
        }

        var built = editor.TryBuild(out _);

        Assert.False(built);
        Assert.Contains("控制字符", editor.ErrorMessage);
    }

    [Fact]
    public void BuildCsvPulseCurrent_UsesPulseCurrentLabelsAndUnits()
    {
        var csv = PrescriptionViewModel.BuildCsv(CreatePulseCurrentPrescription());

        Assert.StartsWith("参数,内容", csv);
        Assert.Contains("\"治疗时间\",\"1200 s\"", csv);
        Assert.Contains("\"脉冲宽度\",\"10 ms\"", csv);
        Assert.Contains("\"上升宽度\",\"5 ms\"", csv);
        Assert.Contains("\"间隔宽度\",\"20 ms\"", csv);
        Assert.Contains("\"渐降时间\",\"/\"", csv);
    }

    [Fact]
    public void PrescriptionDetails_SeparateRampUpAndRampDownByStimulationType()
    {
        var pulseCurrent = CreatePulseCurrentPrescription();
        var directCurrent = CreateDirectCurrentPrescription();

        Assert.Equal("上升宽度", pulseCurrent.RampUpLabel);
        Assert.Equal("5 ms", pulseCurrent.RampUpDisplay);
        Assert.Equal("渐降时间", pulseCurrent.RampDownLabel);
        Assert.Equal("/", pulseCurrent.RampDownDisplay);
        Assert.Equal("渐升时间", directCurrent.RampUpLabel);
        Assert.Equal("30 s", directCurrent.RampUpDisplay);
        Assert.Equal("渐降时间", directCurrent.RampDownLabel);
        Assert.Equal("30 s", directCurrent.RampDownDisplay);
    }

    [Theory]
    [InlineData("tDCS")]
    [InlineData(PrescriptionDefinition.PulseCurrentStimulationType)]
    public void TryBuild_DoesNotCarryPolarityIntoSavedPrescription(string stimulationType)
    {
        var source = (stimulationType == PrescriptionDefinition.PulseCurrentStimulationType
            ? CreatePulseCurrentPrescription()
            : CreateDirectCurrentPrescription()) with
        {
            ChannelPolarities = ["调转", "不掉转"]
        };
        var editor = new PrescriptionEditorViewModel(source, false, ["tDCS", "tPCS"]);

        var built = editor.TryBuild(out var prescription);

        Assert.True(built, editor.ErrorMessage);
        Assert.Null(prescription.ChannelPolarities);
    }

    private static PrescriptionDefinition CreateEmptyPrescription() => new(
        Id: "draft",
        Name: string.Empty,
        Indication: string.Empty,
        StimulationType: string.Empty,
        CurrentMilliamp: 0,
        DeliveryMode: string.Empty,
        TotalDurationMinutes: 0,
        IntervalMinutes: null,
        SessionDurationMinutes: null,
        Course: string.Empty,
        RampUpSeconds: 0,
        RampDownSeconds: 0,
        EvidenceGrade: string.Empty,
        IsBuiltin: false);

    private static PrescriptionEditorViewModel CreateValidPulseCurrentEditor() => new(
        CreateEmptyPrescription(),
        true,
        [PrescriptionDefinition.PulseCurrentStimulationType])
    {
        Name = "tPCS处方",
        Indication = "测试适应症",
        StimulationType = PrescriptionDefinition.PulseCurrentStimulationType,
        CurrentMilliamp = "2",
        TotalDurationMinutes = "1200",
        SessionDurationMinutesEntry = "10",
        RampUpSeconds = "5",
        IntervalMinutesEntry = "20",
        Course = "10次",
        EvidenceGrade = "A级"
    };

    private static PrescriptionDefinition CreateDirectCurrentPrescription() => new(
        Id: "tdcs",
        Name: "protocol1",
        Indication: "默认直流电刺激",
        StimulationType: "tDCS",
        CurrentMilliamp: 2,
        DeliveryMode: PrescriptionDeliveryModes.Continuous,
        TotalDurationMinutes: 20,
        IntervalMinutes: null,
        SessionDurationMinutes: null,
        Course: "10次",
        RampUpSeconds: 30,
        RampDownSeconds: 30,
        EvidenceGrade: "内置默认处方",
        IsBuiltin: true);

    private static PrescriptionDefinition CreatePulseCurrentPrescription() => new(
        Id: "tpcs",
        Name: "pulse",
        Indication: "tPCS测试",
        StimulationType: PrescriptionDefinition.PulseCurrentStimulationType,
        CurrentMilliamp: 2,
        DeliveryMode: PrescriptionDeliveryModes.Interval,
        TotalDurationMinutes: 0,
        IntervalMinutes: null,
        SessionDurationMinutes: null,
        Course: "10次",
        RampUpSeconds: 0,
        RampDownSeconds: 0,
        EvidenceGrade: "A级",
        IsBuiltin: false,
        PulseTreatmentDurationSeconds: 1200,
        PulseWidthMilliseconds: 10,
        PulseRiseWidthMilliseconds: 5,
        PulseIntervalWidthMilliseconds: 20);
}
