namespace RuinaoSoftwareWpf;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>
/// 读取图片浏览任务的固定 A-D 序列。
/// 素材与顺序是发布包的一部分，运行时只读取已打包的清单，不访问开发机路径，也不重新随机排序。
/// </summary>
internal sealed class PictureBrowseSequenceCatalog
{
    internal const string ManifestFileName = "picture-browse-sequences.csv";
    internal static readonly IReadOnlyList<string> Versions = ["A", "B", "C", "D"];

    private readonly IReadOnlyDictionary<string, IReadOnlyList<PictureBrowseSequenceItem>> sequences;

    private PictureBrowseSequenceCatalog(
        IReadOnlyDictionary<string, IReadOnlyList<PictureBrowseSequenceItem>> sequences)
    {
        this.sequences = sequences;
    }

    internal IReadOnlyList<PictureBrowseSequenceItem> Get(string version)
    {
        return sequences.TryGetValue(version, out var items)
            ? items
            : throw new InvalidOperationException($"图片浏览序列不存在：{version}");
    }

    internal static PictureBrowseSequenceCatalog Load(
        string manifestPath,
        string imageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageDirectory);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("未找到图片浏览顺序清单。", manifestPath);
        }

        var parsed = new Dictionary<string, List<PictureBrowseSequenceItem>>(StringComparer.OrdinalIgnoreCase);
        var lineNumber = 0;
        foreach (var rawLine in File.ReadLines(manifestPath))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("version|", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fields = line.Split('|');
            if (fields.Length != 6)
            {
                throw new InvalidDataException($"图片浏览清单第 {lineNumber} 行字段数错误。" );
            }

            var version = fields[0].Trim().ToUpperInvariant();
            if (!Versions.Contains(version, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"图片浏览清单第 {lineNumber} 行版本无效：{version}。" );
            }

            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
                || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var block6)
                || position is < 1 or > 30
                || block6 is < 1 or > 5)
            {
                throw new InvalidDataException($"图片浏览清单第 {lineNumber} 行位置或分块无效。" );
            }

            var fileName = fields[3].Trim();
            var valence = fields[4].Trim();
            var valenceCode = fields[5].Trim().ToLowerInvariant();
            var valenceType = valenceCode switch
            {
                "positive" => 1,
                "neutral" => 2,
                "negative" => 3,
                _ => 0
            };
            if (string.IsNullOrWhiteSpace(fileName) || valenceType == 0)
            {
                throw new InvalidDataException($"图片浏览清单第 {lineNumber} 行素材或效价无效。" );
            }

            var imagePath = Path.Combine(imageDirectory, fileName);
            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException($"图片浏览素材缺失：{fileName}。", imagePath);
            }

            if (!parsed.TryGetValue(version, out var items))
            {
                items = [];
                parsed.Add(version, items);
            }

            items.Add(new PictureBrowseSequenceItem(
                version,
                position,
                block6,
                fileName,
                valence,
                valenceType,
                imagePath));
        }

        if (parsed.Count != Versions.Count)
        {
            throw new InvalidDataException("图片浏览清单必须包含 A、B、C、D 四套序列。" );
        }

        var result = new Dictionary<string, IReadOnlyList<PictureBrowseSequenceItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in Versions)
        {
            if (!parsed.TryGetValue(version, out var items))
            {
                throw new InvalidDataException($"图片浏览清单缺少 {version} 套序列。" );
            }

            ValidateVersion(version, items);
            result.Add(version, items.OrderBy(static item => item.Position).ToArray());
        }

        return new PictureBrowseSequenceCatalog(result);
    }

    /// <summary>
    /// 用稳定运行编号做首次版本分配，保证重启或继续评估时仍然使用同一套序列。
    /// 运行编号来自数据库，因此不会因进程内 Random 状态变化而更换版本。
    /// </summary>
    internal static string ResolveStableVersion(long runId, string patientCode)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(patientCode);

        unchecked
        {
            ulong hash = (ulong)runId;
            foreach (var character in patientCode)
            {
                hash = (hash * 16777619UL) ^ character;
            }

            return Versions[(int)(hash % (ulong)Versions.Count)];
        }
    }

    private static void ValidateVersion(
        string version,
        IReadOnlyList<PictureBrowseSequenceItem> items)
    {
        if (items.Count != 30
            || items.Select(static item => item.Position).Distinct().Count() != 30
            || items.Select(static item => item.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 30)
        {
            throw new InvalidDataException($"图片浏览 {version} 套序列必须包含 1-30 且素材不重复。" );
        }

        var valenceCounts = items.GroupBy(static item => item.ValenceType)
            .ToDictionary(static group => group.Key, static group => group.Count());
        if (valenceCounts.GetValueOrDefault(1) != 10
            || valenceCounts.GetValueOrDefault(2) != 10
            || valenceCounts.GetValueOrDefault(3) != 10)
        {
            throw new InvalidDataException($"图片浏览 {version} 套序列必须正、中、负各 10 张。" );
        }

        foreach (var block in items.GroupBy(static item => item.Block6))
        {
            if (block.Count() != 6 || block.GroupBy(static item => item.ValenceType).Any(static group => group.Count() != 2))
            {
                throw new InvalidDataException($"图片浏览 {version} 套第 {block.Key} 个 6 张块必须按效价各 2 张。" );
            }
        }

        var consecutive = 0;
        var previousValence = 0;
        foreach (var item in items.OrderBy(static item => item.Position))
        {
            consecutive = item.ValenceType == previousValence ? consecutive + 1 : 1;
            if (consecutive > 2)
            {
                throw new InvalidDataException($"图片浏览 {version} 套存在超过 2 张的连续同效价。" );
            }

            previousValence = item.ValenceType;
        }
    }
}

internal sealed record PictureBrowseSequenceItem(
    string Version,
    int Position,
    int Block6,
    string FileName,
    string Valence,
    int ValenceType,
    string ImagePath);
