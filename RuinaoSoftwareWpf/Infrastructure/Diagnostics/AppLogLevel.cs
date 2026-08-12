namespace RuinaoSoftwareWpf;

/// <summary>
/// 应用日志级别。数值越小越详细，Release 构建会自动过滤低级别日志。
/// </summary>
public enum AppLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4
}
