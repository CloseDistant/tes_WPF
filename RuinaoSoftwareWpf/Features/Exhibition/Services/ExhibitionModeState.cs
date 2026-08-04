namespace RuinaoSoftwareWpf.Features.Exhibition.Services;

public sealed class ExhibitionModeState : IExhibitionModeState
{
    public bool IsEnabled
    {
        get
        {
#if EXHIBITION
            return true;
#else
            return false;
#endif
        }
    }
}
