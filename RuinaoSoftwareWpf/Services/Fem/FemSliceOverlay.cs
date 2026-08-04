namespace RuinaoSoftwareWpf;

using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>Compact TI-envelope field and bilateral-amygdala ROI used by the 2D FEM views.</summary>
public sealed class FemSliceOverlay
{
    private readonly ushort[] field;
    private readonly byte[] roiBits;
    private readonly int mriWidth, mriHeight, mriDepth;
    private readonly int fieldWidth, fieldHeight, fieldDepth;
    private readonly double originX, originY, originZ, spacingX, spacingY, spacingZ;

    private FemSliceOverlay(BinaryReader reader)
    {
        var magic = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));
        if (magic != "FEM2D01\0" || reader.ReadInt32() != 1)
            throw new InvalidDataException("不支持的 FEM 二维叠加数据格式。");

        mriWidth = reader.ReadInt32(); mriHeight = reader.ReadInt32(); mriDepth = reader.ReadInt32();
        fieldWidth = reader.ReadInt32(); fieldHeight = reader.ReadInt32(); fieldDepth = reader.ReadInt32();
        originX = reader.ReadDouble(); originY = reader.ReadDouble(); originZ = reader.ReadDouble();
        spacingX = reader.ReadDouble(); spacingY = reader.ReadDouble(); spacingZ = reader.ReadDouble();
        DisplayMaximum = reader.ReadSingle();
        DisplayMinimum = reader.ReadSingle();
        HighFieldThreshold = reader.ReadSingle();
        DefaultSagittalSlice = reader.ReadInt32();
        DefaultCoronalSlice = reader.ReadInt32();
        DefaultAxialSlice = reader.ReadInt32();

        var fieldCount = checked(fieldWidth * fieldHeight * fieldDepth);
        field = new ushort[fieldCount];
        for (var index = 0; index < fieldCount; index++) field[index] = reader.ReadUInt16();
        roiBits = reader.ReadBytes((checked(mriWidth * mriHeight * mriDepth) + 7) / 8);
        if (roiBits.Length != (mriWidth * mriHeight * mriDepth + 7) / 8)
            throw new InvalidDataException("FEM 二维叠加数据不完整。");
    }

    public float DisplayMaximum { get; }
    public float DisplayMinimum { get; }
    public float HighFieldThreshold { get; }
    public int DefaultSagittalSlice { get; }
    public int DefaultCoronalSlice { get; }
    public int DefaultAxialSlice { get; }

    public static Task<FemSliceOverlay> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            return new FemSliceOverlay(reader);
        }, cancellationToken);

    public bool Matches(NiftiVolume volume) =>
        volume.SourceWidth == mriWidth && volume.SourceHeight == mriHeight && volume.SourceDepth == mriDepth;

    public (int Sagittal, int Coronal, int Axial) GetDefaultSlices(NiftiVolume volume)
    {
        double sumX = 0, sumY = 0, sumZ = 0;
        var count = 0;
        for (var index = 0; index < mriWidth * mriHeight * mriDepth; index++)
        {
            if ((roiBits[index >> 3] & (1 << (index & 7))) == 0) continue;
            var x = index % mriWidth; var yz = index / mriWidth; var y = yz % mriHeight; var z = yz / mriHeight;
            var world = volume.SourceVoxelToWorld(x, y, z);
            sumX += world.X; sumY += world.Y; sumZ += world.Z; count++;
        }

        if (count == 0) return (volume.Width / 2, volume.Height / 2, volume.Depth / 2);
        var centerX = sumX / count; var centerY = sumY / count; var centerZ = sumZ / count;
        double leftSumX = 0; var leftCount = 0;
        for (var index = 0; index < mriWidth * mriHeight * mriDepth; index++)
        {
            if ((roiBits[index >> 3] & (1 << (index & 7))) == 0) continue;
            var x = index % mriWidth; var yz = index / mriWidth; var y = yz % mriHeight; var z = yz / mriHeight;
            var world = volume.SourceVoxelToWorld(x, y, z);
            if (world.X >= centerX) continue;
            leftSumX += world.X; leftCount++;
        }

        var sagittalWorldX = leftCount > 0 ? leftSumX / leftCount : centerX;
        var canonical = volume.WorldToCanonicalVoxel(sagittalWorldX, centerY, centerZ);
        return
        (
            Math.Clamp((int)Math.Round(canonical.X), 0, volume.Width - 1),
            Math.Clamp((int)Math.Round(canonical.Y), 0, volume.Height - 1),
            Math.Clamp((int)Math.Round(canonical.Z), 0, volume.Depth - 1)
        );
    }

    public BitmapSource CreateAxialSlice(NiftiVolume volume, int z) =>
        CreateSlice(volume, volume.Width, volume.Height, (x, y) => (x, volume.Height - 1 - y, z));

    public BitmapSource CreateCoronalSlice(NiftiVolume volume, int y) =>
        CreateSlice(volume, volume.Width, volume.Depth, (x, z) => (x, y, volume.Depth - 1 - z));

    public BitmapSource CreateSagittalSlice(NiftiVolume volume, int x) =>
        CreateSlice(volume, volume.Height, volume.Depth, (y, z) => (x, y, volume.Depth - 1 - z));

    private BitmapSource CreateSlice(NiftiVolume volume, int width, int height, Func<int, int, (int X, int Y, int Z)> coordinate)
    {
        var pixels = new byte[checked(width * height * 4)];
        var roi = new bool[width * height];
        var highField = new bool[width * height];
        var grayscaleScale = 255f / (volume.DisplayMaximum - volume.DisplayMinimum);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var voxel = coordinate(x, y);
            var world = volume.ToWorld(voxel.X, voxel.Y, voxel.Z);
            var gray = (byte)Math.Clamp((volume.SampleWorld(world.X, world.Y, world.Z) - volume.DisplayMinimum) * grayscaleScale, 0, 255);
            var value = SampleField(world);
            var pixelIndex = x + y * width;
            var byteIndex = pixelIndex * 4;
            var red = (float)gray; var green = (float)gray; var blue = (float)gray;

            if (value >= DisplayMinimum)
            {
                var t = Math.Clamp(value / DisplayMaximum, 0f, 1f);
                var color = MapColor(t);
                var alpha = 0.25f + 0.43f * MathF.Sqrt(t);
                blue = blue * (1 - alpha) + color.B * alpha;
                green = green * (1 - alpha) + color.G * alpha;
                red = red * (1 - alpha) + color.R * alpha;
            }

            pixels[byteIndex] = (byte)blue;
            pixels[byteIndex + 1] = (byte)green;
            pixels[byteIndex + 2] = (byte)red;
            pixels[byteIndex + 3] = 255;
            roi[pixelIndex] = IsRoi(volume, world);
            highField[pixelIndex] = value >= HighFieldThreshold;
        }

        DrawContour(pixels, roi, width, height, 36, 255, 80, 2);
        DrawContour(pixels, highField, width, height, 255, 45, 28, 1);

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private float SampleField((double X, double Y, double Z) world)
    {
        var gx = (world.X - originX) / spacingX;
        var gy = (world.Y - originY) / spacingY;
        var gz = (world.Z - originZ) / spacingZ;
        if (gx < 0 || gy < 0 || gz < 0 || gx > fieldWidth - 1 || gy > fieldHeight - 1 || gz > fieldDepth - 1) return 0;

        var x0 = Math.Min((int)gx, fieldWidth - 2); var tx = (float)(gx - x0);
        var y0 = Math.Min((int)gy, fieldHeight - 2); var ty = (float)(gy - y0);
        var z0 = Math.Min((int)gz, fieldDepth - 2); var tz = (float)(gz - z0);
        float At(int px, int py, int pz) => field[px + fieldWidth * (py + fieldHeight * pz)] / 65535f;
        var c00 = At(x0, y0, z0) * (1 - tx) + At(x0 + 1, y0, z0) * tx;
        var c10 = At(x0, y0 + 1, z0) * (1 - tx) + At(x0 + 1, y0 + 1, z0) * tx;
        var c01 = At(x0, y0, z0 + 1) * (1 - tx) + At(x0 + 1, y0, z0 + 1) * tx;
        var c11 = At(x0, y0 + 1, z0 + 1) * (1 - tx) + At(x0 + 1, y0 + 1, z0 + 1) * tx;
        return ((c00 * (1 - ty) + c10 * ty) * (1 - tz) + (c01 * (1 - ty) + c11 * ty) * tz) * DisplayMaximum;
    }

    private bool IsRoi(NiftiVolume volume, (double X, double Y, double Z) world)
    {
        var source = volume.WorldToSourceVoxel(world.X, world.Y, world.Z);
        var sourceX = (int)Math.Round(source.X); var sourceY = (int)Math.Round(source.Y); var sourceZ = (int)Math.Round(source.Z);
        if (sourceX < 0 || sourceY < 0 || sourceZ < 0 || sourceX >= mriWidth || sourceY >= mriHeight || sourceZ >= mriDepth) return false;
        var index = sourceX + mriWidth * (sourceY + mriHeight * sourceZ);
        return (roiBits[index >> 3] & (1 << (index & 7))) != 0;
    }

    private static void DrawContour(byte[] pixels, bool[] mask, int width, int height, byte red, byte green, byte blue, int radius)
    {
        var edges = new bool[mask.Length];
        for (var y = 1; y < height - 1; y++)
        for (var x = 1; x < width - 1; x++)
        {
            var index = x + y * width;
            if (mask[index] && (!mask[index - 1] || !mask[index + 1] || !mask[index - width] || !mask[index + width])) edges[index] = true;
        }

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var draw = false;
            for (var dy = -radius; dy <= radius && !draw; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var px = x + dx; var py = y + dy;
                if (px >= 0 && py >= 0 && px < width && py < height && edges[px + py * width]) { draw = true; break; }
            }
            if (!draw) continue;
            var offset = (x + y * width) * 4;
            pixels[offset] = blue; pixels[offset + 1] = green; pixels[offset + 2] = red; pixels[offset + 3] = 255;
        }
    }

    private static Color MapColor(float value)
    {
        ReadOnlySpan<(float Position, byte R, byte G, byte B)> stops =
        [
            (0.00f, 48, 34, 136), (0.18f, 42, 92, 210), (0.36f, 23, 190, 210),
            (0.54f, 63, 213, 102), (0.72f, 220, 229, 60), (0.87f, 255, 139, 34), (1.00f, 210, 30, 24)
        ];
        for (var index = 1; index < stops.Length; index++)
        {
            if (value > stops[index].Position) continue;
            var left = stops[index - 1]; var right = stops[index];
            var t = (value - left.Position) / (right.Position - left.Position);
            return Color.FromRgb((byte)(left.R + (right.R - left.R) * t), (byte)(left.G + (right.G - left.G) * t), (byte)(left.B + (right.B - left.B) * t));
        }
        return Color.FromRgb(210, 30, 24);
    }
}
