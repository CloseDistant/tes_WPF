namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.Globalization;

public sealed class PrescriptionEditorViewModel : ObservableObject
{
    public const int NameMaxLength = 50;
    public const int CourseMaxLength = 100;
    public const int EvidenceGradeMaxLength = 100;

    private string name;
    private string indication;
    private string stimulationType;
    private string currentMilliamp;
    private string deliveryMode;
    private string totalDurationValue;
    private string intervalValue;
    private string sessionDurationValue;
    private string course;
    private string rampUpValue;
    private string rampDownValue;
    private string evidenceGrade;
    private string errorMessage = string.Empty;

    public PrescriptionEditorViewModel(
        PrescriptionDefinition prescription,
        bool isNew,
        IEnumerable<string> availableStimulationTypes)
    {
        Original = prescription;
        IsNew = isNew;
        AvailableStimulationTypes = new ObservableCollection<string>(availableStimulationTypes);
        stimulationType = isNew
            ? string.Empty
            : AvailableStimulationTypes.Contains(prescription.StimulationType)
            ? prescription.StimulationType
            : string.Empty;
        name = prescription.Name;
        indication = prescription.Indication;
        currentMilliamp = isNew ? string.Empty : prescription.CurrentMilliamp.ToString("0.##", CultureInfo.InvariantCulture);
        deliveryMode = IsPulseCurrent
            ? PrescriptionDeliveryModes.Interval
            : isNew ? string.Empty : prescription.DeliveryMode;
        totalDurationValue = LoadTotalDuration(prescription, isNew);
        intervalValue = LoadIntervalValue(prescription, isNew);
        sessionDurationValue = LoadSessionDurationValue(prescription, isNew);
        course = prescription.Course;
        rampUpValue = LoadRampUpValue(prescription, isNew);
        rampDownValue = IsPulseCurrent ? string.Empty : isNew ? string.Empty : prescription.RampDownSeconds.ToString(CultureInfo.InvariantCulture);
        evidenceGrade = prescription.EvidenceGrade;
    }

    public PrescriptionDefinition Original { get; }
    public bool IsNew { get; }
    public string Title => IsNew ? "新增处方" : "编辑处方";
    public ObservableCollection<string> AvailableStimulationTypes { get; }
    public IReadOnlyList<string> DeliveryModes => PrescriptionDeliveryModes.All;
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Indication { get => indication; set => SetProperty(ref indication, value); }

    public string StimulationType
    {
        get => stimulationType;
        set
        {
            if (stimulationType == value)
            {
                return;
            }

            var wasPulseCurrent = IsPulseCurrent;
            SetProperty(ref stimulationType, value);
            var isPulseCurrent = IsPulseCurrent;
            if (wasPulseCurrent != isPulseCurrent)
            {
                ClearModeSpecificTimingFields();
            }

            if (isPulseCurrent)
            {
                DeliveryMode = PrescriptionDeliveryModes.Interval;
            }

            NotifyModePropertiesChanged();
        }
    }

    public string CurrentMilliamp { get => currentMilliamp; set => SetProperty(ref currentMilliamp, value); }

    public string DeliveryMode
    {
        get => deliveryMode;
        set
        {
            var next = IsPulseCurrent ? PrescriptionDeliveryModes.Interval : value;
            if (!SetProperty(ref deliveryMode, next))
            {
                return;
            }

            OnPropertyChanged(nameof(IsIntervalMode));
            OnPropertyChanged(nameof(IsContinuousMode));
            OnPropertyChanged(nameof(IntervalMinutesEntry));
            OnPropertyChanged(nameof(SessionDurationMinutesEntry));
        }
    }

    public bool IsPulseCurrent => StimulationType == PrescriptionDefinition.PulseCurrentStimulationType;
    public bool IsDeliveryModeEnabled => !IsPulseCurrent;
    public bool IsIntervalMode => IsPulseCurrent || DeliveryMode == PrescriptionDeliveryModes.Interval;
    public bool IsContinuousMode => !IsPulseCurrent && DeliveryMode == PrescriptionDeliveryModes.Continuous;
    public bool IsRampDownEnabled => !IsPulseCurrent;
    public string CurrentLabel => "幅值 (mA)";
    public string TotalDurationLabel => IsPulseCurrent ? "治疗时间 (s)" : "总时长 (min)";
    public string IntervalLabel => IsPulseCurrent ? "间隔宽度 (ms)" : "间隔时间 (min)";
    public string SessionDurationLabel => IsPulseCurrent ? "脉冲宽度 (ms)" : "单次时长 (min)";
    public string RampUpLabel => IsPulseCurrent ? "上升宽度 (ms)" : "渐升时间 (s)";
    public string RampDownLabel => IsPulseCurrent ? "渐降时间" : "渐降时间 (s)";
    public string TotalDurationMinutes { get => totalDurationValue; set => SetProperty(ref totalDurationValue, value); }

    public string IntervalMinutesEntry
    {
        get => IsContinuousMode ? "/" : intervalValue;
        set { if (IsIntervalMode) SetProperty(ref intervalValue, value, nameof(IntervalMinutesEntry)); }
    }

