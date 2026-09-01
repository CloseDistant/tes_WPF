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
    private string carrierFrequencyValue;
    private string evidenceGrade;
    private string errorMessage = string.Empty;
    private bool isModeSelectionStep;

    public PrescriptionEditorViewModel(
        PrescriptionDefinition prescription,
        bool isNew,
        IEnumerable<string> availableStimulationTypes)
    {
        Original = prescription;
        IsNew = isNew;
        AvailableStimulationTypes = new ObservableCollection<string>(availableStimulationTypes);
        AvailableModeChoices = new ObservableCollection<PrescriptionStimulationModeChoice>(
            AvailableStimulationTypes.Select(PrescriptionStimulationModeChoice.Create));
        isModeSelectionStep = isNew;
        stimulationType = isNew
            ? AvailableStimulationTypes.FirstOrDefault() ?? string.Empty
            : AvailableStimulationTypes.Contains(prescription.StimulationType)
            ? prescription.StimulationType
            : string.Empty;
        name = prescription.Name;
        indication = prescription.Indication;
        currentMilliamp = isNew
            ? string.Empty
            : prescription.CurrentMilliamp.ToString(
                IsTemporalInterference || IsTacs ? "0.000" : "0.##",
                CultureInfo.InvariantCulture);
        deliveryMode = IsTemporalInterference || IsTacs
            ? PrescriptionDeliveryModes.Continuous
            : IsPulseCurrent || IsMonophasicPulseCurrent
                ? PrescriptionDeliveryModes.Interval
            : isNew ? string.Empty : prescription.DeliveryMode;
        totalDurationValue = LoadTotalDuration(prescription, isNew);
        intervalValue = LoadIntervalValue(prescription, isNew);
        sessionDurationValue = LoadSessionDurationValue(prescription, isNew);
        course = prescription.Course;
        rampUpValue = LoadRampUpValue(prescription, isNew);
        rampDownValue = LoadRampDownValue(prescription, isNew);
        carrierFrequencyValue = isNew
            ? string.Empty
            : prescription.IsTacs
                ? prescription.TacsFrequencyHz.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        evidenceGrade = prescription.EvidenceGrade;
    }

    public PrescriptionDefinition Original { get; }
    public bool IsNew { get; }
    public string Title => IsNew ? "新增处方" : "编辑处方";
    public string DialogTitle => IsModeSelectionStep ? "选择刺激模式" : Title;
    public double DialogHeight => IsModeSelectionStep
        ? Math.Clamp(228 + (AvailableModeChoices.Count * 92), 388, 570)
        : 650;
    public ObservableCollection<string> AvailableStimulationTypes { get; }
    public ObservableCollection<PrescriptionStimulationModeChoice> AvailableModeChoices { get; }
    public bool IsModeSelectionStep
    {
        get => isModeSelectionStep;
        private set
        {
            if (SetProperty(ref isModeSelectionStep, value))
            {
                OnPropertyChanged(nameof(IsEditorStep));
                OnPropertyChanged(nameof(DialogTitle));
                OnPropertyChanged(nameof(DialogHeight));
            }
        }
    }
    public bool IsEditorStep => !IsModeSelectionStep;
    public IReadOnlyList<string> DeliveryModes => PrescriptionDeliveryModes.All;
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Indication { get => indication; set => SetProperty(ref indication, value); }

    public string StimulationType
    {
        get => stimulationType;
        set
        {
            if (!IsModeSelectionStep)
            {
                return;
            }

            if (stimulationType == value)
            {
                return;
            }

            var wasPulseCurrent = IsPulseCurrent;
            var wasMonophasic = IsMonophasicPulseCurrent;
            var wasTemporalInterference = IsTemporalInterference;
            var wasTacs = IsTacs;
            SetProperty(ref stimulationType, value);
            var isPulseCurrent = IsPulseCurrent;
            if (wasPulseCurrent != isPulseCurrent
                || wasMonophasic != IsMonophasicPulseCurrent
                || wasTemporalInterference != IsTemporalInterference
                || wasTacs != IsTacs)
            {
                ClearModeSpecificTimingFields();
            }

            if (IsTemporalInterference || IsTacs)
            {
                DeliveryMode = PrescriptionDeliveryModes.Continuous;
            }
            else if (isPulseCurrent || IsMonophasicPulseCurrent)
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
            var next = IsTemporalInterference || IsTacs
                ? PrescriptionDeliveryModes.Continuous
                : IsPulseCurrent || IsMonophasicPulseCurrent
                    ? PrescriptionDeliveryModes.Interval
                : value;
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
    public bool IsDirectCurrent => StimulationType == StimulationModeCodes.DirectCurrent;
    public bool IsTemporalInterference => StimulationType == StimulationModeCodes.TemporalInterference;
    public bool IsTacs => StimulationType == StimulationModeCodes.AlternatingCurrent;
    public bool IsMonophasicPulseCurrent => StimulationType == StimulationModeCodes.MonophasicPulseCurrent;
    public bool IsDeliveryModeEnabled => !IsPulseCurrent && !IsMonophasicPulseCurrent && !IsTemporalInterference && !IsTacs;
    public bool IsIntervalMode => !IsTemporalInterference && !IsTacs
        && (IsPulseCurrent || IsMonophasicPulseCurrent || DeliveryMode == PrescriptionDeliveryModes.Interval);
    public bool IsContinuousMode => !IsPulseCurrent && !IsMonophasicPulseCurrent && DeliveryMode == PrescriptionDeliveryModes.Continuous;
    public bool IsRampDownEnabled => !IsPulseCurrent && !IsMonophasicPulseCurrent;
    public bool ShowDeliveryMode => !IsMonophasicPulseCurrent;
    public bool ShowInterval => true;
    public bool ShowSingleDuration => !IsMonophasicPulseCurrent;
    public string DeliveryModeRowHeight => ShowDeliveryMode ? "39" : "0";
    public string IntervalRowHeight => ShowInterval ? "39" : "0";
    public string SingleDurationRowHeight => ShowSingleDuration ? "39" : "0";
    public string RampDownRowHeight => IsRampDownEnabled ? "39" : "0";
    public string FrequencyRowHeight => IsTacs ? "39" : "0";
    public string CurrentLabel => "幅值 (mA)";
    public string TotalDurationLabel => IsPulseCurrent
        ? "治疗时间 (s)"
        : IsDirectCurrent || IsMonophasicPulseCurrent || IsTemporalInterference || IsTacs
            ? "刺激时间 (s)"
            : "总时长 (min)";
    public string IntervalLabel => IsPulseCurrent
        ? "间隔宽度 (ms)"
        : IsDirectCurrent || IsMonophasicPulseCurrent || IsTemporalInterference || IsTacs
            ? "间隔时间 (s)"
            : "间隔时间 (min)";
    public string SessionDurationLabel => IsPulseCurrent
        ? "脉冲宽度 (ms)"
        : IsDirectCurrent || IsTemporalInterference || IsTacs ? "单次时长 (s)" : "单次时长 (min)";
    public string RampUpLabel => IsPulseCurrent
        ? "上升宽度 (ms)"
        : IsMonophasicPulseCurrent ? "渐升时间（渐降同值）(s)" : "渐升时间 (s)";
    public string RampDownLabel => IsPulseCurrent ? "渐降时间" : "渐降时间 (s)";
    public string TotalDurationMinutes { get => totalDurationValue; set => SetProperty(ref totalDurationValue, value); }

    public string IntervalMinutesEntry
    {
        get => IsTemporalInterference || IsTacs ? "-" : IsContinuousMode ? "/" : intervalValue;
        set { if (IsIntervalMode) SetProperty(ref intervalValue, value, nameof(IntervalMinutesEntry)); }
    }

    public string SessionDurationMinutesEntry
    {
        get => IsTemporalInterference || IsTacs ? "-" : IsContinuousMode ? "/" : sessionDurationValue;
        set { if (IsIntervalMode) SetProperty(ref sessionDurationValue, value, nameof(SessionDurationMinutesEntry)); }
    }

    public string Course { get => course; set => SetProperty(ref course, value); }
    public string RampUpSeconds { get => rampUpValue; set => SetProperty(ref rampUpValue, value); }

    public string RampDownSecondsEntry
    {
        get => IsPulseCurrent ? "/" : rampDownValue;
        set { if (!IsPulseCurrent) SetProperty(ref rampDownValue, value, nameof(RampDownSecondsEntry)); }
    }

    public string CarrierFrequencyHz
    {
        get => carrierFrequencyValue;
        set => SetProperty(ref carrierFrequencyValue, value);
    }

    public string EvidenceGrade { get => evidenceGrade; set => SetProperty(ref evidenceGrade, value); }
    public string ErrorMessage { get => errorMessage; private set => SetProperty(ref errorMessage, value); }

    public bool SelectStimulationType(string stimulationType)
    {
        if (!IsModeSelectionStep || !AvailableStimulationTypes.Contains(stimulationType))
        {
            return false;
        }

        StimulationType = stimulationType;
        return true;
    }

    public bool ContinueToEditor()
    {
        if (!IsModeSelectionStep || string.IsNullOrWhiteSpace(StimulationType))
        {
            ErrorMessage = "请选择刺激模式。";
            return false;
        }

        ApplyNewPrescriptionDefaults();
        ErrorMessage = string.Empty;
        IsModeSelectionStep = false;
        return true;
    }

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

        if (IsDirectCurrent)
        {
            return TryBuildDirectCurrent(current, out prescription);
        }

        if (IsTemporalInterference)
        {
            return TryBuildTemporalInterference(out prescription);
        }

        if (IsTacs)
        {
            return TryBuildTacs(out prescription);
        }

        if (IsMonophasicPulseCurrent)
        {
            return TryBuildMonophasicPulseCurrent(out prescription);
        }

        return TryBuildMinuteBased(current, out prescription);
    }

    public DirectCurrentParameterNormalization NormalizeDirectCurrentEntry(
        DirectCurrentParameterKind kind,
        string text,
        string fallbackValue) =>
        DirectCurrentParameterRules.Normalize(kind, text, fallbackValue);

    public PulseCurrentParameterNormalization NormalizePulseCurrentEntry(
        PulseCurrentParameterKind kind,
        string text,
        string fallbackValue) =>
        PulseCurrentParameterRules.Normalize(kind, text, fallbackValue);

    public MonophasicPulseCurrentParameterNormalization NormalizeMonophasicPulseCurrentEntry(
        MonophasicPulseCurrentParameterKind kind,
        string text,
        string fallbackValue) =>
        MonophasicPulseCurrentParameterRules.Normalize(kind, text, fallbackValue);

    public TiAlternatingCurrentParameterNormalization NormalizeTemporalInterferenceEntry(
        TiAlternatingCurrentParameterKind kind,
        string text,
        string fallbackValue) =>
        TiAlternatingCurrentParameterRules.Normalize(kind, text, fallbackValue);

    public TacsParameterNormalization NormalizeTacsEntry(
        TacsParameterKind kind,
        string text,
        string fallbackValue) =>
        TacsParameterRules.Normalize(kind, text, fallbackValue);

    public void ReportInputError(string message) => ErrorMessage = message;

    private bool TryBuildPulseCurrent(double current, out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (!TryPulseCurrentParameter(
                PulseCurrentParameterKind.CurrentMilliamp,
                CurrentMilliamp,
                out current))
        {
            return false;
        }

        if (!TryPulseCurrentParameter(
                PulseCurrentParameterKind.TreatmentDurationSeconds,
                TotalDurationMinutes,
                out var treatmentDurationSeconds))
        {
            return false;
        }

        if (!TryPulseCurrentIntegerParameter(
                PulseCurrentParameterKind.PulseWidthMilliseconds,
                SessionDurationMinutesEntry,
                out var pulseWidthMilliseconds))
        {
            return false;
        }

        if (!TryPulseCurrentIntegerParameter(
                PulseCurrentParameterKind.RiseWidthMilliseconds,
                RampUpSeconds,
                out var riseWidthMilliseconds))
        {
            return false;
        }

        if (!TryPulseCurrentIntegerParameter(
                PulseCurrentParameterKind.IntervalWidthMilliseconds,
                IntervalMinutesEntry,
                out var intervalWidthMilliseconds))
        {
            return false;
        }

        var channel = new PulseCurrentChannelConfig
        {
            CurrentMilliamp = PulseCurrentParameterRules.FormatCurrent(current),
            PulseWidthMilliseconds = pulseWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
            RiseWidthMilliseconds = riseWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
            IntervalWidthMilliseconds = intervalWidthMilliseconds.ToString(CultureInfo.InvariantCulture),
            TreatmentDurationSeconds = PulseCurrentParameterRules.FormatTreatmentDuration(
                treatmentDurationSeconds),
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
            // 旧整数字段用于兼容旧版本；新版本读取精确秒字段。
            PulseTreatmentDurationSeconds = (int)Math.Ceiling(treatmentDurationSeconds),
            PulseTreatmentDurationSecondsValue = treatmentDurationSeconds,
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
            PulseTreatmentDurationSecondsValue = null,
            PulseWidthMilliseconds = null,
            PulseRiseWidthMilliseconds = null,
            PulseIntervalWidthMilliseconds = null
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private bool TryBuildDirectCurrent(double current, out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (!TryDirectCurrentParameter(
                DirectCurrentParameterKind.CurrentMilliamp,
                CurrentMilliamp,
                out current))
        {
            return false;
        }

        if (!TryDirectCurrentParameter(
                DirectCurrentParameterKind.TotalDurationSeconds,
                TotalDurationMinutes,
                out var totalDuration)
            || !TryDirectCurrentParameter(
                DirectCurrentParameterKind.RampUpSeconds,
                RampUpSeconds,
                out var rampUp)
            || !TryDirectCurrentParameter(
                DirectCurrentParameterKind.RampDownSeconds,
                RampDownSecondsEntry,
                out var rampDown))
        {
            return false;
        }

        double? interval = null;
        double? singleDuration = null;
        if (totalDuration < rampUp + rampDown)
        {
            ErrorMessage = "刺激时间不能小于渐升时间与渐降时间之和。";
            return false;
        }

        if (IsIntervalMode)
        {
            if (!TryDirectCurrentParameter(
                    DirectCurrentParameterKind.IntervalSeconds,
                    IntervalMinutesEntry,
                    out var parsedInterval)
                || !TryDirectCurrentParameter(
                    DirectCurrentParameterKind.SingleDurationSeconds,
                    SessionDurationMinutesEntry,
                    out var parsedSingleDuration))
            {
                return false;
            }

            if (parsedSingleDuration <= rampUp + rampDown)
            {
                ErrorMessage = "单次时长必须大于渐升时间与渐降时间之和。";
                return false;
            }

            interval = parsedInterval;
            singleDuration = parsedSingleDuration;
        }
        prescription = Original with
        {
            Name = Name.Trim(),
            Indication = Indication.Trim(),
            StimulationType = StimulationType,
            CurrentMilliamp = current,
            DeliveryMode = DeliveryMode,
            // 旧分钟字段仅用于老版本读取；新版本始终使用下方的秒制字段。
            TotalDurationMinutes = Math.Max(1, (int)Math.Ceiling(totalDuration / 60d)),
            IntervalMinutes = interval.HasValue ? (int)Math.Ceiling(interval.Value / 60d) : null,
            SessionDurationMinutes = singleDuration.HasValue
                ? Math.Max(1, (int)Math.Ceiling(singleDuration.Value / 60d))
                : null,
            Course = Course.Trim(),
            RampUpSeconds = (int)Math.Round(rampUp, MidpointRounding.AwayFromZero),
            RampDownSeconds = (int)Math.Round(rampDown, MidpointRounding.AwayFromZero),
            EvidenceGrade = EvidenceGrade.Trim(),
            ChannelPolarities = null,
            PulseTreatmentDurationSeconds = null,
            PulseTreatmentDurationSecondsValue = null,
            PulseWidthMilliseconds = null,
            PulseRiseWidthMilliseconds = null,
            PulseIntervalWidthMilliseconds = null,
            DirectCurrentTotalDurationSecondsValue = totalDuration,
            DirectCurrentIntervalSecondsValue = interval,
            DirectCurrentSingleDurationSecondsValue = singleDuration,
            DirectCurrentRampUpSecondsValue = rampUp,
            DirectCurrentRampDownSecondsValue = rampDown
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private bool TryBuildTemporalInterference(out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (!TryTemporalInterferenceParameter(
                TiAlternatingCurrentParameterKind.PeakCurrentMilliampere,
                CurrentMilliamp,
                out var current)
            || !TryTemporalInterferenceParameter(
                TiAlternatingCurrentParameterKind.TotalDurationSeconds,
                TotalDurationMinutes,
                out var totalDuration)
            || !TryTemporalInterferenceParameter(
                TiAlternatingCurrentParameterKind.RampUpSeconds,
                RampUpSeconds,
                out var rampUp)
            || !TryTemporalInterferenceParameter(
                TiAlternatingCurrentParameterKind.RampDownSeconds,
                RampDownSecondsEntry,
                out var rampDown))
        {
            return false;
        }

        if (rampUp + rampDown > totalDuration)
        {
            ErrorMessage = "刺激时间不能小于渐升时间与渐降时间之和。";
            return false;
        }

        prescription = Original with
        {
            Name = Name.Trim(),
            Indication = Indication.Trim(),
            StimulationType = StimulationModeCodes.TemporalInterference,
            CurrentMilliamp = decimal.ToDouble(current),
            DeliveryMode = PrescriptionDeliveryModes.Continuous,
            TotalDurationMinutes = Math.Max(1, (int)Math.Ceiling(totalDuration / 60m)),
            IntervalMinutes = null,
            SessionDurationMinutes = null,
            Course = Course.Trim(),
            RampUpSeconds = decimal.ToInt32(decimal.Round(rampUp, 0, MidpointRounding.AwayFromZero)),
            RampDownSeconds = decimal.ToInt32(decimal.Round(rampDown, 0, MidpointRounding.AwayFromZero)),
            EvidenceGrade = EvidenceGrade.Trim(),
            ChannelPolarities = null,
            PulseTreatmentDurationSeconds = null,
            PulseTreatmentDurationSecondsValue = null,
            PulseWidthMilliseconds = null,
            PulseRiseWidthMilliseconds = null,
            PulseIntervalWidthMilliseconds = null,
            DirectCurrentTotalDurationSecondsValue = decimal.ToDouble(totalDuration),
            DirectCurrentIntervalSecondsValue = null,
            DirectCurrentSingleDurationSecondsValue = null,
            DirectCurrentRampUpSecondsValue = decimal.ToDouble(rampUp),
            DirectCurrentRampDownSecondsValue = decimal.ToDouble(rampDown)
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private bool TryBuildTacs(out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (!TryTacsParameter(TacsParameterKind.PeakCurrentMilliampere, CurrentMilliamp, out var current)
            || !TryTacsParameter(TacsParameterKind.TotalDurationSeconds, TotalDurationMinutes, out var totalDuration)
            || !TryTacsParameter(TacsParameterKind.RampUpSeconds, RampUpSeconds, out var rampUp)
            || !TryTacsParameter(TacsParameterKind.RampDownSeconds, RampDownSecondsEntry, out var rampDown)
            || !TryTacsParameter(TacsParameterKind.FrequencyHz, CarrierFrequencyHz, out var frequency))
        {
            return false;
        }

        if (rampUp + rampDown > totalDuration)
        {
            ErrorMessage = "刺激时间不能小于渐升时间与渐降时间之和。";
            return false;
        }

        prescription = Original with
        {
            Name = Name.Trim(),
            Indication = Indication.Trim(),
            StimulationType = StimulationModeCodes.AlternatingCurrent,
            CurrentMilliamp = decimal.ToDouble(current),
            DeliveryMode = PrescriptionDeliveryModes.Continuous,
            TotalDurationMinutes = Math.Max(1, (int)Math.Ceiling(totalDuration / 60m)),
            IntervalMinutes = null,
            SessionDurationMinutes = null,
            Course = Course.Trim(),
            RampUpSeconds = decimal.ToInt32(decimal.Round(rampUp, 0, MidpointRounding.AwayFromZero)),
            RampDownSeconds = decimal.ToInt32(decimal.Round(rampDown, 0, MidpointRounding.AwayFromZero)),
            EvidenceGrade = EvidenceGrade.Trim(),
            ChannelPolarities = null,
            TacsPeakCurrentMilliampereValue = decimal.ToDouble(current),
            TacsRampUpSecondsValue = decimal.ToDouble(rampUp),
            TacsRampDownSecondsValue = decimal.ToDouble(rampDown),
            TacsFrequencyHzValue = decimal.ToInt32(frequency),
            TacsTotalDurationSecondsValue = decimal.ToDouble(totalDuration),
            TacsParameterVersion = 1,
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private bool TryBuildMonophasicPulseCurrent(out PrescriptionDefinition prescription)
    {
        prescription = Original;
        if (!TryMonophasicPulseCurrentParameter(
                MonophasicPulseCurrentParameterKind.CurrentMilliamp,
                CurrentMilliamp,
                out var current)
            || !TryMonophasicPulseCurrentParameter(
                MonophasicPulseCurrentParameterKind.RampSeconds,
                RampUpSeconds,
                out var ramp)
            || !TryMonophasicPulseCurrentParameter(
                MonophasicPulseCurrentParameterKind.IntervalSeconds,
                IntervalMinutesEntry,
                out var interval)
            || !TryMonophasicPulseCurrentParameter(
                MonophasicPulseCurrentParameterKind.TotalDurationSeconds,
                TotalDurationMinutes,
                out var totalDuration))
        {
            return false;
        }

        if (totalDuration < ramp * 2d)
        {
            ErrorMessage = "刺激时间不能小于一个完整三角脉冲时长（2×渐升时间）。";
            return false;
        }

        prescription = Original with
        {
            Name = Name.Trim(),
            Indication = Indication.Trim(),
            StimulationType = StimulationModeCodes.MonophasicPulseCurrent,
            CurrentMilliamp = current,
            DeliveryMode = PrescriptionDeliveryModes.Interval,
            TotalDurationMinutes = Math.Max(1, (int)Math.Ceiling(totalDuration / 60d)),
            IntervalMinutes = interval <= 0 ? null : Math.Max(1, (int)Math.Ceiling(interval / 60d)),
            SessionDurationMinutes = null,
            Course = Course.Trim(),
            RampUpSeconds = (int)Math.Round(ramp, MidpointRounding.AwayFromZero),
            RampDownSeconds = (int)Math.Round(ramp, MidpointRounding.AwayFromZero),
            EvidenceGrade = EvidenceGrade.Trim(),
            ChannelPolarities = null,
            PulseTreatmentDurationSeconds = null,
            PulseTreatmentDurationSecondsValue = null,
            PulseWidthMilliseconds = null,
            PulseRiseWidthMilliseconds = null,
            PulseIntervalWidthMilliseconds = null,
            DirectCurrentTotalDurationSecondsValue = totalDuration,
            DirectCurrentIntervalSecondsValue = interval,
            DirectCurrentSingleDurationSecondsValue = ramp * 2d,
            DirectCurrentRampUpSecondsValue = ramp,
            DirectCurrentRampDownSecondsValue = ramp
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
        CarrierFrequencyHz = string.Empty;
        OnPropertyChanged(nameof(IntervalMinutesEntry));
        OnPropertyChanged(nameof(SessionDurationMinutesEntry));
        OnPropertyChanged(nameof(RampUpSeconds));
        OnPropertyChanged(nameof(RampDownSecondsEntry));
    }

    private void NotifyModePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsPulseCurrent));
        OnPropertyChanged(nameof(IsDirectCurrent));
        OnPropertyChanged(nameof(IsTemporalInterference));
        OnPropertyChanged(nameof(IsTacs));
        OnPropertyChanged(nameof(IsMonophasicPulseCurrent));
        OnPropertyChanged(nameof(IsDeliveryModeEnabled));
        OnPropertyChanged(nameof(IsIntervalMode));
        OnPropertyChanged(nameof(IsContinuousMode));
        OnPropertyChanged(nameof(IsRampDownEnabled));
        OnPropertyChanged(nameof(ShowDeliveryMode));
        OnPropertyChanged(nameof(ShowInterval));
        OnPropertyChanged(nameof(ShowSingleDuration));
        OnPropertyChanged(nameof(DeliveryModeRowHeight));
        OnPropertyChanged(nameof(IntervalRowHeight));
        OnPropertyChanged(nameof(SingleDurationRowHeight));
        OnPropertyChanged(nameof(RampDownRowHeight));
        OnPropertyChanged(nameof(FrequencyRowHeight));
        OnPropertyChanged(nameof(TotalDurationLabel));
        OnPropertyChanged(nameof(IntervalLabel));
        OnPropertyChanged(nameof(SessionDurationLabel));
        OnPropertyChanged(nameof(RampUpLabel));
        OnPropertyChanged(nameof(RampDownLabel));
        OnPropertyChanged(nameof(IntervalMinutesEntry));
        OnPropertyChanged(nameof(SessionDurationMinutesEntry));
        OnPropertyChanged(nameof(RampDownSecondsEntry));
        OnPropertyChanged(nameof(CarrierFrequencyHz));
    }

    private static string LoadTotalDuration(PrescriptionDefinition prescription, bool isNew)
    {
        if (isNew)
        {
            return string.Empty;
        }

        return prescription.IsPulseCurrent
            ? PulseCurrentParameterRules.FormatTreatmentDuration(
                prescription.PulseTreatmentDurationSecondsResolved)
            : string.Equals(
                prescription.StimulationType,
                StimulationModeCodes.DirectCurrent,
                StringComparison.Ordinal)
                || prescription.IsMonophasicPulseCurrent
                || prescription.IsTacs
                || string.Equals(
                    prescription.StimulationType,
                    StimulationModeCodes.TemporalInterference,
                    StringComparison.Ordinal)
                ? prescription.IsTacs
                    ? TacsParameterRules.Normalize(
                        TacsParameterKind.TotalDurationSeconds,
                        prescription.TacsTotalDurationSeconds.ToString(CultureInfo.InvariantCulture),
                        TacsParameterRules.DefaultTotalDurationSeconds).Value
                    : DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentTotalDurationSeconds)
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
            : string.Equals(
                prescription.StimulationType,
                StimulationModeCodes.DirectCurrent,
                StringComparison.Ordinal)
                || prescription.IsMonophasicPulseCurrent
                || prescription.IsTacs
                || string.Equals(
                    prescription.StimulationType,
                    StimulationModeCodes.TemporalInterference,
                    StringComparison.Ordinal)
                ? DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentIntervalDurationSeconds)
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
            : string.Equals(
                prescription.StimulationType,
                StimulationModeCodes.DirectCurrent,
                StringComparison.Ordinal)
                || prescription.IsMonophasicPulseCurrent
                || prescription.IsTacs
                || string.Equals(
                    prescription.StimulationType,
                    StimulationModeCodes.TemporalInterference,
                    StringComparison.Ordinal)
                ? DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentSingleDurationSeconds)
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
            : string.Equals(
                prescription.StimulationType,
                StimulationModeCodes.DirectCurrent,
                StringComparison.Ordinal)
                || prescription.IsMonophasicPulseCurrent
                || prescription.IsTacs
                || string.Equals(
                    prescription.StimulationType,
                    StimulationModeCodes.TemporalInterference,
                    StringComparison.Ordinal)
                ? prescription.IsTacs
                    ? TacsParameterRules.Normalize(
                        TacsParameterKind.RampUpSeconds,
                        prescription.TacsRampUpSeconds.ToString(CultureInfo.InvariantCulture),
                        TacsParameterRules.DefaultRampUpSeconds).Value
                    : DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentRampUpDurationSeconds)
                : prescription.RampUpSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private static string LoadRampDownValue(PrescriptionDefinition prescription, bool isNew)
    {
        if (isNew || prescription.IsPulseCurrent || prescription.IsMonophasicPulseCurrent)
        {
            return string.Empty;
        }

        return string.Equals(
                prescription.StimulationType,
                StimulationModeCodes.DirectCurrent,
                StringComparison.Ordinal)
            || string.Equals(
                prescription.StimulationType,
                StimulationModeCodes.TemporalInterference,
                StringComparison.Ordinal)
            || prescription.IsTacs
            ? prescription.IsTacs
                ? TacsParameterRules.Normalize(
                    TacsParameterKind.RampDownSeconds,
                    prescription.TacsRampDownSeconds.ToString(CultureInfo.InvariantCulture),
                    TacsParameterRules.DefaultRampDownSeconds).Value
                : DirectCurrentParameterRules.FormatTime(prescription.DirectCurrentRampDownDurationSeconds)
            : prescription.RampDownSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyNewPrescriptionDefaults()
    {
        if (!IsNew)
        {
            return;
        }

        if (IsDirectCurrent)
        {
            CurrentMilliamp = DirectCurrentParameterRules.DefaultCurrentMilliamp;
            DeliveryMode = PrescriptionDeliveryModes.Interval;
            TotalDurationMinutes = DirectCurrentParameterRules.DefaultTotalDurationSeconds;
            intervalValue = DirectCurrentParameterRules.DefaultIntervalSeconds;
            sessionDurationValue = DirectCurrentParameterRules.DefaultSingleDurationSeconds;
            RampUpSeconds = DirectCurrentParameterRules.DefaultRampUpSeconds;
            rampDownValue = DirectCurrentParameterRules.DefaultRampDownSeconds;
        }
        else if (IsTemporalInterference)
        {
            CurrentMilliamp = TiAlternatingCurrentParameterRules.DefaultPeakCurrentMilliampere;
            DeliveryMode = PrescriptionDeliveryModes.Continuous;
            TotalDurationMinutes = TiAlternatingCurrentParameterRules.DefaultTotalDurationSeconds;
            intervalValue = string.Empty;
            sessionDurationValue = string.Empty;
            RampUpSeconds = TiAlternatingCurrentParameterRules.DefaultRampUpSeconds;
            rampDownValue = TiAlternatingCurrentParameterRules.DefaultRampDownSeconds;
        }
        else if (IsTacs)
        {
            CurrentMilliamp = TacsParameterRules.DefaultPeakCurrentMilliampere;
            DeliveryMode = PrescriptionDeliveryModes.Continuous;
            TotalDurationMinutes = TacsParameterRules.DefaultTotalDurationSeconds;
            intervalValue = string.Empty;
            sessionDurationValue = string.Empty;
            RampUpSeconds = TacsParameterRules.DefaultRampUpSeconds;
            rampDownValue = TacsParameterRules.DefaultRampDownSeconds;
            CarrierFrequencyHz = TacsParameterRules.DefaultFrequencyHz;
        }
        else if (IsPulseCurrent)
        {
            CurrentMilliamp = PulseCurrentParameterRules.DefaultCurrentMilliamp;
            DeliveryMode = PrescriptionDeliveryModes.Interval;
            TotalDurationMinutes = PulseCurrentParameterRules.DefaultTreatmentDurationSeconds;
            intervalValue = PulseCurrentParameterRules.DefaultIntervalWidthMilliseconds;
            sessionDurationValue = PulseCurrentParameterRules.DefaultPulseWidthMilliseconds;
            RampUpSeconds = PulseCurrentParameterRules.DefaultRiseWidthMilliseconds;
            rampDownValue = string.Empty;
        }
        else if (IsMonophasicPulseCurrent)
        {
            CurrentMilliamp = MonophasicPulseCurrentParameterRules.DefaultCurrentMilliamp;
            DeliveryMode = PrescriptionDeliveryModes.Interval;
            TotalDurationMinutes = MonophasicPulseCurrentParameterRules.DefaultTotalDurationSeconds;
            intervalValue = MonophasicPulseCurrentParameterRules.DefaultIntervalSeconds;
            sessionDurationValue = string.Empty;
            RampUpSeconds = MonophasicPulseCurrentParameterRules.DefaultRampSeconds;
            rampDownValue = string.Empty;
        }

        OnPropertyChanged(nameof(IntervalMinutesEntry));
        OnPropertyChanged(nameof(SessionDurationMinutesEntry));
        OnPropertyChanged(nameof(RampDownSecondsEntry));
    }

    private bool TryDirectCurrentParameter(
        DirectCurrentParameterKind kind,
        string text,
        out double value)
    {
        if (DirectCurrentParameterRules.TryParseValidated(kind, text, out value, out var error))
        {
            return true;
        }

        ErrorMessage = error;
        return false;
    }

    private bool TryTemporalInterferenceParameter(
        TiAlternatingCurrentParameterKind kind,
        string text,
        out decimal value)
    {
        if (TiAlternatingCurrentParameterRules.TryParseValidated(kind, text, out value, out var error))
        {
            return true;
        }

        ErrorMessage = error;
        return false;
    }

    private bool TryTacsParameter(
        TacsParameterKind kind,
        string text,
        out decimal value)
    {
        if (TacsParameterRules.TryParseValidated(kind, text, out value, out var error))
        {
            return true;
        }

        ErrorMessage = error;
        return false;
    }

    private bool TryPulseCurrentParameter(
        PulseCurrentParameterKind kind,
        string text,
        out double value)
    {
        if (PulseCurrentParameterRules.TryParseValidated(kind, text, out value, out var error))
        {
            return true;
        }

        ErrorMessage = error;
        return false;
    }

    private bool TryMonophasicPulseCurrentParameter(
        MonophasicPulseCurrentParameterKind kind,
        string text,
        out double value)
    {
        if (MonophasicPulseCurrentParameterRules.TryParseValidated(kind, text, out value, out var error))
        {
            return true;
        }

        ErrorMessage = error;
        return false;
    }

    private bool TryPulseCurrentIntegerParameter(
        PulseCurrentParameterKind kind,
        string text,
        out int value)
    {
        value = 0;
        if (!TryPulseCurrentParameter(kind, text, out var parsed))
        {
            return false;
        }

        value = checked((int)parsed);
        return true;
    }

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
        || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool ContainsControlCharacters(params string[] values) =>
        values.Any(InputTextRules.ContainsControlCharacters);
    private static bool TryPositiveInt(string value, out int result) => int.TryParse(value, out result) && result > 0;
    private static bool TryNonNegativeInt(string value, out int result) => int.TryParse(value, out result) && result >= 0;
}
