namespace RuinaoSoftwareWpf.Tests;

using System.Windows;
using Xunit;

public sealed class AssessmentPatientPortalViewModelTests
{
    [Fact]
    public async Task SearchAsync_RequiresPhoneAndUsesExactMatch()
    {
        var service = new RecordingFollowUpService
        {
            Result = new ExternalFollowUpPatientPage(
                1, 10, 1, 2,
                [
                    new ExternalFollowUpPatient("RP", "正确", "中心", "B1", "1", "13800000000"),
                    new ExternalFollowUpPatient("RP", "相似", "中心", "B2", "2", "13800000001")
                ])
        };
        var viewModel = CreateViewModel(service);

        viewModel.PhoneQuery = " 13800000000 ";
        await viewModel.SearchAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(service.Searches);
        Assert.Equal("13800000000", request.Phone);
        Assert.NotNull(viewModel.Patient);
        Assert.Equal("正确", viewModel.Patient!.Name);
        Assert.Equal("13800000000", Assert.Single(service.DetailRequests));
    }

    [Fact]
    public async Task SearchAsync_WhenPhoneIsBlank_DoesNotCallServer()
    {
        var service = new RecordingFollowUpService();
        var viewModel = CreateViewModel(service);

        await viewModel.SearchAsync(TestContext.Current.CancellationToken);

        Assert.Empty(service.Searches);
        Assert.True(viewModel.HasError);
        Assert.False(viewModel.HasPatient);
    }

    [Fact]
    public void PhoneQuery_FiltersNonDigitsAndAllowsVariableLengthUpToTwentyDigits()
    {
        var viewModel = CreateViewModel(new RecordingFollowUpService());

        viewModel.PhoneQuery = "ab123456789012345678901-cd";

        Assert.Equal("12345678901234567890", viewModel.PhoneQuery);
    }

    private static AssessmentPatientPortalViewModel CreateViewModel(RecordingFollowUpService service) =>
        new(service, new NullPatientService(), new AppLocalizationService(), new NullLoggingService(), new NullToastService());

    private sealed class RecordingFollowUpService : IExternalFollowUpService
    {
        public ExternalFollowUpPatientPage Result { get; set; } = new(1, 100, 1, 0, []);
        public List<(string Phone, int Page, int Size)> Searches { get; } = [];
        public List<string> DetailRequests { get; } = [];

        public Task<ExternalFollowUpPatientPage> SearchPatientsAsync(
            string? phone,
            int pageNumber = 1,
            int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            Searches.Add((phone ?? string.Empty, pageNumber, pageSize));
            return Task.FromResult(Result with { PageNumber = pageNumber, PageSize = pageSize });
        }

        public Task<IReadOnlyList<ExternalFollowUpDetail>> GetFollowUpDetailsAsync(
            string phone,
            CancellationToken cancellationToken = default)
        {
            DetailRequests.Add(phone);
            return Task.FromResult<IReadOnlyList<ExternalFollowUpDetail>>([]);
        }
    }

    private sealed class NullPatientService : IPatientService
    {
        public event EventHandler? CurrentPatientChanged { add { } remove { } }
        public PatientRecord? CurrentPatient => null;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> GenerateNextPatientCodeAsync(CancellationToken cancellationToken = default) => Task.FromResult("P0");
        public Task<PatientRecord> CreatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PatientRecord> UpdatePatientAsync(PatientSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PageResult<PatientRecord>> GetPatientsPageAsync(PageRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PageResult<PatientRecord>([], false));
        public Task<PatientRecord> SwitchCurrentPatientAsync(string patientCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetRequiredCurrentPatientCodeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public Visibility Visibility => Visibility.Collapsed;
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
