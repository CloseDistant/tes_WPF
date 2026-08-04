namespace RuinaoSoftwareWpf;

using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Media3D;

public sealed record FemFieldPreview(Model3DGroup Model, int SourcePointCount, int RenderedPointCount, double MaximumValue);

/// <summary>Reads NumPy arrays directly from the verified FEM npz output and creates a colored WPF point-cloud preview.</summary>
public static class FemFieldPointCloudLoader
{
    private sealed record Sample(double X, double Y, double Z, double Value);

    public static Task<FemFieldPreview> LoadAsync(string npzPath, string? electrodeCsvPath = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(npzPath, electrodeCsvPath, cancellationToken), cancellationToken);

    private static FemFieldPreview Load(string npzPath, string? electrodeCsvPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(npzPath);
        var xyzEntry = archive.GetEntry("xyz.npy") ?? throw new InvalidDataException("FEM 结果缺少 xyz.npy。");
        var valueEntry = archive.GetEntry("values_ti_envelope.npy") ?? throw new InvalidDataException("FEM 结果缺少 values_ti_envelope.npy。");
        using var xyzReader = OpenNpy(xyzEntry, out var xyzCount);
        using var valueReader = OpenNpy(valueEntry, out var valueCount);
        var count = Math.Min(xyzCount, valueCount);
        // WPF Viewport3D is CPU-heavy; ~6k single-triangle particles keeps rotation responsive.
        var stride = Math.Max(1, count / 6_000);
        var samples = new List<Sample>(count / stride + 1);
        var minX = double.MaxValue; var minY = double.MaxValue; var minZ = double.MaxValue;
        var maxX = double.MinValue; var maxY = double.MinValue; var maxZ = double.MinValue; var maxValue = 0d;
        for (var index = 0; index < count; index++)
        {
            var x = xyzReader.ReadDouble(); var y = xyzReader.ReadDouble(); var z = xyzReader.ReadDouble(); var value = valueReader.ReadDouble();
            if (index % stride != 0) continue;
            if ((samples.Count & 0x7FF) == 0) cancellationToken.ThrowIfCancellationRequested();
            samples.Add(new Sample(x, y, z, value));
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); maxZ = Math.Max(maxZ, z); maxValue = Math.Max(maxValue, value);
        }

        var centerX = (minX + maxX) / 2; var centerY = (minY + maxY) / 2; var centerZ = (minZ + maxZ) / 2;
        var scale = 2.0 / Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        var colors = new[] { Color.FromRgb(49, 66, 180), Color.FromRgb(42, 135, 210), Color.FromRgb(38, 190, 175), Color.FromRgb(77, 210, 92), Color.FromRgb(232, 210, 44), Color.FromRgb(238, 74, 42) };
        var positions = colors.Select(_ => new Point3DCollection()).ToArray();
        var indices = colors.Select(_ => new Int32Collection()).ToArray();
        var radius = 0.007;
        foreach (var sample in samples)
        {
            var normalized = maxValue <= 0 ? 0 : Math.Clamp(sample.Value / maxValue, 0, 1);
            var bin = Math.Min(colors.Length - 1, (int)(normalized * colors.Length));
            var point = new Point3D((sample.X - centerX) * scale, (sample.Z - centerZ) * scale, (sample.Y - centerY) * scale);
            AddParticleTriangle(positions[bin], indices[bin], point, radius * (0.9 + normalized * 0.8));
        }

        var group = new Model3DGroup();
        for (var bin = 0; bin < colors.Length; bin++)
        {
            var mesh = new MeshGeometry3D { Positions = positions[bin], TriangleIndices = indices[bin] };
            var opacity = new[] { 0.14, 0.18, 0.25, 0.40, 0.70, 0.96 }[bin];
            var brush = new SolidColorBrush(colors[bin]) { Opacity = opacity }; brush.Freeze(); mesh.Freeze();
            var material = new DiffuseMaterial(brush); material.Freeze();
            group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
        if (!string.IsNullOrWhiteSpace(electrodeCsvPath) && File.Exists(electrodeCsvPath))
            AddElectrodeRings(group, electrodeCsvPath, centerX, centerY, centerZ, scale);
        group.Freeze();
        return new FemFieldPreview(group, count, samples.Count, maxValue);
    }

