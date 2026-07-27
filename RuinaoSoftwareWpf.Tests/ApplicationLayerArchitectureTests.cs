namespace RuinaoSoftwareWpf.Tests;

using System.Reflection;
using RuinaoSoftwareWpf.ApplicationContracts;
using Xunit;

public sealed class ApplicationLayerArchitectureTests
{
    private static readonly Type[] ApplicationInterfaces =
    [
        typeof(IUserDialogService),
        typeof(ISessionLifecycleCoordinator),
        typeof(IStimulationDeviceGateway),
        typeof(IAssessmentModule),
        typeof(ICaptureMediaService),
        typeof(IEegAcquisitionService)
    ];

    [Fact]
    public void ApplicationContracts_UseOnlyPureClrTypes()
    {
        var visited = new HashSet<Type>();
        foreach (var contract in ApplicationInterfaces)
        {
            AssertPureType(contract, visited);
        }
    }

    [Fact]
    public void ApplicationSource_DoesNotReferencePresentationOrInfrastructureTypes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceDirectories = new[]
        {
            Path.Combine(repositoryRoot, "RuinaoSoftwareWpf", "Application"),
            Path.Combine(repositoryRoot, "RuinaoSoftwareWpf", "Domain")
        };
        var forbiddenTokens = new[]
        {
            "System.Windows",
            "OpenCvSharp",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.Data.Sqlite",
            "RuinaoSoftwareWpf.Views",
            "RuinaoSoftwareWpf.Infrastructure",
            "ObservableObject",
            "ObservableCollection",
            "ICommand",
            "DispatcherTimer",
            "DbContext"
        };

        var violations = sourceDirectories
            .Where(Directory.Exists)
            .SelectMany(directory =>
                Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return forbiddenTokens
                    .Where(token => source.Contains(token, StringComparison.Ordinal))
                    .Select(token =>
                        $"{Path.GetRelativePath(repositoryRoot, path)} -> {token}");
            })
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Application 不得引用 Presentation/Infrastructure：{string.Join(", ", violations)}");
    }

    [Fact]
    public void CompositionRoot_RegistersNewContractsAndLegacyAdapters()
    {
        var services = AppComposition.Services;

        Assert.NotNull(services.GetService(typeof(IStimulationDeviceGateway)));
        Assert.NotNull(services.GetService(typeof(ICaptureMediaService)));
        Assert.NotNull(services.GetService(typeof(IEegAcquisitionService)));

        Assert.NotNull(services.GetService(typeof(IHardwareService)));
        Assert.NotNull(services.GetService(typeof(ICaptureMediaRecorder)));
        Assert.NotNull(services.GetService(typeof(ILegacyEegAcquisitionService)));
    }

    [Fact]
    public void SessionCoordinator_DoesNotDependOnDialogService()
    {
        var constructorParameterTypes = typeof(SessionLifecycleCoordinator)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IUserDialogService), constructorParameterTypes);
    }

    private static void AssertPureType(Type type, HashSet<Type> visited)
    {
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            AssertPureType(type.GetElementType()!, visited);
            return;
        }

        if (type.IsGenericParameter)
        {
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                AssertPureType(argument, visited);
            }
        }

        var typeNamespace = type.Namespace ?? string.Empty;
        Assert.False(
            typeNamespace.StartsWith("System.Windows", StringComparison.Ordinal)
            || typeNamespace.StartsWith("OpenCvSharp", StringComparison.Ordinal)
            || typeNamespace.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || typeNamespace.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal)
            || typeNamespace.StartsWith("RuinaoSoftwareWpf.Views", StringComparison.Ordinal)
            || typeNamespace.StartsWith("RuinaoSoftwareWpf.Infrastructure", StringComparison.Ordinal),
            $"应用层契约泄漏了禁止类型：{type.FullName}");

        if (!visited.Add(type)
            || type.Assembly != typeof(AppComposition).Assembly
            || typeNamespace != "RuinaoSoftwareWpf"
                && typeNamespace != "RuinaoSoftwareWpf.ApplicationContracts")
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            AssertPureType(property.PropertyType, visited);
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(method => !method.IsSpecialName))
        {
            AssertPureType(method.ReturnType, visited);
            foreach (var parameter in method.GetParameters())
            {
                AssertPureType(parameter.ParameterType, visited);
            }
        }

        foreach (var eventInfo in type.GetEvents(BindingFlags.Public | BindingFlags.Instance))
        {
            AssertPureType(eventInfo.EventHandlerType!, visited);
        }
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