    public string SessionDurationMinutesEntry
    {
        get => IsContinuousMode ? "/" : sessionDurationValue;
        set { if (IsIntervalMode) SetProperty(ref sessionDurationValue, value, nameof(SessionDurationMinutesEntry)); }
    }

    public string Course { get => course; set => SetProperty(ref course, value); }
    public string RampUpSeconds { get => rampUpValue; set => SetProperty(ref rampUpValue, value); }

    public string RampDownSecondsEntry
    {
        get => IsPulseCurrent ? "/" : rampDownValue;
        set { if (!IsPulseCurrent) SetProperty(ref rampDownValue, value, nameof(RampDownSecondsEntry)); }
    }

    public string EvidenceGrade { get => evidenceGrade; set => SetProperty(ref evidenceGrade, value); }
    public string ErrorMessage { get => errorMessage; private set => SetProperty(ref errorMessage, value); }

    public bool TryBuild(out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Indication)
            || string.IsNullOrWhiteSpace(StimulationType) || string.IsNullOrWhiteSpace(DeliveryMode))
        {
            ErrorMessage = "请填写处方名称、适应症、刺激模式和模式。";
            return false;
        }

        if (ContainsControlCharacters(Name, Indication, Course, EvidenceGrade))
        {
            ErrorMessage = "处方名称、适应症、疗程和证据等级不能包含控制字符。";
            return false;
        }

        if (Name.Trim().Length > NameMaxLength)
        {
            ErrorMessage = $"处方名称不能超过 {NameMaxLength} 个字符。";
            return false;
        }

        if (Course.Trim().Length > CourseMaxLength)
        {
            ErrorMessage = $"疗程不能超过 {CourseMaxLength} 个字符。";
            return false;
        }

        if (EvidenceGrade.Trim().Length > EvidenceGradeMaxLength)
        {
            ErrorMessage = $"证据等级不能超过 {EvidenceGradeMaxLength} 个字符。";
            return false;
        }

        if (!TryParseDouble(CurrentMilliamp, out var current) || current <= 0)
        {
            ErrorMessage = "幅值请输入大于 0 的数字。";
            return false;
        }

        if (IsPulseCurrent)
        {
            return TryBuildPulseCurrent(current, out prescription);
        }

        return TryBuildMinuteBased(current, out prescription);
    }

    private bool TryBuildPulseCurrent(double current, out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (current > PulseCurrentParameters.MaxCurrentMilliamp)
        {
            ErrorMessage = "幅值必须大于 0 且不超过 15 mA。";
            return false;
        }

        if (!TryPositiveInt(TotalDurationMinutes, out var treatmentDurationSeconds))
        {
            ErrorMessage = "治疗时间必须为大于 0 的整数秒。";
            return false;
        }

        if (!TryPositiveInt(SessionDurationMinutesEntry, out var pulseWidthMilliseconds)
            || pulseWidthMilliseconds > PulseCurrentParameters.MaxPulseWidthMilliseconds)
        {
            ErrorMessage = "脉冲宽度必须大于 0 且不超过 1000 ms。";
            return false;
        }

        if (!TryNonNegativeInt(RampUpSeconds, out var riseWidthMilliseconds)
            || riseWidthMilliseconds > PulseCurrentParameters.MaxRiseWidthMilliseconds)
        {
            ErrorMessage = "上升宽度必须在 0–1000 ms 范围内。";
            return false;
        }

        if (!TryNonNegativeInt(IntervalMinutesEntry, out var intervalWidthMilliseconds)
            || intervalWidthMilliseconds > PulseCurrentParameters.MaxIntervalWidthMilliseconds)
        {
            ErrorMessage = "间隔宽度必须在 0–10000 ms 范围内。";
            return false;
        }

        var channel = new PulseCurrentChannelConfig
        {
            CurrentMilliamp = current.ToString("0.##", CultureInfo.InvariantCulture),
            PulseWidthMilliseconds = pulseWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
            RiseWidthMilliseconds = riseWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
            IntervalWidthMilliseconds = intervalWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
            TreatmentDurationSeconds = treatmentDurationSeconds.ToString(CultureInfo.InvariantCulture),
            Polarity = PulseCurrentPolarities.NotReversed
        };
        if (!PulseCurrentParameters.TryCreate(channel, out _, out var pulseError))
        {
            ErrorMessage = pulseError;
            return false;
        }

        prescription = Original with
        {
            Name = Name.Trim(),
            Indication = Indication.Trim(),
            StimulationType = StimulationType,
            CurrentMilliamp = current,
            DeliveryMode = PrescriptionDeliveryModes.Interval,
            TotalDurationMinutes = 0,
            IntervalMinutes = null,
            SessionDurationMinutes = null,
            Course = Course.Trim(),
            RampUpSeconds = 0,
            RampDownSeconds = 0,
            EvidenceGrade = EvidenceGrade.Trim(),
            ChannelPolarities = null,
            PulseTreatmentDurationSeconds = treatmentDurationSeconds,
            PulseWidthMilliseconds = pulseWidthMilliseconds,
            PulseRiseWidthMilliseconds = riseWidthMilliseconds,
            PulseIntervalWidthMilliseconds = intervalWidthMilliseconds
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private bool TryBuildMinuteBased(double current, out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (!TryPositiveInt(TotalDurationMinutes, out var totalDuration)
            || !TryNonNegativeInt(RampUpSeconds, out var rampUp)
            || !TryNonNegativeInt(RampDownSecondsEntry, out var rampDown))
        {
            ErrorMessage = "总时长必须大于 0，渐升和渐降时间必须为非负整数。";
            return false;
        }

        int? interval = null;
        int? sessionDuration = null;
        if (IsIntervalMode)
        {
            if (!TryPositiveInt(IntervalMinutesEntry, out var parsedInterval)
                || !TryPositiveInt(SessionDurationMinutesEntry, out var parsedSessionDuration))
            {
                ErrorMessage = "间隔模式下，间隔时间和单次时长必须填写大于 0 的整数。";
                return false;
            }

            if ((long)parsedSessionDuration * 60 < rampUp + rampDown)
            {
                ErrorMessage = "单次时长已包含渐升和渐降，不能小于渐升与渐降时间之和。";
                return false;
            }
            interval = parsedInterval;
            sessionDuration = parsedSessionDuration;
        }

        prescription = Original with
        {
            Name = Name.Trim(),
            Indication = Indication.Trim(),
            StimulationType = StimulationType,
            CurrentMilliamp = current,
            DeliveryMode = DeliveryMode,
            TotalDurationMinutes = totalDuration,
            IntervalMinutes = interval,
            SessionDurationMinutes = sessionDuration,
            Course = Course.Trim(),
            RampUpSeconds = rampUp,
            RampDownSeconds = rampDown,
            EvidenceGrade = EvidenceGrade.Trim(),
            ChannelPolarities = null,
            PulseTreatmentDurationSeconds = null,
            PulseWidthMilliseconds = null,
            PulseRiseWidthMilliseconds = null,
            PulseIntervalWidthMilliseconds = null
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private void ClearModeSpecificTimingFields()
    {
        TotalDurationMinutes = string.Empty;
        intervalValue = string.Empty;
        sessionDurationValue = string.Empty;
        rampUpValue = string.Empty;
        rampDownValue = string.Empty;
        OnPropertyChanged(nameof(IntervalMinutesEntry));
        OnPropertyChanged(nameof(SessionDurationMinutesEntry));
        OnPropertyChanged(nameof(RampUpSeconds));
        OnPropertyChanged(nameof(RampDownSecondsEntry));
    }

    private void NotifyModePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsPulseCurrent));
        OnPropertyChanged(nameof(IsDeliveryModeEnabled));
        OnPropertyChanged(nameof(IsIntervalMode));
        OnPropertyChanged(nameof(IsContinuousMode));
        OnPropertyChanged(nameof(IsRampDownEnabled));
        OnPropertyChanged(nameof(TotalDurationLabel));
        OnPropertyChanged(nameof(IntervalLabel));
        OnPropertyChanged(nameof(SessionDurationLabel));
        OnPropertyChanged(nameof(RampUpLabel));
        OnPropertyChanged(nameof(RampDownLabel));
        OnPropertyChanged(nameof(IntervalMinutesEntry));
        OnPropertyChanged(nameof(SessionDurationMinutesEntry));
        OnPropertyChanged(nameof(RampDownSecondsEntry));
    }

    private static string LoadTotalDuration(PrescriptionDefinition prescription, bool isNew)
    {
        if (isNew)
        {
            return string.Empty;
        }

        return prescription.IsPulseCurrent
            ? prescription.PulseTreatmentDurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            : prescription.TotalDurationMinutes.ToString(CultureInfo.InvariantCulture);
    }

    private static string LoadIntervalValue(PrescriptionDefinition prescription, bool isNew)
    {
        if (isNew)
        {
            return string.Empty;
        }

        return prescription.IsPulseCurrent
            ? prescription.PulseIntervalWidthMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            : prescription.IntervalMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string LoadSessionDurationValue(PrescriptionDefinition prescription, bool isNew)
    {
        if (isNew)
        {
            return string.Empty;
        }

        return prescription.IsPulseCurrent
            ? prescription.PulseWidthMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            : prescription.SessionDurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string LoadRampUpValue(PrescriptionDefinition prescription, bool isNew)
    {
        if (isNew)
        {
            return string.Empty;
        }

        return prescription.IsPulseCurrent
            ? prescription.PulseRiseWidthMilliseconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
            : prescription.RampUpSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
        || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool ContainsControlCharacters(params string[] values) =>
        values.Any(InputTextRules.ContainsControlCharacters);
    private static bool TryPositiveInt(string value, out int result) => int.TryParse(value, out result) && result > 0;
    private static bool TryNonNegativeInt(string value, out int result) => int.TryParse(value, out result) && result >= 0;
}
