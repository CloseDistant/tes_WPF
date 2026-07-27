namespace RuinaoSoftwareWpf.ApplicationContracts;

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
    EmotionOddball,
    EmotionLetterSearch,
    EmotionStroop,
    Questionnaire,
    BasicInformation,
    GenericTask,
    SynchronizationTest
}

public sealed record AssessmentModuleDefinition(
    string Code,
    string DisplayNameKey,
    AssessmentModuleKind Kind,
    bool IsDevelopmentOnly);

public interface IAssessmentModule
{
    AssessmentModuleDefinition Definition { get; }
}
