namespace RuinaoSoftwareWpf;

public interface IUserViewModeService
{
    UserViewMode CurrentMode { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SetModeAsync(UserViewMode mode, CancellationToken cancellationToken = default);
}
