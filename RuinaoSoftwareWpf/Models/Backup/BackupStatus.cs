namespace RuinaoSoftwareWpf;

public sealed record BackupStatus(DateTimeOffset? LastBackupAt, string? LastBackupFileName);
