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

            var documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documentsDirectory))
            {
                throw new InvalidOperationException("无法获取当前 Windows 用户的文档目录。");
            }

            return Path.Combine(documentsDirectory, "ruinao", "data", DatabaseFileName);
        }
    }

    public static string PatientKeyPath => Path.Combine(
        Path.GetDirectoryName(MainDatabasePath)!,
        "security",
        "patient.key");
}
