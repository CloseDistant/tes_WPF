namespace RuinaoSoftwareWpf.Tests;

using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class AssessmentModuleFlowDefinitionTests
{
    [Fact]
    public void FormalModuleFlow_ExcludesCalibrationAndUnneededEmotionModulesWithoutReusingIds()
    {
        var flow = AssessmentCaptureViewModel.FormalModuleFlow;
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["picture_browse"] = AssessmentModuleTypeIds.PictureBrowse,
            ["video_browse"] = AssessmentModuleTypeIds.VideoBrowse,
            ["voice_baseline"] = AssessmentModuleTypeIds.VoiceBaseline,
            ["word_reading"] = AssessmentModuleTypeIds.WordReading,
            ["short_text_reading"] = AssessmentModuleTypeIds.ShortTextReading,
            ["emotion_question"] = AssessmentModuleTypeIds.EmotionQuestion,
            ["emotion_letter_search"] = AssessmentModuleTypeIds.EmotionLetterSearch,
            ["emotion_stroop"] = AssessmentModuleTypeIds.EmotionStroop,
            ["basic_info"] = AssessmentModuleTypeIds.BasicInformation,
            ["questionnaire_a"] = AssessmentModuleTypeIds.QuestionnaireA,
            ["questionnaire_b"] = AssessmentModuleTypeIds.QuestionnaireB,
            ["questionnaire_c"] = AssessmentModuleTypeIds.QuestionnaireC,
            ["questionnaire_d"] = AssessmentModuleTypeIds.QuestionnaireD,
            ["questionnaire_e"] = AssessmentModuleTypeIds.QuestionnaireE,
            ["questionnaire_f"] = AssessmentModuleTypeIds.QuestionnaireF,
            ["questionnaire_g"] = AssessmentModuleTypeIds.QuestionnaireG,
            ["questionnaire_h"] = AssessmentModuleTypeIds.QuestionnaireH,
            ["questionnaire_i"] = AssessmentModuleTypeIds.QuestionnaireI,
            ["questionnaire_j"] = AssessmentModuleTypeIds.QuestionnaireJ
        };

        Assert.Equal(expected.Count, flow.Count);
        Assert.Equal(AssessmentModuleTypeIds.PictureBrowse, flow[0].ModuleTypeId);
        Assert.Equal("picture_browse", flow[0].ModuleCode);
        Assert.Equal(AssessmentModuleTypeIds.QuestionnaireJ, flow[^1].ModuleTypeId);
        Assert.Equal("questionnaire_j", flow[^1].ModuleCode);
        Assert.DoesNotContain(flow, module => module.ModuleTypeId == AssessmentModuleTypeIds.EyeCalibration);
        Assert.DoesNotContain(flow, module => module.ModuleTypeId == AssessmentModuleTypeIds.DotProbe);
        Assert.DoesNotContain(flow, module => module.ModuleTypeId == AssessmentModuleTypeIds.EmotionOddball);
        Assert.Equal(24, AssessmentModuleTypeIds.NextAvailable);
        Assert.Equal(flow.Count, flow.Select(module => module.ModuleTypeId).Distinct().Count());
        Assert.Equal(flow.Count, flow.Select(module => module.ModuleCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(flow, module => Assert.Equal(expected[module.ModuleCode], module.ModuleTypeId));
    }
}
