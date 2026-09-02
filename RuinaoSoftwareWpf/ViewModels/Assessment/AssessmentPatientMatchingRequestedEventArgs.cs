namespace RuinaoSoftwareWpf;

/// <summary>
/// 将评估入口的“匹配患者”请求交给外部患者/随访匹配页面处理。
/// 当前阶段只定义入口事件，具体接口查询和随访选择在匹配页面中实现。
/// </summary>
public sealed class AssessmentPatientMatchingRequestedEventArgs(
    CancellationToken cancellationToken) : EventArgs
{
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public Task Completion { get; set; } = Task.CompletedTask;

    public bool IsHandled { get; set; }
}
