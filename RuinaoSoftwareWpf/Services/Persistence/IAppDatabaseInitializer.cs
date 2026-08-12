namespace RuinaoSoftwareWpf;

public interface IAppDatabaseInitializer
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}
