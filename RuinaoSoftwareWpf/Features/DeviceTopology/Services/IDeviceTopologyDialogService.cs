namespace RuinaoSoftwareWpf;

/// <summary>设备拓扑诊断弹窗边界，避免主ViewModel直接依赖具体Window。</summary>
public interface IDeviceTopologyDialogService
{
    void Show();
}
