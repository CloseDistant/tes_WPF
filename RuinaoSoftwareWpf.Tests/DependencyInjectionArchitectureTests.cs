namespace RuinaoSoftwareWpf.Tests;

using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class DependencyInjectionArchitectureTests
{
    [Fact]
    public void CompositionRoot_BuildsWithValidationEnabled()
    {
        Assert.NotNull(AppComposition.Services);
    }

    [Fact]
    public void SingleWindowViewModels_HaveStableApplicationLifetime()
    {
        var services = AppComposition.Services;

        Assert.Same(
            services.GetRequiredService<MainViewModel>(),
            services.GetRequiredService<MainViewModel>());
        Assert.Same(
            services.GetRequiredService<LocalizationViewModel>(),
            services.GetRequiredService<LocalizationViewModel>());
        Assert.Same(
            services.GetRequiredService<ConfigViewModel>(),
            services.GetRequiredService<ConfigViewModel>());
    }

    [Fact]
    public void ProductionCode_OnlyStartupBoundariesAccessCompositionRoot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionDirectory = Path.Combine(repositoryRoot, "RuinaoSoftwareWpf");
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(Path.Combine(
                productionDirectory,
                "Infrastructure",
                "Composition",
                "AppComposition.cs"))
        };
        var violations = Directory
            .EnumerateFiles(productionDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !allowedFiles.Contains(Path.GetFullPath(path)))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("AppComposition.Services", StringComparison.Ordinal)
                    || source.Contains("IServiceProvider", StringComparison.Ordinal)
                    || source.Contains("GetService(", StringComparison.Ordinal)
                    || source.Contains("GetRequiredService", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"除启动边界外不得访问 Composition Root：{string.Join(", ", violations)}");
    }

    private static string FindRepositoryRoot()
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

        throw new DirectoryNotFoundException("未找到包含 Ruinao.slnx 的仓库根目录。");
    }
}
