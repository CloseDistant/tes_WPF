namespace RuinaoSoftwareWpf;

internal sealed record ReleaseIntegrityResult(bool IsValid, string ErrorCode, int VerifiedFileCount = 0)
{
    public static ReleaseIntegrityResult Success { get; } = new(true, string.Empty);

    public static ReleaseIntegrityResult Failure(string errorCode) => new(false, errorCode);
}
