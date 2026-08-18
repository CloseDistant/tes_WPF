namespace RuinaoSoftwareWpf;

/// <summary>
/// 将评估入口的患者选择请求交给主界面现有患者窗口处理，同时保留异步完成语义。
/// </summary>
public sealed class AssessmentPatientSelectionRequestedEventArgs(
    CancellationToken cancellationToken) : EventArgs
{
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public Task Completion { get; set; } = Task.CompletedTask;

    public bool IsHandled { get; set; }
}
