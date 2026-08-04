namespace RuinaoSoftwareWpf;

/// <summary>
/// 为Debug模拟联机提供稳定的界面阻抗快照。
/// 该边界不访问USB、不修改真实硬件快照，也不发送任何设备命令。
/// </summary>
public interface IDebugStimulationImpedanceProvider
{
    StimulationImpedanceSnapshot? GetSnapshot();
}
