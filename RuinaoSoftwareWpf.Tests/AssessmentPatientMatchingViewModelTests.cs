namespace RuinaoSoftwareWpf.Tests;

using System.Text.Json;
using Xunit;

public sealed class AssessmentPatientMatchingViewModelTests
{
    [Fact]
    public async Task SearchAsync_ResetsToFirstPageAndUsesSelectedPageSize()
    {
        var service = new RecordingExternalFollowUpService();
        var viewModel = CreateViewModel(service);
        viewModel.PhoneQuery = "  13800000000 ";
        viewModel.SelectedPageSize = 20;

        await viewModel.SearchAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(service.Requests);
        Assert.Equal(1, request.PageNumber);
        Assert.Equal(20, request.PageSize);
        Assert.Equal("  13800000000 ", request.Phone);
        Assert.Equal(1, viewModel.CurrentPage);
        Assert.Equal(3, viewModel.TotalPage);
        Assert.Equal(46, viewModel.Total);
    }

    [Fact]
    public async Task GoToPageAsync_ClampsPageToServerTotalPage()
    {
        var service = new RecordingExternalFollowUpService();
        var viewModel = CreateViewModel(service);

        await viewModel.SearchAsync(TestContext.Current.CancellationToken);
        await viewModel.GoToPageAsync(99, TestContext.Current.CancellationToken);

        Assert.Equal(2, service.Requests.Count);
        Assert.Equal(3, service.Requests[1].PageNumber);
        Assert.Equal(3, viewModel.CurrentPage);
        Assert.False(viewModel.CanGoNext);
    }

    [Fact]
    public async Task GoToPageAsync_WhenNoResults_DoesNotRequestOutOfRangePage()
    {
        var service = new RecordingExternalFollowUpService
        {
            Result = new ExternalFollowUpPatientPage(1, 10, 0, 0, [])
        };
        var viewModel = CreateViewModel(service);

        await viewModel.SearchAsync(TestContext.Current.CancellationToken);
        await viewModel.GoToPageAsync(2, TestContext.Current.CancellationToken);

        Assert.Single(service.Requests);
        Assert.Equal(0, viewModel.CurrentPage);
        Assert.False(viewModel.CanGoPrevious);
        Assert.False(viewModel.CanGoNext);
    }

    [Fact]
    public async Task SelectingPatient_LoadsDetailsAndOnlyOneRowRemainsExpanded()
    {
        var service = new RecordingExternalFollowUpService();
        service.FollowUpDetails = [new ExternalFollowUpDetail(
            117, 9, 3, "第2次随访", "测试患者", "2026-08-01", "2026-08-15",
            1, "已完成", null, 0, "待测评", 12, "流程", null,
            null, null, null, null, null, 3)];
        var viewModel = CreateViewModel(service);
        await viewModel.SearchAsync(TestContext.Current.CancellationToken);

        var first = viewModel.Patients[0];
        await viewModel.SelectPatientAsync(first, TestContext.Current.CancellationToken);

        Assert.True(first.IsExpanded);
        Assert.Equal("13800000000", Assert.Single(service.DetailRequests));
        var followUp = Assert.Single(first.FollowUps);
        Assert.Equal(117, followUp.Id);
        Assert.True(first.SelectFollowUpCommand.CanExecute(followUp));
        first.SelectFollowUpCommand.Execute(followUp);
        Assert.Same(followUp, first.SelectedFollowUp);
        Assert.Contains("detailId 117", first.SelectedFollowUpSummary);

        var secondPatient = new ExternalFollowUpPatient("RP", "第二位", "中心", "batch-2", "1002", "13900000000");
        service.Result = service.Result with { Items = [first.Patient, secondPatient] };
        await viewModel.SearchAsync(TestContext.Current.CancellationToken);
        await viewModel.SelectPatientAsync(viewModel.Patients[0], TestContext.Current.CancellationToken);
        await viewModel.SelectPatientAsync(viewModel.Patients[1], TestContext.Current.CancellationToken);

        Assert.False(viewModel.Patients[0].IsExpanded);
        Assert.True(viewModel.Patients[1].IsExpanded);
    }

    [Fact]
    public void FollowUpDetail_AllowsNumericDateValuesReturnedByServer()
    {
        const string json = """
            {
              "id": "117",
              "followUpId": "9",
              "settingId": "3",
              "settingName": "第2次随访",
              "name": "测试患者",
              "followUpStartTime": 1754006400000,
              "followUpEndTime": null,
              "questionnaireStatus": "1",
              "questionnaireStatusName": "已完成",
              "questionnaireCompleteTime": null,
              "flowStatus": "0",
              "flowStatusName": "待测评",
              "flowId": null,
              "flowName": null,
              "flowCompleteTime": null,
              "pcFlowId": null,
              "pcFlowName": null,
              "pcFlowCompleteTime": null,
              "assessmentRecordId": null,
              "pcAssessmentRecordId": null,
              "scaleCount": "3"
            }
            """;

        var detail = JsonSerializer.Deserialize<ExternalFollowUpDetail>(json);

        Assert.NotNull(detail);
        Assert.Equal("1754006400000", detail.FollowUpStartTime);
    }

    [Fact]
    public void FollowUpDetail_AllowsNullNumericValuesReturnedByServer()
    {
        const string json = """
            {
              "id": null,
              "followUpId": null,
              "settingId": null,
              "questionnaireStatus": null,
              "flowStatus": null,
              "scaleCount": null
            }
            """;

        var detail = JsonSerializer.Deserialize<ExternalFollowUpDetail>(json);

        Assert.NotNull(detail);
        Assert.Null(detail.Id);
        Assert.Null(detail.FollowUpId);
        Assert.Null(detail.SettingId);
        Assert.Null(detail.QuestionnaireStatus);
        Assert.Null(detail.FlowStatus);
        Assert.Null(detail.ScaleCount);
    }

    private static AssessmentPatientMatchingViewModel CreateViewModel(
        RecordingExternalFollowUpService service) =>
        new(service, new AppLocalizationService(), new NullLoggingService(), new NullToastService());

    private sealed class RecordingExternalFollowUpService : IExternalFollowUpService
    {
        public List<(string? Phone, int PageNumber, int PageSize)> Requests { get; } = [];
        public List<string> DetailRequests { get; } = [];
        public IReadOnlyList<ExternalFollowUpDetail> FollowUpDetails { get; set; } = [];

        public ExternalFollowUpPatientPage Result { get; set; } = new(
            1,
            10,
            3,
            46,
            [new ExternalFollowUpPatient("RP", "测试患者", "测试中心", "batch-1", "1001", "13800000000")]);

        public Task<ExternalFollowUpPatientPage> SearchPatientsAsync(
            string? phone,
            int pageNumber = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((phone, pageNumber, pageSize));
            var page = Result with
            {
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            return Task.FromResult(page);
        }

        public Task<IReadOnlyList<ExternalFollowUpDetail>> GetFollowUpDetailsAsync(
            string phone,
            CancellationToken cancellationToken = default)
        {
            DetailRequests.Add(phone);
            return Task.FromResult(FollowUpDetails);
        }
    }

    private sealed class NullLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }

    private sealed class NullToastService : IToastService
    {
        public System.Windows.Visibility Visibility => System.Windows.Visibility.Collapsed;
        public string Title => string.Empty;
        public string Message => string.Empty;
        public string Icon => string.Empty;
        public string Accent => string.Empty;
        public void Show(ToastKind kind, string title, string message, TimeSpan? duration = null) { }
        public void ShowInformation(string message, string title = "提示") { }
        public void ShowSuccess(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}
