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
    private string totalDurationMinutes;
    private string intervalMinutes;
    private string sessionDurationMinutes;
    private string course;
    private string rampUpSeconds;
    private string rampDownSeconds;
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
        deliveryMode = isNew ? string.Empty : prescription.DeliveryMode;
        totalDurationMinutes = isNew ? string.Empty : prescription.TotalDurationMinutes.ToString(CultureInfo.InvariantCulture);
        intervalMinutes = isNew ? string.Empty : prescription.IntervalMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        sessionDurationMinutes = isNew ? string.Empty : prescription.SessionDurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        course = prescription.Course;
        rampUpSeconds = isNew ? string.Empty : prescription.RampUpSeconds.ToString(CultureInfo.InvariantCulture);
        rampDownSeconds = isNew ? string.Empty : prescription.RampDownSeconds.ToString(CultureInfo.InvariantCulture);
        evidenceGrade = prescription.EvidenceGrade;
    }

    public PrescriptionDefinition Original { get; }
    public bool IsNew { get; }
    public string Title => IsNew ? "新增处方" : "编辑处方";
    public ObservableCollection<string> AvailableStimulationTypes { get; }
    public IReadOnlyList<string> DeliveryModes => PrescriptionDeliveryModes.All;
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Indication { get => indication; set => SetProperty(ref indication, value); }
    public string StimulationType { get => stimulationType; set => SetProperty(ref stimulationType, value); }
    public string CurrentMilliamp { get => currentMilliamp; set => SetProperty(ref currentMilliamp, value); }
    public string DeliveryMode
    {
        get => deliveryMode;
        set
        {
            if (!SetProperty(ref deliveryMode, value)) return;
            OnPropertyChanged(nameof(IsIntervalMode));
            OnPropertyChanged(nameof(IntervalMinutesEntry));
            OnPropertyChanged(nameof(SessionDurationMinutesEntry));
        }
    }
    public bool IsIntervalMode => DeliveryMode == PrescriptionDeliveryModes.Interval;
    public bool IsContinuousMode => DeliveryMode == PrescriptionDeliveryModes.Continuous;
    public string TotalDurationMinutes { get => totalDurationMinutes; set => SetProperty(ref totalDurationMinutes, value); }
    public string IntervalMinutesEntry
    {
        get => IsContinuousMode ? "/" : intervalMinutes;
        set { if (IsIntervalMode) SetProperty(ref intervalMinutes, value, nameof(IntervalMinutesEntry)); }
    }
    public string SessionDurationMinutesEntry
    {
        get => IsContinuousMode ? "/" : sessionDurationMinutes;
        set { if (IsIntervalMode) SetProperty(ref sessionDurationMinutes, value, nameof(SessionDurationMinutesEntry)); }
    }
    public string Course { get => course; set => SetProperty(ref course, value); }
    public string RampUpSeconds { get => rampUpSeconds; set => SetProperty(ref rampUpSeconds, value); }
    public string RampDownSeconds { get => rampDownSeconds; set => SetProperty(ref rampDownSeconds, value); }
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
            ErrorMessage = "电流强度请输入大于 0 的数字。";
            return false;
        }

        if (!TryPositiveInt(TotalDurationMinutes, out var totalDuration)
            || !TryNonNegativeInt(RampUpSeconds, out var rampUp)
            || !TryNonNegativeInt(RampDownSeconds, out var rampDown))
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
            Name = Name.Trim(), Indication = Indication.Trim(), StimulationType = StimulationType,
            CurrentMilliamp = current, DeliveryMode = DeliveryMode, TotalDurationMinutes = totalDuration,
            IntervalMinutes = interval, SessionDurationMinutes = sessionDuration, Course = Course.Trim(),
            RampUpSeconds = rampUp, RampDownSeconds = rampDown, EvidenceGrade = EvidenceGrade.Trim()
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
        || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool TryPositiveInt(string value, out int result) => int.TryParse(value, out result) && result > 0;
    private static bool TryNonNegativeInt(string value, out int result) => int.TryParse(value, out result) && result >= 0;
}