    private static BinaryReader OpenNpy(ZipArchiveEntry entry, out int count)
    {
        var reader = new BinaryReader(entry.Open());
        var magic = reader.ReadBytes(6);
        if (magic.Length != 6 || magic[0] != 0x93) throw new InvalidDataException($"{entry.Name} 不是 NPY 文件。");
        var major = reader.ReadByte(); reader.ReadByte();
        var headerLength = major <= 1 ? reader.ReadUInt16() : checked((int)reader.ReadUInt32());
        var header = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(headerLength));
        if (!header.Contains("'<f8'", StringComparison.Ordinal) && !header.Contains("\"<f8\"", StringComparison.Ordinal)) throw new NotSupportedException($"{entry.Name} 不是 little-endian float64。");
        var shapeStart = header.IndexOf("shape", StringComparison.Ordinal); var open = header.IndexOf('(', shapeStart); var comma = header.IndexOf(',', open);
        if (shapeStart < 0 || open < 0 || comma < 0 || !int.TryParse(header[(open + 1)..comma].Trim(), out count)) throw new InvalidDataException($"无法读取 {entry.Name} 的 shape。");
        return reader;
    }

    private static void AddParticleTriangle(Point3DCollection positions, Int32Collection indices, Point3D p, double r)
    {
        var start = positions.Count;
        positions.Add(new Point3D(p.X, p.Y + r, p.Z));
        positions.Add(new Point3D(p.X - r, p.Y - r, p.Z));
        positions.Add(new Point3D(p.X + r, p.Y - r, p.Z));
        indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
        positions.Add(new Point3D(p.X, p.Y + r, p.Z));
        positions.Add(new Point3D(p.X, p.Y - r, p.Z - r));
        positions.Add(new Point3D(p.X, p.Y - r, p.Z + r));
        indices.Add(start + 3); indices.Add(start + 4); indices.Add(start + 5);
    }

    private static void AddElectrodeRings(Model3DGroup group, string csvPath, double centerX, double centerY, double centerZ, double scale)
    {
        var regularPositions = new Point3DCollection(); var regularIndices = new Int32Collection();
        var activePositions = new Point3DCollection(); var activeIndices = new Int32Collection();
        var activeNames = new HashSet<string>(new[] { "F7", "T7", "T8", "P8" }, StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(csvPath))
        {
            var parts = line.Split(',');
            if (parts.Length < 5 || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) continue;
            var name = parts[4].Trim();
            var center = new Point3D((x - centerX) * scale, (z - centerZ) * scale, (y - centerY) * scale);
            var radial = new Vector3D(center.X, center.Y, center.Z);
            if (radial.LengthSquared < 0.001) continue;
            radial.Normalize(); center += radial * 0.025;
            if (activeNames.Contains(name)) AddRing(activePositions, activeIndices, center, radial, 0.060, 0.012);
            else AddRing(regularPositions, regularIndices, center, radial, 0.043, 0.005);
        }
        AddRingModel(group, regularPositions, regularIndices, Color.FromRgb(30, 48, 145), 0.78);
        AddRingModel(group, activePositions, activeIndices, Color.FromRgb(210, 20, 155), 0.98);
    }

    private static void AddRing(Point3DCollection positions, Int32Collection indices, Point3D center, Vector3D normal, double radius, double thickness)
    {
        var basisU = Vector3D.CrossProduct(normal, new Vector3D(0, 1, 0));
        if (basisU.LengthSquared < 0.01) basisU = Vector3D.CrossProduct(normal, new Vector3D(1, 0, 0));
        basisU.Normalize(); var basisV = Vector3D.CrossProduct(normal, basisU); basisV.Normalize();
        const int segments = 24;
        var start = positions.Count;
        for (var index = 0; index < segments; index++)
        {
            var angle = index * Math.PI * 2 / segments;
            var direction = basisU * Math.Cos(angle) + basisV * Math.Sin(angle);
            positions.Add(center + direction * (radius - thickness)); positions.Add(center + direction * (radius + thickness));
        }
        for (var index = 0; index < segments; index++)
        {
            var next = (index + 1) % segments; var a = start + index * 2; var b = a + 1; var c = start + next * 2; var d = c + 1;
            indices.Add(a); indices.Add(c); indices.Add(d); indices.Add(a); indices.Add(d); indices.Add(b);
        }
    }

    private static void AddRingModel(Model3DGroup group, Point3DCollection positions, Int32Collection indices, Color color, double opacity)
    {
        if (positions.Count == 0) return;
        var mesh = new MeshGeometry3D { Positions = positions, TriangleIndices = indices }; mesh.Freeze();
        var brush = new SolidColorBrush(color) { Opacity = opacity }; brush.Freeze();
        var material = new EmissiveMaterial(brush); material.Freeze();
        group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
    }
}
