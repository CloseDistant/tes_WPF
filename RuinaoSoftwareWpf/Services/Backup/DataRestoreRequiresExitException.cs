namespace RuinaoSoftwareWpf;

public sealed class DataRestoreRequiresExitException : InvalidOperationException
{
    public DataRestoreRequiresExitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
