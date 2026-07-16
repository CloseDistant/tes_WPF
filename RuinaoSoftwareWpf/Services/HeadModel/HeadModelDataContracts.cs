namespace RuinaoSoftwareWpf;

using System.Numerics;

public enum HeadModelLayer
{
    Scalp,
    Skull,
    CerebrospinalFluid,
    GrayMatter,
    WhiteMatter,
    Electrodes,
    ElectricField
}

public enum MeshLod
{
    Preview,
    Interactive,
    Full
}

public sealed record HeadMeshData(
    HeadModelLayer Layer,
    MeshLod Lod,
    Vector3[] Positions,
    Vector3[] Normals,
    int[] Indices,
    long EstimatedGpuBytes);

public interface IHeadModelDataService
{
    Task<HeadMeshData> LoadAsync(
        string modelDirectory,
        HeadModelLayer layer,
        MeshLod lod,
        CancellationToken cancellationToken = default);

    void ClearCache(string? modelDirectory = null);
}
