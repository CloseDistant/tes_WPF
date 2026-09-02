namespace RuinaoSoftwareWpf.Tests;

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

    private static AssessmentPatientMatchingViewModel CreateViewModel(
        RecordingExternalFollowUpService service) =>
        new(service, new AppLocalizationService(), new NullLoggingService());

    private sealed class RecordingExternalFollowUpService : IExternalFollowUpService
    {
        public List<(string? Phone, int PageNumber, int PageSize)> Requests { get; } = [];

        public ExternalFollowUpPatientPage Result { get; init; } = new(
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
}
