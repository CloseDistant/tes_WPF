namespace RuinaoSoftwareWpf;

using System.Numerics;

public sealed record HeadMeshData(
    HeadModelLayer Layer,
    MeshLod Lod,
    Vector3[] Positions,
    Vector3[] Normals,
    int[] Indices,
    long EstimatedGpuBytes);
