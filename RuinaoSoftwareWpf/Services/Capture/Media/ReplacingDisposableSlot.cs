namespace RuinaoSoftwareWpf;

/// <summary>
/// 线程安全的单元素槽位。新值覆盖尚未消费的旧值，避免预览帧排队形成累计延迟。
/// </summary>
internal sealed class ReplacingDisposableSlot<T>
    where T : class, IDisposable
{
    private readonly object syncRoot = new();
    private T? current;

    public void Publish(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        T? replaced;
        lock (syncRoot)
        {
            replaced = current;
            current = value;
        }

        replaced?.Dispose();
    }

    public T? Take()
    {
        lock (syncRoot)
        {
            var value = current;
            current = null;
            return value;
        }
    }

    public void Clear() => Take()?.Dispose();
}
