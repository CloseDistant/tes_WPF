namespace RuinaoHardwareDebugWpf;

using System.ComponentModel;
using System.Text.Json;

public sealed partial class AssessmentCaptureViewModel
{
    private void BeginBasicInfoForm()
    {
        StopModuleExecutionTimers();
        MoveToStep(CaptureWorkbenchStep.ModuleExecution);
        isDemoCompleted = true;
        isDemoPlaying = false;
        StageNoticeText = string.Empty;
        basicInfoSaveStatusText = T("CaptureWorkspaceFormPending");
        OnPropertyChanged(nameof(WorkbenchStatusText));
        NotifyStageChanged();
    }

    /// <summary>
    /// 打开个人信息选项面板。
    /// 这里不用 WPF 原生下拉框，避免系统弹层样式和深色采集工作台不一致。
    /// </summary>
    private void OpenBasicInfoOptionPanel(object? parameter)
    {
        var field = parameter?.ToString() ?? string.Empty;
        string[] options;
        string title;
        switch (field)
        {
            case "gender":
                options = BasicInfoGenderOptions;
                title = BasicInfoGenderLabelText;
                break;
            case "education":
                options = BasicInfoEducationOptions;
                title = BasicInfoEducationLabelText;
                break;
            case "occupation":
                options = BasicInfoOccupationOptions;
                title = BasicInfoOccupationLabelText;
                break;
            case "income":
                options = BasicInfoIncomeOptions;
                title = BasicInfoIncomeLevelLabelText;
                break;
            default:
                return;
        }

        basicInfoOptionField = field;
        BasicInfoOptionTitle = title;
        CurrentBasicInfoOptions.Clear();
        foreach (var option in options)
        {
            CurrentBasicInfoOptions.Add(option);
        }

        IsBasicInfoOptionPanelOpen = true;
    }

    private void SelectBasicInfoOption(object? parameter)
    {
        var selected = parameter?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        switch (basicInfoOptionField)
        {
            case "gender":
                SelectedBasicInfoGender = selected;
                OnPropertyChanged(nameof(SelectedBasicInfoGenderDisplay));
                break;
            case "education":
                SelectedBasicInfoEducation = selected;
                OnPropertyChanged(nameof(SelectedBasicInfoEducationDisplay));
                break;
            case "occupation":
                SelectedBasicInfoOccupation = selected;
                OnPropertyChanged(nameof(SelectedBasicInfoOccupationDisplay));
                break;
            case "income":
                SelectedBasicInfoIncomeLevel = selected;
                OnPropertyChanged(nameof(SelectedBasicInfoIncomeLevelDisplay));
                break;
        }

        CloseBasicInfoOptionPanel();
    }

    private void CloseBasicInfoOptionPanel()
    {
        IsBasicInfoOptionPanelOpen = false;
        basicInfoOptionField = string.Empty;
        BasicInfoOptionTitle = string.Empty;
        CurrentBasicInfoOptions.Clear();
    }

    private void LoadCurrentQuestionnaireQuestions()
    {
        foreach (var question in QuestionnaireQuestionItems)
        {
            question.PropertyChanged -= OnQuestionnaireQuestionPropertyChanged;
        }

        questionnaireSession.Clear();
        if (GetQuestionnaireDefinition(CurrentModuleCode) is not { } definition)
        {
            NotifyCurrentQuestionnaireQuestionChanged();
            return;
        }

        foreach (var question in definition.Questions)
        {
            var answerOptions = question.AnswerOptions ?? definition.AnswerOptions;
            var questionItem = new QuestionnaireQuestionItem(
                question.Number,
                question.Text,
                T("CaptureWorkspaceChooseOption"),
                answerOptions,
                IsTwoColumnQuestionnaire(definition.ModuleCode) ? 2 : 1);
            questionItem.PropertyChanged += OnQuestionnaireQuestionPropertyChanged;
            QuestionnaireQuestionItems.Add(questionItem);
        }

        NotifyCurrentQuestionnaireQuestionChanged();
    }

    private static bool IsTwoColumnQuestionnaire(string moduleCode)
    {
        return string.Equals(moduleCode, QuestionnaireGModuleCode, StringComparison.Ordinal)
            || string.Equals(moduleCode, QuestionnaireJModuleCode, StringComparison.Ordinal);
    }

    private void BeginQuestionnaireForm()
    {
        LoadCurrentQuestionnaireQuestions();
        StopModuleExecutionTimers();
        MoveToStep(CaptureWorkbenchStep.ModuleExecution);
        isDemoCompleted = true;
        isDemoPlaying = false;
        StageNoticeText = string.Empty;
        questionnaireSaveStatusText = T("CaptureWorkspaceFormPending");
        OnPropertyChanged(nameof(WorkbenchStatusText));
        NotifyStageChanged();
    }

