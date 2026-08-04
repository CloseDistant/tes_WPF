namespace RuinaoSoftwareWpf;

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// A verified, subject-specific visualization package exported by the local
/// CHARM-replacement/FEM pipeline. All payload paths must remain inside the
/// directory containing result-manifest.json.
/// </summary>
public sealed record FemResultPackage(
    string ManifestPath,
    string PackageDirectory,
    string SubjectId,
    string CoordinateSystem,
    string T1Path,
    string Field2DPath,
    string Field3DPath,
    string MetricsPath,
    string Field3DSha256,
    string ViewerMode)
{
    public static async Task<FemResultPackage> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullManifestPath))
            throw new FileNotFoundException("未找到 result-manifest.json。", fullManifestPath);

        await using var stream = File.OpenRead(fullManifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<ManifestContract>(
            stream,
            JsonOptions,
            cancellationToken);

        if (manifest is null)
            throw new InvalidDataException("结果清单为空或不是有效 JSON。");
        if (manifest.SchemaVersion is not (1 or 2))
            throw new InvalidDataException($"不支持的结果清单版本：{manifest.SchemaVersion}。");
        if (!string.Equals(manifest.Status, "PASS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"结果状态不是 PASS：{manifest.Status ?? "未声明"}。");
        if (manifest.SchemaVersion == 2 && manifest.CompatibilityGate?.Passed != true)
            throw new InvalidDataException("结果清单的 WPF v2 兼容性门控未通过。");
        if (string.IsNullOrWhiteSpace(manifest.SubjectId))
            throw new InvalidDataException("结果清单缺少 subject_id。");
        if (manifest.Files is null)
            throw new InvalidDataException("结果清单缺少 files。");
        var viewerMode = manifest.ViewerMode?.Trim() ?? "dynamic";
        if (viewerMode is not ("dynamic" or "official-static-83y04"))
            throw new InvalidDataException($"不支持的三维查看器模式：{viewerMode}。");
        if (viewerMode == "official-static-83y04" &&
            !string.Equals(manifest.SubjectId.Trim(), "83Y04", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("官方静态示例查看器只能用于固定受试者 83Y04。");
        }

        var packageDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidDataException("无法确定结果包目录。");

        var t1 = await ResolveAndVerifyAsync(
            packageDirectory,
            "T1",
            manifest.Files.T1,
            cancellationToken);
        var field2D = await ResolveAndVerifyAsync(
            packageDirectory,
            "二维有限元场",
            manifest.Files.Field2D,
            cancellationToken);
        var field3D = await ResolveAndVerifyAsync(
            packageDirectory,
            "三维有限元场",
            manifest.Files.Field3D,
            cancellationToken);
        var metrics = await ResolveAndVerifyAsync(
            packageDirectory,
            "指标",
            manifest.Files.Metrics,
            cancellationToken);
        if (manifest.SchemaVersion == 2)
        {
            _ = await ResolveAndVerifyAsync(
                packageDirectory,
                "计算网格摘要",
                manifest.Files.MeshSummary,
                cancellationToken);
        }

        return new FemResultPackage(
            fullManifestPath,
            packageDirectory,
            manifest.SubjectId.Trim(),
            manifest.CoordinateSystem?.Trim() ?? "未声明",
            t1,
            field2D,
            field3D,
            metrics,
            NormalizeSha256(manifest.Files.Field3D?.Sha256, "三维有限元场"),
            viewerMode);
    }

    private static async Task<string> ResolveAndVerifyAsync(
        string packageDirectory,
        string displayName,
        FileContract? contract,
        CancellationToken cancellationToken)
    {
        if (contract is null || string.IsNullOrWhiteSpace(contract.Path))
            throw new InvalidDataException($"结果清单缺少 {displayName} 文件。");

        if (Path.IsPathRooted(contract.Path))
            throw new InvalidDataException($"{displayName} 必须使用结果包内的相对路径。");

        var path = Path.GetFullPath(Path.Combine(packageDirectory, contract.Path));
        var relative = Path.GetRelativePath(packageDirectory, path);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException($"{displayName} 路径越出了结果包目录。");
        }

        if (!File.Exists(path))
            throw new FileNotFoundException($"结果包缺少 {displayName} 文件。", path);

        var expected = NormalizeSha256(contract.Sha256, displayName);
        await using var stream = File.OpenRead(path);
        var actualBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(actualBytes).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(actual)))
        {
            throw new InvalidDataException(
                $"{displayName} 的 SHA-256 校验失败，文件可能不完整或与清单不匹配。");
        }

        return path;
    }

    private static string NormalizeSha256(string? value, string displayName)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"{displayName} 缺少有效的 SHA-256。");
        }

        return normalized;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    private sealed record ManifestContract(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("subject_id")] string? SubjectId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("coordinate_system")] string? CoordinateSystem,
        [property: JsonPropertyName("viewer_mode")] string? ViewerMode,
        [property: JsonPropertyName("files")] FilesContract? Files,
        [property: JsonPropertyName("compatibility_gate")] CompatibilityGateContract? CompatibilityGate);

    private sealed record FilesContract(
        [property: JsonPropertyName("t1")] FileContract? T1,
        [property: JsonPropertyName("field_2d")] FileContract? Field2D,
        [property: JsonPropertyName("field_3d")] FileContract? Field3D,
        [property: JsonPropertyName("metrics")] FileContract? Metrics,
        [property: JsonPropertyName("mesh_summary")] FileContract? MeshSummary);

    private sealed record CompatibilityGateContract(
        [property: JsonPropertyName("passed")] bool Passed);

    private sealed record FileContract(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("sha256")] string? Sha256);
}
