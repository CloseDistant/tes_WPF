namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 多语言 ViewModel。
///
/// 它把 ILocalizationService 的 key 访问封装成属性，
/// 这样 XAML 可以直接绑定 {Binding Localization.DashboardText}，不需要额外转换器。
/// </summary>
public sealed class LocalizationViewModel : ObservableObject
{
    private readonly ILocalizationService localization;

    public LocalizationViewModel(ILocalizationService localization)
    {
        this.localization = localization;
        this.localization.LanguageChanged += (_, _) => NotifyAllTextChanged();
    }

    /// <summary>当前是否为中文。</summary>
    public bool IsChinese => localization.IsChinese;

    // 下面大量属性都是把 key 映射成文本，供 XAML 绑定。
    public string LanguageText => T("Language");
    public string DashboardText => T("Dashboard");
    public string PageControlText => T("PageControl");
    public string ClosedLoopControlText => T("ClosedLoopControl");
    public string AssessmentCaptureText => T("AssessmentCapture");
    public string EegSignalCaptureText => T("EegSignalCapture");
    public string ElectrodePlanningText => T("ElectrodePlanning");
    public string HeadModelText => T("HeadModel");
    public string FemSimulationText => T("FemSimulation");
    public string ProtocolManagerText => T("ProtocolManager");
    public string TreatmentHistoryText => T("TreatmentHistory");
    public string TreatmentHistoryTitleText => T("TreatmentHistoryTitle");
    public string SettingsText => T("Settings");
    public string HelpText => T("Help");
    public string PrescriptionMenuText => T("PrescriptionMenu");
    public string DeviceMenuText => T("DeviceMenu");
    public string SimulationMenuText => T("SimulationMenu");
    public string ToolsMenuText => T("ToolsMenu");
    public string HelpMenuText => T("Help");
    public string AccountMenuText => T("AccountMenu");
    public string ConnectText => T("Connect");
    public string DisconnectText => T("Disconnect");
    public string HandshakeText => T("Handshake");
    public string ReadProductModelText => T("ReadProductModel");
    public string ReadBoardModelText => T("ReadBoardModel");
    public string ImpedanceCheckText => T("ImpedanceCheck");
    public string SwitchLanguageText => T("SwitchLanguage");
    public string NewPrescriptionText => T("NewPrescription");
    public string ImportPrescriptionText => T("ImportPrescription");
    public string PrescriptionListText => T("PrescriptionList");
    public string LoadModelText => T("LoadModel");
    public string RunSimulationText => T("RunSimulation");
    public string SimulationResultText => T("SimulationResult");
    public string OpenLogFolderText => T("OpenLogFolder");
    public string ExportConfigText => T("ExportConfig");
    public string UserGuideText => T("UserGuide");
    public string ShortcutHelpText => T("ShortcutHelp");
    public string AboutText => T("About");
    public string CurrentUserText => T("CurrentUser");
    public string PermissionSettingsText => T("PermissionSettings");
    public string LogoutText => T("Logout");
    public string SystemInfoText => T("SystemInfo");
    public string PlaceholderFeatureText => T("PlaceholderFeature");
    public string ExitApplicationText => T("ExitApplication");
    public string StartButtonText => T("StartButton");
    public string SynchronizedStartButtonText => T("SynchronizedStartButton");
    public string EmergencyStopButtonText => T("EmergencyStopButton");
    public string NameLabel => T("Name");
    public string SexLabel => T("Sex");
    public string AgeLabel => T("Age");
    public string NavigationText => T("Navigation");
    public string ControlText => T("Control");
    public string StimulationTypeTitleText => T("StimulationTypeTitle");
    public string StimulationTypeHintText => T("StimulationTypeHint");
    public string TemporalInterferenceText => T("TemporalInterference");
    public string TranscranialDirectCurrentText => T("TranscranialDirectCurrent");
    public string ComingSoonText => T("ComingSoon");
    public string BackText => T("Back");
    public string ModeTitleText => T("ModeTitle");
    public string DirectCurrentModeTitleText => T("DirectCurrentModeTitle");
    public string ChannelHintText => T("ChannelHint");
    public string RemainingTimeLabel => T("RemainingTime");
    public string StatusMonitorLabel => T("StatusMonitor");
    public string AnodeTempLabel => T("AnodeTemp");
    public string CathodeTempLabel => T("CathodeTemp");
    public string ImpedanceLabel => T("Impedance");
    public string AmplitudeLabel => T("Amplitude");
    public string RampUpLabel => T("RampUp");
    public string RampDownLabel => T("RampDown");
    public string PolarityLabel => T("Polarity");
    public string ModeLabel => T("Mode");
    public string IntervalModeText => T("IntervalMode");
    public string ContinuousModeText => T("ContinuousMode");
    public string DurationLabel => T("Duration");
    public string IntervalTimeLabel => T("IntervalTime");
    public string SingleDurationLabel => T("SingleDuration");
    public string CarrierFrequencyLabel => T("CarrierFrequency");
    public string WaveformLabel => T("Waveform");
    public string ElectrodeLabel => T("Electrode");
    public string FemStep1Text => T("FemStep1");
    public string FemStep2Text => T("FemStep2");
    public string FemLoadModelText => T("FemLoadModel");
    public string FemVisualizationText => T("FemVisualization");
    public string FemLoadHeadModelText => T("FemLoadHeadModel");
    public string FemChooseFileText => T("FemChooseFile");
    public string FemCoronalSliceText => T("FemCoronalSlice");
    public string FemSagittalSliceText => T("FemSagittalSlice");
    public string FemAxialSliceText => T("FemAxialSlice");
    public string FemGenerateMontageText => T("FemGenerateMontage");
    public string FemModelStatusText => T("FemModelStatus");
    public string FemSimulationStatusText => T("FemSimulationStatus");
    public string DeviceOnlineText => T("DeviceOnline");
    public string DeviceOfflineText => T("DeviceOffline");