    /// <summary>
    /// 打开问卷选项面板。
    /// 问卷沿用个人信息模块的自制选择面板，不依赖系统 ComboBox 弹层。
    /// </summary>
    private void OpenQuestionnaireOptionPanel(object? parameter)
    {
        if (parameter is not QuestionnaireQuestionItem question)
        {
            return;
        }

        selectedQuestionnaireQuestion = question;
        QuestionnaireOptionTitle = T("CaptureWorkspaceQuestionnaireOptionTitle", question.Number);
        CurrentQuestionnaireOptions.Clear();
        foreach (var option in question.AnswerOptions)
        {
            CurrentQuestionnaireOptions.Add(option);
        }

        IsQuestionnaireOptionPanelOpen = true;
    }

    private void SelectQuestionnaireOption(object? parameter)
    {
        var selected = parameter?.ToString() ?? string.Empty;
        var targetQuestion = selectedQuestionnaireQuestion ?? CurrentQuestionnaireQuestion;
        if (targetQuestion is null || string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        targetQuestion.AnswerText = selected;
        if (!string.IsNullOrWhiteSpace(QuestionnaireValidationMessage))
        {
            QuestionnaireValidationMessage = string.Empty;
        }

        CloseQuestionnaireOptionPanel();
    }

    private void GoToPreviousQuestionnaireQuestion()
    {
        if (!CanGoPreviousQuestionnaireQuestion)
        {
            return;
        }

        questionnaireSession.MovePrevious();
        CloseQuestionnaireOptionPanel();
        NotifyCurrentQuestionnaireQuestionChanged();
    }

    private void GoToNextQuestionnaireQuestion()
    {
        if (!CanGoNextQuestionnaireQuestion)
        {
            return;
        }

        if (CurrentQuestionnaireQuestion is null || string.IsNullOrWhiteSpace(CurrentQuestionnaireQuestion.AnswerText))
        {
            QuestionnaireValidationMessage = localization.IsChinese
                ? "请先选择当前题目的答案，再进入下一题。"
                : "Please select an answer before continuing to the next question.";
            return;
        }

        questionnaireSession.MoveNext();
        CloseQuestionnaireOptionPanel();
        NotifyCurrentQuestionnaireQuestionChanged();
    }

    private void OnQuestionnaireQuestionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(QuestionnaireQuestionItem.AnswerText))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(QuestionnaireValidationMessage))
        {
            QuestionnaireValidationMessage = string.Empty;
        }
    }

    private void NotifyCurrentQuestionnaireQuestionChanged()
    {
        OnPropertyChanged(nameof(CurrentQuestionnaireQuestion));
        OnPropertyChanged(nameof(CurrentQuestionnaireQuestionNumber));
        OnPropertyChanged(nameof(QuestionnaireQuestionCount));
        OnPropertyChanged(nameof(QuestionnaireProgressText));
        OnPropertyChanged(nameof(CanGoPreviousQuestionnaireQuestion));
        OnPropertyChanged(nameof(CanGoNextQuestionnaireQuestion));
        OnPropertyChanged(nameof(ShowQuestionnaireNextButton));
        OnPropertyChanged(nameof(ShowQuestionnaireSubmitButton));
    }

    private void CloseQuestionnaireOptionPanel()
    {
        IsQuestionnaireOptionPanelOpen = false;
        selectedQuestionnaireQuestion = null;
        QuestionnaireOptionTitle = string.Empty;
        CurrentQuestionnaireOptions.Clear();
    }

    private async Task SubmitQuestionnaireAsync()
    {
        var definition = GetQuestionnaireDefinition(CurrentModuleCode);
        if (!IsQuestionnaireStage || definition is null)
        {
            return;
        }

        var missingQuestions = QuestionnaireQuestionItems
            .Where(static question => string.IsNullOrWhiteSpace(question.AnswerText))
            .ToList();

        if (missingQuestions.Count > 0)
        {
            questionnaireSession.MoveTo(missingQuestions[0]);
            NotifyCurrentQuestionnaireQuestionChanged();

            QuestionnaireValidationMessage = FormatMissingQuestionMessage(missingQuestions);
            return;
        }

        QuestionnaireValidationMessage = string.Empty;
        SetQuestionnaireSaveStatus(T("CaptureWorkspaceFormSaving"));
        var submittedAt = DateTimeOffset.Now;
        var payload = new
        {
            moduleCode = definition.ModuleCode,
            moduleName = CurrentModule,
            formType = "questionnaire",
            questionnaireCode = definition.QuestionnaireCode,
            title = QuestionnaireTitleText,
            subtitle = QuestionnaireSubtitleText,
            instruction = QuestionnaireInstructionText,
            answers = QuestionnaireQuestionItems.Select(static question => new
            {
                questionNo = question.Number,
                questionText = question.Text,
                answerText = question.AnswerText,
                answerIndex = question.AnswerIndex,
                score = question.Score
            }).ToArray(),
            submittedAtUnixMs = submittedAt.ToUnixTimeMilliseconds()
        };

        await SubmitFormAsync(
            definition.ModuleCode,
            CurrentModule,
            payload,
            submittedAt,
            () =>
            {
                SetQuestionnaireSaveStatus(T("CaptureWorkspaceFormSaved"));
                StageNoticeText = T("CaptureWorkspaceQuestionnaireCompletedNotice", CurrentModule, NextModule);
                MoveToStep(CaptureWorkbenchStep.Completed);
                UpdateModuleProgressItems();
                NotifyStageChanged();
            },
            message =>
            {
                SetQuestionnaireSaveStatus(T("CaptureWorkspaceFormSaveFailed"));
                QuestionnaireValidationMessage = T("CaptureWorkspaceQuestionnaireSaveFailed", message);
            });
    }

    private async Task SubmitBasicInfoAsync()
    {
        if (!IsBasicInfoStage)
        {
            return;
        }

        var missingFields = GetMissingBasicInfoFields();
        if (missingFields.Count > 0)
        {
            BasicInfoValidationMessage = T("CaptureWorkspaceBasicInfoRequired", string.Join("、", missingFields));
            return;
        }

        if (!IsValidBasicInfoBirthDate())
        {
            BasicInfoValidationMessage = T("CaptureWorkspaceBasicInfoBirthDateInvalid");
            return;
        }

        BasicInfoValidationMessage = string.Empty;
        SetBasicInfoSaveStatus(T("CaptureWorkspaceFormSaving"));
        var submittedAt = DateTimeOffset.Now;
        var payload = new
        {
            moduleCode = BasicInfoModuleCode,
            moduleName = CurrentModule,
            formType = "basic_info",
            gender = SelectedBasicInfoGender,
            birthDate = basicInfoBirthDateText.Trim(),
            education = SelectedBasicInfoEducation,
            occupation = SelectedBasicInfoOccupation,
            incomeLevel = SelectedBasicInfoIncomeLevel,
            submittedAtUnixMs = submittedAt.ToUnixTimeMilliseconds()
        };

        await SubmitFormAsync(
            BasicInfoModuleCode,
            CurrentModule,
            payload,
            submittedAt,
            () =>
            {
                SetBasicInfoSaveStatus(T("CaptureWorkspaceFormSaved"));
                StageNoticeText = T("CaptureWorkspaceBasicInfoCompletedNotice", NextModule);
                MoveToStep(CaptureWorkbenchStep.Completed);
                UpdateModuleProgressItems();
                NotifyStageChanged();
            },
            message =>
            {
                SetBasicInfoSaveStatus(T("CaptureWorkspaceFormSaveFailed"));
                BasicInfoValidationMessage = T("CaptureWorkspaceBasicInfoSaveFailed", message);
            });
    }

    private async Task SubmitFormAsync(
        string moduleCode,
        string moduleName,
        object payload,
        DateTimeOffset submittedAt,
        Action onSuccess,
        Action<string> onError)
    {
        try
        {
            var outputRoot = CaptureOutputPathProvider.GetOutputRoot();
            var sessionKey = await GetOrStartUnifiedSessionKeyAsync();
            var payloadJson = JsonSerializer.Serialize(payload);
            await CaptureMediaRecorder.SaveFormModuleRecordAsync(
                outputRoot,
                sessionKey,
                moduleCode,
                moduleName,
                payloadJson);

            await unifiedSessionService.RecordEventAsync(
                SessionModuleCodes.DigitalPhenotype,
                "form_submitted",
                moduleName,
                payloadJson,
                submittedAt);

            onSuccess();
        }
        catch (Exception exception)
        {
            onError(exception.Message);
        }
    }

    private List<string> GetMissingBasicInfoFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(SelectedBasicInfoGender))
        {
            missing.Add(T("CaptureWorkspaceBasicInfoGender"));
        }

        if (string.IsNullOrWhiteSpace(BasicInfoBirthDateText))
        {
            missing.Add(T("CaptureWorkspaceBasicInfoBirthDate"));
        }

        if (string.IsNullOrWhiteSpace(SelectedBasicInfoEducation))
        {
            missing.Add(T("CaptureWorkspaceBasicInfoEducation"));
        }

        if (string.IsNullOrWhiteSpace(SelectedBasicInfoOccupation))
        {
            missing.Add(T("CaptureWorkspaceBasicInfoOccupation"));
        }

        if (string.IsNullOrWhiteSpace(SelectedBasicInfoIncomeLevel))
        {
            missing.Add(T("CaptureWorkspaceBasicInfoIncomeLevel"));
        }

        return missing;
    }

    private bool IsValidBasicInfoBirthDate()
    {
        return DateTime.TryParseExact(
            BasicInfoBirthDateText.Trim(),
            "yyyy-MM-dd",
            null,
            System.Globalization.DateTimeStyles.None,
            out _);
    }

    private string FormatMissingQuestionMessage(IReadOnlyList<QuestionnaireQuestionItem> missingQuestions)
    {
        if (missingQuestions.Count <= 5)
        {
            var allQuestions = string.Join("、", missingQuestions.Select(static question => $"第 {question.Number} 题"));
            return T("CaptureWorkspaceQuestionnaireRequired", allQuestions);
        }

        var previewQuestions = string.Join("、", missingQuestions.Take(5).Select(static question => $"第 {question.Number} 题"));
        return T("CaptureWorkspaceQuestionnaireRequiredLong", missingQuestions.Count, previewQuestions);
    }

    private void ResetBasicInfoFormState(bool clearValues)
    {
        BasicInfoValidationMessage = string.Empty;
        basicInfoSaveStatusText = T("CaptureWorkspaceFormPending");
        if (clearValues)
        {
            selectedBasicInfoGender = string.Empty;
            basicInfoBirthDateText = string.Empty;
            selectedBasicInfoEducation = string.Empty;
            selectedBasicInfoOccupation = string.Empty;
            selectedBasicInfoIncomeLevel = string.Empty;
            OnPropertyChanged(nameof(SelectedBasicInfoGender));
            OnPropertyChanged(nameof(SelectedBasicInfoGenderDisplay));
            OnPropertyChanged(nameof(BasicInfoBirthDateText));
            OnPropertyChanged(nameof(BasicInfoBirthDateDisplay));
            OnPropertyChanged(nameof(SelectedBasicInfoEducation));
            OnPropertyChanged(nameof(SelectedBasicInfoEducationDisplay));
            OnPropertyChanged(nameof(SelectedBasicInfoOccupation));
            OnPropertyChanged(nameof(SelectedBasicInfoOccupationDisplay));
            OnPropertyChanged(nameof(SelectedBasicInfoIncomeLevel));
            OnPropertyChanged(nameof(SelectedBasicInfoIncomeLevelDisplay));
        }

        OnPropertyChanged(nameof(WorkbenchStatusText));
    }

    private void ResetQuestionnaireState(bool clearAnswers)
    {
        QuestionnaireValidationMessage = string.Empty;
        questionnaireSaveStatusText = T("CaptureWorkspaceFormPending");
        CloseQuestionnaireOptionPanel();
        questionnaireSession.Reset(clearAnswers);

        NotifyCurrentQuestionnaireQuestionChanged();
        OnPropertyChanged(nameof(WorkbenchStatusText));
    }

    private void SetBasicInfoSaveStatus(string value)
    {
        basicInfoSaveStatusText = value;
        OnPropertyChanged(nameof(WorkbenchStatusText));
    }

    private void SetQuestionnaireSaveStatus(string value)
    {
        questionnaireSaveStatusText = value;
        OnPropertyChanged(nameof(WorkbenchStatusText));
    }

    private string GetCurrentFormSaveStatusText()
    {
        if (IsQuestionnaireModule)
        {
            return questionnaireSaveStatusText;
        }

        return basicInfoSaveStatusText;
    }

    private string ToOptionDisplay(string value) => string.IsNullOrWhiteSpace(value)
        ? T("CaptureWorkspaceChooseOption")
        : value;

    private bool SetBasicInfoField<T>(ref T field, T value)
    {
        if (SetProperty(ref field, value))
        {
            if (!string.IsNullOrWhiteSpace(BasicInfoValidationMessage))
            {
                BasicInfoValidationMessage = string.Empty;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 清理视频浏览内部状态。
    /// 用于切换模块、重播演示或中断当前模块。
    /// </summary>
}
