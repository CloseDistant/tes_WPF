namespace RuinaoSoftwareWpf;

public sealed record BackupLocationInfo(
    string? DirectoryPath,
    bool IsRemovable,
    long AvailableBytes,
    long EstimatedBytes,
    string Message);
