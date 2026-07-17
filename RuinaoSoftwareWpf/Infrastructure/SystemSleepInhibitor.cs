namespace RuinaoSoftwareWpf;

using System.Runtime.InteropServices;

internal static class SystemSleepInhibitor
{
    public static bool TryEnable()
    {
        return SetThreadExecutionState(
            ExecutionState.Continuous | ExecutionState.SystemRequired) != 0;
    }

    public static void Disable()
    {
        _ = SetThreadExecutionState(ExecutionState.Continuous);
    }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState executionState);

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000
    }
}
