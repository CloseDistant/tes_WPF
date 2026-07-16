namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;

public enum AssessmentModuleKind
{
    EyeCalibration,
    PictureBrowse,
    VideoBrowse,
    VoiceBaseline,
    WordReading,
    ShortTextReading,
    EmotionQuestion,
    DotProbe,
    Questionnaire,
    BasicInformation,
    GenericTask,
    SynchronizationTest
}

public abstract class AssessmentModuleViewModel : ObservableObject
{
    protected AssessmentModuleViewModel(string code, string displayNameKey, bool isDevelopmentOnly)
    {
        Code = code;
        DisplayNameKey = displayNameKey;
        IsDevelopmentOnly = isDevelopmentOnly;
    }

    public string Code { get; }
    public string DisplayNameKey { get; }
    public bool IsDevelopmentOnly { get; }
    public abstract AssessmentModuleKind Kind { get; }
}

public sealed class EyeCalibrationAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.EyeCalibration;
}

public sealed class PictureBrowseAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.PictureBrowse;
}

public sealed class VideoBrowseAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.VideoBrowse;
}

public sealed class VoiceAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.VoiceBaseline;
}

public sealed class WordReadingAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.WordReading;
}

public sealed class ShortTextReadingAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.ShortTextReading;
}

public sealed class EmotionQuestionAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.EmotionQuestion;
}

public sealed class DotProbeAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.DotProbe;
}

public sealed class QuestionnaireAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.Questionnaire;
}

public sealed class BasicInformationAssessmentModuleViewModel(string code, string key, bool developmentOnly)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind => AssessmentModuleKind.BasicInformation;
}

public sealed class GenericAssessmentModuleViewModel(string code, string key, bool developmentOnly, AssessmentModuleKind kind)
    : AssessmentModuleViewModel(code, key, developmentOnly)
{
    public override AssessmentModuleKind Kind { get; } = kind;
}

public sealed class AssessmentWorkbenchCoordinator : ObservableObject
{
    private int currentModuleIndex;
    private int currentStepIndex = 1;

    public ObservableCollection<AssessmentModuleViewModel> Modules { get; } = [];

    public int CurrentModuleIndex
    {
        get => currentModuleIndex;
        set
        {
            if (value < 0 || value >= Modules.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetProperty(ref currentModuleIndex, value);
            OnPropertyChanged(nameof(CurrentModule));
        }
    }

    public int CurrentStepIndex
    {
        get => currentStepIndex;
        set => SetProperty(ref currentStepIndex, value);
    }

    public AssessmentModuleViewModel? CurrentModule => Modules.Count == 0 ? null : Modules[currentModuleIndex];

    public void Configure(IEnumerable<(string Code, string DisplayNameKey, bool IsDevelopmentOnly)> definitions)
    {
        Modules.Clear();
        foreach (var definition in definitions)
        {
            Modules.Add(CreateModule(definition.Code, definition.DisplayNameKey, definition.IsDevelopmentOnly));
        }

        currentModuleIndex = 0;
        currentStepIndex = 1;
        OnPropertyChanged(nameof(CurrentModuleIndex));
        OnPropertyChanged(nameof(CurrentStepIndex));
        OnPropertyChanged(nameof(CurrentModule));
    }

    private static AssessmentModuleViewModel CreateModule(string code, string key, bool developmentOnly)
    {
        return code switch
        {
            "eye_calibration" => new EyeCalibrationAssessmentModuleViewModel(code, key, developmentOnly),
            "picture_browse" => new PictureBrowseAssessmentModuleViewModel(code, key, developmentOnly),
            "video_browse" => new VideoBrowseAssessmentModuleViewModel(code, key, developmentOnly),
            "voice_baseline" => new VoiceAssessmentModuleViewModel(code, key, developmentOnly),
            "word_reading" => new WordReadingAssessmentModuleViewModel(code, key, developmentOnly),
            "short_text_reading" => new ShortTextReadingAssessmentModuleViewModel(code, key, developmentOnly),
            "emotion_question" => new EmotionQuestionAssessmentModuleViewModel(code, key, developmentOnly),
            "dot_probe" => new DotProbeAssessmentModuleViewModel(code, key, developmentOnly),
            "basic_info" => new BasicInformationAssessmentModuleViewModel(code, key, developmentOnly),
            "sync_test" => new GenericAssessmentModuleViewModel(code, key, developmentOnly, AssessmentModuleKind.SynchronizationTest),
            _ when code.StartsWith("questionnaire_", StringComparison.Ordinal) => new QuestionnaireAssessmentModuleViewModel(code, key, developmentOnly),
            _ => new GenericAssessmentModuleViewModel(code, key, developmentOnly, AssessmentModuleKind.GenericTask)
        };
    }
}
