namespace RuinaoSoftwareWpf;

public enum StimulationSubpage
{
    TypeSelection,
    TemporalInterference,
    DirectCurrent,
    PulseCurrent
}

/// <summary>
/// 记录当前登录期间最后停留的电刺激子页面，不写入数据库或配置文件。
/// </summary>
public sealed class StimulationNavigationState
{
    public StimulationSubpage CurrentSubpage { get; private set; } = StimulationSubpage.TypeSelection;

    public void Remember(StimulationSubpage subpage)
    {
        CurrentSubpage = subpage;
    }

    public void Reset()
    {
        CurrentSubpage = StimulationSubpage.TypeSelection;
    }
}
