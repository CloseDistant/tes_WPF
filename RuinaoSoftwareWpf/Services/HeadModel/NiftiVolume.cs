namespace RuinaoSoftwareWpf;

using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

/// <summary>Minimal NIfTI-1 reader used by the FEM workbench. Supports common scalar MRI/label datatypes.</summary>
public sealed class NiftiVolume
{
    private readonly float[] sourceVoxels;
    private readonly double[] sourceToWorld;
    private readonly double[] worldToSource;
    private readonly double canonicalOriginX, canonicalOriginY, canonicalOriginZ;

    private NiftiVolume(int sourceWidth, int sourceHeight, int sourceDepth, float voxelX, float voxelY, float voxelZ, float[] voxels, double[] voxelToWorld)
    {
        SourceWidth = sourceWidth; SourceHeight = sourceHeight; SourceDepth = sourceDepth;
        sourceVoxels = voxels;
        sourceToWorld = voxelToWorld;
        worldToSource = InvertAffine(voxelToWorld);

        // Build an isotropic, axis-aligned world grid. Slice generation then
        // produces true sagittal/coronal/axial planes even when the acquisition
        // sform is oblique (83Y04 is tilted about 18.7 degrees in Y/Z).
        var spacing = Math.Max(0.01f, Math.Min(voxelX, Math.Min(voxelY, voxelZ)));
        var minimum = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var maximum = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        foreach (var x in new[] { 0, sourceWidth - 1 })
        foreach (var y in new[] { 0, sourceHeight - 1 })
        foreach (var z in new[] { 0, sourceDepth - 1 })
        {
            var world = SourceVoxelToWorld(x, y, z);
            minimum[0] = Math.Min(minimum[0], world.X); maximum[0] = Math.Max(maximum[0], world.X);
            minimum[1] = Math.Min(minimum[1], world.Y); maximum[1] = Math.Max(maximum[1], world.Y);
            minimum[2] = Math.Min(minimum[2], world.Z); maximum[2] = Math.Max(maximum[2], world.Z);
        }

        canonicalOriginX = minimum[0]; canonicalOriginY = minimum[1]; canonicalOriginZ = minimum[2];
        Width = (int)Math.Ceiling((maximum[0] - minimum[0]) / spacing) + 1;
        Height = (int)Math.Ceiling((maximum[1] - minimum[1]) / spacing) + 1;
        Depth = (int)Math.Ceiling((maximum[2] - minimum[2]) / spacing) + 1;
        VoxelX = VoxelY = VoxelZ = spacing;
        var sampleStep = Math.Max(1, voxels.Length / 500_000);
        var ordered = voxels.Where((value, index) => index % sampleStep == 0 && float.IsFinite(value)).OrderBy(value => value).ToArray();
        if (ordered.Length == 0) { DisplayMinimum = 0; DisplayMaximum = 1; }
        else
        {
            DisplayMinimum = ordered[(int)((ordered.Length - 1) * 0.01)];
            DisplayMaximum = ordered[(int)((ordered.Length - 1) * 0.99)];
            if (DisplayMaximum <= DisplayMinimum) DisplayMaximum = DisplayMinimum + 1;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    internal int SourceWidth { get; }
    internal int SourceHeight { get; }
    internal int SourceDepth { get; }
    public float VoxelX { get; }
    public float VoxelY { get; }
    public float VoxelZ { get; }
    public float DisplayMinimum { get; }
    public float DisplayMaximum { get; }
    public string DimensionsText => $"{Width} × {Height} × {Depth}";
    public string VoxelSizeText => $"{VoxelX:0.###} × {VoxelY:0.###} × {VoxelZ:0.###} mm";

    public static Task<NiftiVolume> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(path, cancellationToken), cancellationToken);

    public BitmapSource CreateAxialSlice(int z) => CreateSlice(Width, Height, (x, y) => Get(x, Height - 1 - y, z));
    public BitmapSource CreateCoronalSlice(int y) => CreateSlice(Width, Depth, (x, z) => Get(x, y, Depth - 1 - z));
    public BitmapSource CreateSagittalSlice(int x) => CreateSlice(Height, Depth, (y, z) => Get(x, y, Depth - 1 - z));

    internal float GetVoxel(int x, int y, int z) => Get(x, y, z);

    internal (double X, double Y, double Z) ToWorld(int x, int y, int z) =>
        (canonicalOriginX + x * VoxelX, canonicalOriginY + y * VoxelY, canonicalOriginZ + z * VoxelZ);

    internal (double X, double Y, double Z) SourceVoxelToWorld(int x, int y, int z) =>
        (sourceToWorld[0] * x + sourceToWorld[1] * y + sourceToWorld[2] * z + sourceToWorld[3],
         sourceToWorld[4] * x + sourceToWorld[5] * y + sourceToWorld[6] * z + sourceToWorld[7],
         sourceToWorld[8] * x + sourceToWorld[9] * y + sourceToWorld[10] * z + sourceToWorld[11]);

    internal (double X, double Y, double Z) WorldToSourceVoxel(double x, double y, double z) =>
        (worldToSource[0] * x + worldToSource[1] * y + worldToSource[2] * z + worldToSource[3],
         worldToSource[4] * x + worldToSource[5] * y + worldToSource[6] * z + worldToSource[7],
         worldToSource[8] * x + worldToSource[9] * y + worldToSource[10] * z + worldToSource[11]);

    internal (double X, double Y, double Z) WorldToCanonicalVoxel(double x, double y, double z) =>
        ((x - canonicalOriginX) / VoxelX, (y - canonicalOriginY) / VoxelY, (z - canonicalOriginZ) / VoxelZ);

    internal float SampleWorld(double x, double y, double z)
    {
        var source = WorldToSourceVoxel(x, y, z);
        return SampleSource(source.X, source.Y, source.Z);
    }

    /// <summary>Builds a coarse threshold surface for interactive preview; this is not a segmented FEM mesh.</summary>
    public MeshGeometry3D CreatePreviewSurface(int samplingStep = 4)
    {
        samplingStep = Math.Clamp(samplingStep, 2, 12);
        var positions = new Point3DCollection();
        var indices = new Int32Collection();
        var threshold = DisplayMinimum + (DisplayMaximum - DisplayMinimum) * 0.16f;
        var scale = 2.0 / Math.Max(Width * VoxelX, Math.Max(Height * VoxelY, Depth * VoxelZ));

        bool Occupied(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Depth && Get(x, y, z) > threshold;
        Point3D Point(double x, double y, double z) => new(
            (x - Width / 2.0) * VoxelX * scale,
            (Depth / 2.0 - z) * VoxelZ * scale,
            (y - Height / 2.0) * VoxelY * scale);
        void Face(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            var start = positions.Count;
            positions.Add(a); positions.Add(b); positions.Add(c); positions.Add(d);
            indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            indices.Add(start); indices.Add(start + 2); indices.Add(start + 3);
        }

        for (var z = 0; z < Depth; z += samplingStep)
        for (var y = 0; y < Height; y += samplingStep)
        for (var x = 0; x < Width; x += samplingStep)
        {
            if (!Occupied(x, y, z)) continue;
            var x1 = Math.Min(Width, x + samplingStep); var y1 = Math.Min(Height, y + samplingStep); var z1 = Math.Min(Depth, z + samplingStep);
            if (!Occupied(x - samplingStep, y, z)) Face(Point(x, y, z), Point(x, y, z1), Point(x, y1, z1), Point(x, y1, z));
            if (!Occupied(x + samplingStep, y, z)) Face(Point(x1, y, z), Point(x1, y1, z), Point(x1, y1, z1), Point(x1, y, z1));
            if (!Occupied(x, y - samplingStep, z)) Face(Point(x, y, z), Point(x1, y, z), Point(x1, y, z1), Point(x, y, z1));
            if (!Occupied(x, y + samplingStep, z)) Face(Point(x, y1, z), Point(x, y1, z1), Point(x1, y1, z1), Point(x1, y1, z));
            if (!Occupied(x, y, z - samplingStep)) Face(Point(x, y, z), Point(x, y1, z), Point(x1, y1, z), Point(x1, y, z));
            if (!Occupied(x, y, z + samplingStep)) Face(Point(x, y, z1), Point(x1, y, z1), Point(x1, y1, z1), Point(x, y1, z1));
        }

        var mesh = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        mesh.Freeze();
        return mesh;
    }

    private float Get(int x, int y, int z)
    {
        var world = ToWorld(x, y, z);
        return SampleWorld(world.X, world.Y, world.Z);
    }

    private float SampleSource(double x, double y, double z)
    {
        const double tolerance = 1e-5;
        if (x < -tolerance || y < -tolerance || z < -tolerance ||
            x > SourceWidth - 1 + tolerance || y > SourceHeight - 1 + tolerance || z > SourceDepth - 1 + tolerance)
            return 0;

        x = Math.Clamp(x, 0, SourceWidth - 1); y = Math.Clamp(y, 0, SourceHeight - 1); z = Math.Clamp(z, 0, SourceDepth - 1);
        var x0 = (int)Math.Floor(x); var x1 = Math.Min(x0 + 1, SourceWidth - 1); var tx = (float)(x - x0);
        var y0 = (int)Math.Floor(y); var y1 = Math.Min(y0 + 1, SourceHeight - 1); var ty = (float)(y - y0);
        var z0 = (int)Math.Floor(z); var z1 = Math.Min(z0 + 1, SourceDepth - 1); var tz = (float)(z - z0);
        float At(int px, int py, int pz) => sourceVoxels[px + SourceWidth * (py + SourceHeight * pz)];
        var c00 = At(x0, y0, z0) * (1 - tx) + At(x1, y0, z0) * tx;
        var c10 = At(x0, y1, z0) * (1 - tx) + At(x1, y1, z0) * tx;
        var c01 = At(x0, y0, z1) * (1 - tx) + At(x1, y0, z1) * tx;
        var c11 = At(x0, y1, z1) * (1 - tx) + At(x1, y1, z1) * tx;
        return (c00 * (1 - ty) + c10 * ty) * (1 - tz) + (c01 * (1 - ty) + c11 * ty) * tz;
    }

    private BitmapSource CreateSlice(int width, int height, Func<int, int, float> sample)
    {
        var pixels = new byte[width * height];
        var scale = 255f / (DisplayMaximum - DisplayMinimum);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            pixels[x + y * width] = (byte)Math.Clamp((sample(x, y) - DisplayMinimum) * scale, 0, 255);

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        bitmap.Freeze();
        return bitmap;
    }

    private static NiftiVolume Load(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("NIfTI 文件不存在。", path);
        using var file = File.OpenRead(path);
        using Stream stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
        using var reader = new BinaryReader(stream);
        var header = reader.ReadBytes(348);
        if (header.Length != 348) throw new InvalidDataException("NIfTI 文件头不完整。");
        var littleEndian = BitConverter.ToInt32(header, 0) == 348;
        if (!littleEndian) throw new NotSupportedException("当前仅支持小端 NIfTI-1 数据。");
        var dimensions = BitConverter.ToInt16(header, 40);
        var width = BitConverter.ToInt16(header, 42);
        var height = BitConverter.ToInt16(header, 44);
        var depth = BitConverter.ToInt16(header, 46);
        if (dimensions < 3 || width <= 0 || height <= 0 || depth <= 0) throw new InvalidDataException("NIfTI 三维尺寸无效。");
        var datatype = BitConverter.ToInt16(header, 70);
        var voxelX = Math.Abs(BitConverter.ToSingle(header, 80));
        var voxelY = Math.Abs(BitConverter.ToSingle(header, 84));
        var voxelZ = Math.Abs(BitConverter.ToSingle(header, 88));
        var dataOffset = Math.Max(348, (int)BitConverter.ToSingle(header, 108));
        var slope = BitConverter.ToSingle(header, 112); if (!float.IsFinite(slope) || slope == 0) slope = 1;
        var intercept = BitConverter.ToSingle(header, 116); if (!float.IsFinite(intercept)) intercept = 0;
        var voxelToWorld = CreateVoxelToWorld(header, voxelX, voxelY, voxelZ);
        var remainingHeaderBytes = dataOffset - 348;
        if (remainingHeaderBytes > 0 && reader.ReadBytes(remainingHeaderBytes).Length != remainingHeaderBytes) throw new InvalidDataException("NIfTI 数据偏移超出文件长度。");
        var count = checked(width * height * depth);
        var values = new float[count];
        for (var index = 0; index < count; index++)
        {
            if ((index & 0x3FFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            var raw = datatype switch
            {
                2 => reader.ReadByte(),
                4 => reader.ReadInt16(),
                8 => reader.ReadInt32(),
                16 => reader.ReadSingle(),
                64 => (float)reader.ReadDouble(),
                256 => reader.ReadSByte(),
                512 => reader.ReadUInt16(),
                768 => reader.ReadUInt32(),
                _ => throw new NotSupportedException($"不支持 NIfTI datatype={datatype}。")
            };
            values[index] = raw * slope + intercept;
        }
        return new NiftiVolume(width, height, depth, voxelX, voxelY, voxelZ, values, voxelToWorld);
    }

    private static double[] CreateVoxelToWorld(byte[] header, float voxelX, float voxelY, float voxelZ)
    {
        if (BitConverter.ToInt16(header, 254) > 0)
        {
            var matrix = new double[12];
            for (var index = 0; index < matrix.Length; index++)
                matrix[index] = BitConverter.ToSingle(header, 280 + index * sizeof(float));
            return matrix;
        }

        if (BitConverter.ToInt16(header, 252) > 0)
        {
            var b = BitConverter.ToSingle(header, 256); var c = BitConverter.ToSingle(header, 260); var d = BitConverter.ToSingle(header, 264);
            var a = Math.Sqrt(Math.Max(0, 1.0 - b * b - c * c - d * d));
            var qfac = BitConverter.ToSingle(header, 76) < 0 ? -1.0 : 1.0;
            var dx = voxelX; var dy = voxelY; var dz = voxelZ * qfac;
            var ox = BitConverter.ToSingle(header, 268); var oy = BitConverter.ToSingle(header, 272); var oz = BitConverter.ToSingle(header, 276);
            return
            [
                (a*a+b*b-c*c-d*d)*dx, 2*(b*c-a*d)*dy, 2*(b*d+a*c)*dz, ox,
                2*(b*c+a*d)*dx, (a*a+c*c-b*b-d*d)*dy, 2*(c*d-a*b)*dz, oy,
                2*(b*d-a*c)*dx, 2*(c*d+a*b)*dy, (a*a+d*d-c*c-b*b)*dz, oz
            ];
        }

        return [voxelX, 0, 0, 0, 0, voxelY, 0, 0, 0, 0, voxelZ, 0];
    }

    private static double[] InvertAffine(double[] matrix)
    {
        var a = matrix[0]; var b = matrix[1]; var c = matrix[2];
        var d = matrix[4]; var e = matrix[5]; var f = matrix[6];
        var g = matrix[8]; var h = matrix[9]; var i = matrix[10];
        var determinant = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (Math.Abs(determinant) < 1e-12) throw new InvalidDataException("NIfTI 仿射矩阵不可逆。");
        var inverse = new[]
        {
            (e*i-f*h)/determinant, (c*h-b*i)/determinant, (b*f-c*e)/determinant, 0.0,
            (f*g-d*i)/determinant, (a*i-c*g)/determinant, (c*d-a*f)/determinant, 0.0,
            (d*h-e*g)/determinant, (b*g-a*h)/determinant, (a*e-b*d)/determinant, 0.0
        };
        var tx = matrix[3]; var ty = matrix[7]; var tz = matrix[11];
        inverse[3] = -(inverse[0] * tx + inverse[1] * ty + inverse[2] * tz);
        inverse[7] = -(inverse[4] * tx + inverse[5] * ty + inverse[6] * tz);
        inverse[11] = -(inverse[8] * tx + inverse[9] * ty + inverse[10] * tz);
        return inverse;
    }
}
