namespace RuinaoSoftwareWpf;

public interface IHeadModelDataService
{
    Task<HeadMeshData> LoadAsync(
        string modelDirectory,
        HeadModelLayer layer,
        MeshLod lod,
        CancellationToken cancellationToken = default);

    void ClearCache(string? modelDirectory = null);
}
