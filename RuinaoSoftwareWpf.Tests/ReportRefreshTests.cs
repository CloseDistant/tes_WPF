using System.Reflection;
using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class ReportRefreshTests
{
    [Fact]
    public async Task InitializeAsync_WhenRecordPageIsEnteredAgain_ReloadsTreatmentRecords()
    {
        var queryCount = 0;
        var recordService = InterfaceProxy<IStimulationRecordService>.Create((method, _) =>
        {
            if (method.Name == nameof(IStimulationRecordService.GetTreatmentRecordsPageAsync))
            {
                queryCount++;
                return Task.FromResult(new PageResult<StimulationTreatmentRecord>([], false, 0));
            }

            return DefaultReturnValue(method.ReturnType);
        });
        var viewModel = new ReportViewModel(
            recordService,
            InterfaceProxy<IUserDialogService>.Create(DefaultInvocation),
            InterfaceProxy<IAccountService>.Create(DefaultInvocation),
            InterfaceProxy<IAuthorizationService>.Create(DefaultInvocation),
            InterfaceProxy<IAuditTrailService>.Create(DefaultInvocation),
            InterfaceProxy<IAuditLogService>.Create(DefaultInvocation));

        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);
        await viewModel.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, queryCount);
    }

    private static object? DefaultInvocation(MethodInfo method, object?[]? arguments)
    {
        return DefaultReturnValue(method.ReturnType);
    }

    private static object? DefaultReturnValue(Type returnType)
    {
        if (returnType == typeof(void))
        {
            return null;
        }

        if (returnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var result = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, [result]);
        }

        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }

    private class InterfaceProxy<T> : DispatchProxy
        where T : class
    {
        private Func<MethodInfo, object?[]?, object?> invocation = DefaultInvocation;

        public static T Create(Func<MethodInfo, object?[]?, object?> invocation)
        {
            var proxy = Create<T, InterfaceProxy<T>>();
            ((InterfaceProxy<T>)(object)proxy).invocation = invocation;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return invocation(targetMethod!, args);
        }
    }
}
