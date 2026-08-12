namespace RuinaoSoftwareWpf;

public sealed record IntegrityCheckProgress(string Stage, string CurrentItem, long Completed, long Total)
{
    public int Percentage => Total <= 0 ? 0 : (int)Math.Clamp(Completed * 100 / Total, 0, 100);
}
