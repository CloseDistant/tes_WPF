namespace RuinaoHardwareDebugWpf;

public static class InterlockedExtensions
{
    public static long Max(ref long location, long value)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (value <= current)
            {
                return current;
            }

            if (Interlocked.CompareExchange(ref location, value, current) == current)
            {
                return value;
            }
        }
    }
}
