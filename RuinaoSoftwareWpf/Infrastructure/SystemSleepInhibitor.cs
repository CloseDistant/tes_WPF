namespace RuinaoSoftwareWpf;

using System.Runtime.InteropServices;

internal static class SystemSleepInhibitor
{
    internal static ExecutionState ActiveExecutionState =>
        ExecutionState.Continuous |
        ExecutionState.SystemRequired |
        ExecutionState.DisplayRequired;

    internal static ExecutionState ResetExecutionState => ExecutionState.Continuous;

    public static bool TryEnable()
    {
        return SetThreadExecutionState(ActiveExecutionState) != 0;
    }

    public static bool Disable()
    {
        return SetThreadExecutionState(ResetExecutionState) != 0;
    }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);

    [Flags]
    internal enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000
    }
}
