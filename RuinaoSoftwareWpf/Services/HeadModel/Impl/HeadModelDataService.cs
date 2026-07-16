namespace RuinaoSoftwareWpf;

using System.Collections.Concurrent;
using System.IO;
using System.Numerics;

public sealed class HeadModelDataService : IHeadModelDataService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<HeadMeshData>>> cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<HeadMeshData> LoadAsync(
        string modelDirectory,
        HeadModelLayer layer,
        MeshLod lod,
        CancellationToken cancellationToken = default)
    {
        var key = $"{Path.GetFullPath(modelDirectory)}|{layer}|{lod}";
        var lazy = cache.GetOrAdd(key, _ => new Lazy<Task<HeadMeshData>>(
            () => Task.Run(() => LoadMesh(modelDirectory, layer, lod, cancellationToken), cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    public void ClearCache(string? modelDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(modelDirectory))
        {
            cache.Clear();
            return;
        }

        var prefix = Path.GetFullPath(modelDirectory) + "|";
        foreach (var key in cache.Keys.Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            cache.TryRemove(key, out _);
        }
    }

    private static HeadMeshData LoadMesh(
        string modelDirectory,
        HeadModelLayer layer,
        MeshLod lod,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(modelDirectory, $"{layer}.{lod}.meshbin");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("未找到指定层级和 LOD 的头模型网格。", path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        var vertexCount = reader.ReadInt32();
        var indexCount = reader.ReadInt32();
        if (vertexCount < 0 || indexCount < 0 || vertexCount > 20_000_000 || indexCount > 60_000_000)
        {
            throw new InvalidDataException("头模型网格大小无效。");
        }

        var positions = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        for (var index = 0; index < vertexCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            positions[index] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            normals[index] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        var indices = new int[indexCount];
        for (var index = 0; index < indexCount; index++)
        {
            indices[index] = reader.ReadInt32();
        }

        var estimatedBytes = (long)vertexCount * sizeof(float) * 6 + (long)indexCount * sizeof(int);
        return new HeadMeshData(layer, lod, positions, normals, indices, estimatedBytes);
    }
}