    public string FeatureText(string localizationKey) => T(localizationKey);

    /// <summary>根据页面获取页面标题。</summary>
    public string PageTitle(AppPage page) => page switch
    {
        AppPage.Dashboard => DashboardText,
        AppPage.Control => PageControlText,
        AppPage.EegSignalCapture => EegSignalCaptureText,
        AppPage.ClosedLoopControl => ClosedLoopControlText,
        AppPage.AssessmentCapture => AssessmentCaptureText,
        AppPage.ElectrodePlanning => ElectrodePlanningText,
        AppPage.HeadModel => HeadModelText,
        AppPage.FemSimulation => FemSimulationText,
        AppPage.ProtocolManager => ProtocolManagerText,
        AppPage.TreatmentHistory => TreatmentHistoryTitleText,
        AppPage.Settings => SettingsText,
        _ => HelpText
    };

    /// <summary>根据页面获取占位页描述文字。</summary>
    public string PlaceholderDescription(AppPage page) => page switch
    {
        AppPage.ClosedLoopControl => T("PlaceholderClosedLoopControl"),
        AppPage.EegSignalCapture => T("PlaceholderEegSignalCapture"),
        AppPage.ElectrodePlanning => T("PlaceholderElectrodePlanning"),
        AppPage.HeadModel => T("PlaceholderHeadModel"),
        AppPage.ProtocolManager => T("PlaceholderProtocolManager"),
        AppPage.TreatmentHistory => T("PlaceholderTreatmentHistory"),
        AppPage.Help => T("PlaceholderHelp"),
        _ => T("PlaceholderDefault")
    };

    /// <summary>切换语言。</summary>
    public void ToggleLanguage() => localization.ToggleLanguage();

    private string T(string key) => localization.Text(key);

    /// <summary>
    /// 通知所有绑定属性都发生了变化。
    /// 传空字符串表示全部刷新，常用于语言切换。
    /// </summary>
    private void NotifyAllTextChanged()
    {
        OnPropertyChanged(string.Empty);
    }
}
