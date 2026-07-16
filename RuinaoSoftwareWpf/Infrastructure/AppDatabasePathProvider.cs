namespace RuinaoSoftwareWpf;

using System.IO;

internal static class AppDatabasePathProvider
{
    private const string DatabaseFileName = "ruinao_app.db";

    public static string MainDatabasePath
    {
        get
        {
            var configuredDirectory = Environment.GetEnvironmentVariable("RUINAO_DATA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(configuredDirectory))
            {
                return Path.Combine(Path.GetFullPath(configuredDirectory), DatabaseFileName);
            }

            var root = FindWorkspaceRoot() ?? AppContext.BaseDirectory;
            return Path.Combine(root, "data", DatabaseFileName);
        }
    }

    public static string PatientKeyPath => Path.Combine(
        Path.GetDirectoryName(MainDatabasePath)!,
        "security",
        "patient.key");

    private static string? FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ruinao.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
