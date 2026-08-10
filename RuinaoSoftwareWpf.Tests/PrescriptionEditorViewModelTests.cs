using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class PrescriptionEditorViewModelTests
{
    [Fact]
    public void EditingDirectCurrent_DoesNotAllowChangingStimulationType()
    {
        var editor = new PrescriptionEditorViewModel(CreateDirectCurrentPrescription(), false, ["tDCS", "tPCS"]);

        editor.StimulationType = PrescriptionDefinition.PulseCurrentStimulationType;

        Assert.False(editor.IsPulseCurrent);
        Assert.True(editor.IsDirectCurrent);
        Assert.Equal("tDCS", editor.StimulationType);
        Assert.False(editor.IsModeSelectionStep);
    }

    [Fact]
    public void EditingPulseCurrent_DoesNotAllowChangingStimulationType()
    {
        var editor = new PrescriptionEditorViewModel(CreatePulseCurrentPrescription(), false, ["tDCS", "tPCS"]);

        editor.StimulationType = "tDCS";

        Assert.True(editor.IsPulseCurrent);
        Assert.False(editor.IsDirectCurrent);
        Assert.Equal(PrescriptionDefinition.PulseCurrentStimulationType, editor.StimulationType);
        Assert.False(editor.IsModeSelectionStep);
    }

    [Fact]
    public void NewPrescription_SelectsOnlyVisibleModesAndLocksChoiceAfterNextStep()
    {
        var editor = new PrescriptionEditorViewModel(
            CreateEmptyPrescription(),
            true,
            ["tDCS", PrescriptionDefinition.PulseCurrentStimulationType]);

        Assert.True(editor.IsModeSelectionStep);
        Assert.Equal(["tDCS", PrescriptionDefinition.PulseCurrentStimulationType],
            editor.AvailableModeChoices.Select(item => item.ShortName));
        Assert.Equal("tDCS", editor.StimulationType);
        Assert.Equal(412, editor.DialogHeight);
        Assert.True(editor.ContinueToEditor());

        editor.StimulationType = PrescriptionDefinition.PulseCurrentStimulationType;

        Assert.True(editor.IsEditorStep);
        Assert.Equal(650, editor.DialogHeight);
        Assert.Equal("tDCS", editor.StimulationType);
        Assert.Equal(DirectCurrentParameterRules.DefaultCurrentMilliamp, editor.CurrentMilliamp);
        Assert.Equal(DirectCurrentParameterRules.DefaultTotalDurationSeconds, editor.TotalDurationMinutes);
    }

    [Fact]
    public void TryBuildDirectCurrent_PersistsExactSecondValuesWithoutPolarity()
    {
        var editor = new PrescriptionEditorViewModel(CreateEmptyPrescription(), true, ["tDCS"]);
        Assert.True(editor.ContinueToEditor());
        editor.Name = "直流测试处方";
        editor.Indication = "测试";
        editor.CurrentMilliamp = "1.23";
        editor.RampUpSeconds = "0.4";
        editor.RampDownSecondsEntry = "0.6";
        editor.TotalDurationMinutes = "1234.5";
        editor.IntervalMinutesEntry = "2.5";
        editor.SessionDurationMinutesEntry = "60.1";

        var built = editor.TryBuild(out var prescription);

        Assert.True(built, editor.ErrorMessage);
        Assert.Equal(1.23, prescription.CurrentMilliamp);
        Assert.Equal(1234.5, prescription.DirectCurrentTotalDurationSeconds);
        Assert.Equal(2.5, prescription.DirectCurrentIntervalDurationSeconds);
        Assert.Equal(60.1, prescription.DirectCurrentSingleDurationSeconds);
        Assert.Equal(0.4, prescription.DirectCurrentRampUpDurationSeconds);
        Assert.Equal(0.6, prescription.DirectCurrentRampDownDurationSeconds);
        Assert.Null(prescription.ChannelPolarities);
    }

    [Fact]
    public void TryBuildDirectCurrent_RejectsSingleDurationEqualToRampSum()
    {
        var editor = new PrescriptionEditorViewModel(CreateEmptyPrescription(), true, ["tDCS"]);
        Assert.True(editor.ContinueToEditor());
        editor.Name = "直流测试处方";
        editor.Indication = "测试";
        editor.RampUpSeconds = "0.5";
        editor.RampDownSecondsEntry = "0.5";
        editor.SessionDurationMinutesEntry = "1.0";

        var built = editor.TryBuild(out _);

        Assert.False(built);
        Assert.Contains("必须大于", editor.ErrorMessage);
    }

    [Fact]
    public void TryBuildMonophasicPulseCurrent_PersistsIndependentTypeAndDerivedFields()
    {
        var editor = new PrescriptionEditorViewModel(
            CreateEmptyPrescription(),
            true,
            [StimulationModeCodes.MonophasicPulseCurrent]);
        Assert.True(editor.ContinueToEditor());
        editor.Name = "单相脉冲处方";
        editor.Indication = "测试";
        editor.CurrentMilliamp = "12.34";
        editor.RampUpSeconds = "1.2";
        editor.IntervalMinutesEntry = "0.0";
        editor.TotalDurationMinutes = "120.0";

        var built = editor.TryBuild(out var prescription);

        Assert.True(built, editor.ErrorMessage);
        Assert.Equal(StimulationModeCodes.MonophasicPulseCurrent, prescription.StimulationType);
        Assert.Equal(PrescriptionDeliveryModes.Interval, prescription.DeliveryMode);
        Assert.Equal(12.34, prescription.CurrentMilliamp);
        Assert.Equal(1.2, prescription.DirectCurrentRampUpDurationSeconds);
        Assert.Equal(1.2, prescription.DirectCurrentRampDownDurationSeconds);
        Assert.Equal(2.4, prescription.DirectCurrentSingleDurationSeconds);
        Assert.Equal(0, prescription.DirectCurrentIntervalDurationSeconds);
        Assert.Null(prescription.ChannelPolarities);
        Assert.False(editor.ShowDeliveryMode);
        Assert.False(editor.ShowSingleDuration);
        Assert.False(editor.IsRampDownEnabled);

        var csv = PrescriptionViewModel.BuildCsv(prescription);
        Assert.Contains("\"刺激时间\",\"120.0 s\"", csv);
        Assert.Contains("\"渐升时间（渐降同值）\",\"1.2 s\"", csv);
        Assert.DoesNotContain("\"模式\",", csv);
        Assert.DoesNotContain("单次时长", csv);
        Assert.DoesNotContain("渐降时间", csv);
    }

    [Fact]
    public void TryBuildMonophasicPulseCurrent_RejectsIncompleteTriangle()
    {
        var editor = new PrescriptionEditorViewModel(
            CreateEmptyPrescription(),
            true,
            [StimulationModeCodes.MonophasicPulseCurrent]);
        Assert.True(editor.ContinueToEditor());
        editor.Name = "单相脉冲处方";
        editor.Indication = "测试";
        editor.RampUpSeconds = "1.0";
        editor.TotalDurationMinutes = "1.9";

        Assert.False(editor.TryBuild(out _));
        Assert.Contains("2×渐升时间", editor.ErrorMessage);
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
            TotalDurationMinutes = "1200.5",
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
        Assert.Equal(1201, prescription.PulseTreatmentDurationSeconds);
        Assert.Equal(1200.5, prescription.PulseTreatmentDurationSecondsValue);
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

    [Fact]
    public void TryBuildPulseCurrent_RejectsZeroIntervalWidth()
    {
        var editor = new PrescriptionEditorViewModel(CreateEmptyPrescription(), true, ["tPCS"])
        {
            Name = "tPCS处方",
            Indication = "测试适应症",
            StimulationType = PrescriptionDefinition.PulseCurrentStimulationType,
            CurrentMilliamp = "2",
            TotalDurationMinutes = "1200.0",
            SessionDurationMinutesEntry = "10",
            RampUpSeconds = "5",
            IntervalMinutesEntry = "0"
        };

        var built = editor.TryBuild(out _);

        Assert.False(built);
        Assert.Contains("间隔宽度", editor.ErrorMessage);
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
        Assert.Contains("\"治疗时间\",\"1200.0 s\"", csv);
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
        Assert.Equal("30.0 s", directCurrent.RampUpDisplay);
        Assert.Equal("渐降时间", directCurrent.RampDownLabel);
        Assert.Equal("30.0 s", directCurrent.RampDownDisplay);
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
