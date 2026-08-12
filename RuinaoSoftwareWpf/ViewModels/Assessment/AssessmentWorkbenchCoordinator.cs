namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using RuinaoSoftwareWpf.ApplicationContracts;

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
            "emotion_oddball" => new EmotionOddballAssessmentModuleViewModel(code, key, developmentOnly),
            "emotion_letter_search" => new EmotionLetterSearchAssessmentModuleViewModel(code, key, developmentOnly),
            "emotion_stroop" => new EmotionStroopAssessmentModuleViewModel(code, key, developmentOnly),
            "basic_info" => new BasicInformationAssessmentModuleViewModel(code, key, developmentOnly),
            "sync_test" => new GenericAssessmentModuleViewModel(code, key, developmentOnly, AssessmentModuleKind.SynchronizationTest),
            _ when code.StartsWith("questionnaire_", StringComparison.Ordinal) => new QuestionnaireAssessmentModuleViewModel(code, key, developmentOnly),
            _ => new GenericAssessmentModuleViewModel(code, key, developmentOnly, AssessmentModuleKind.GenericTask)
        };
    }
}
