namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class PictureBrowseSequenceCatalogTests
{
    [Fact]
    public void Load_ValidatesAllVersionsAndPreservesPositionOrder()
    {
        using var directory = new TemporaryDirectory();
        var manifestPath = Path.Combine(directory.Path, PictureBrowseSequenceCatalog.ManifestFileName);
        var lines = new List<string>
        {
            "version|position|block6|fileName|valence|valenceCode"
        };

        foreach (var version in PictureBrowseSequenceCatalog.Versions)
        {
            for (var position = 1; position <= 30; position++)
            {
                var valenceType = ((position - 1) % 3) switch
                {
                    0 => ("正性", "positive"),
                    1 => ("中性", "neutral"),
                    _ => ("负性", "negative")
                };
                var fileName = $"{version}_{position:00}.png";
                File.WriteAllBytes(Path.Combine(directory.Path, fileName), [1, 2, 3]);
                lines.Add($"{version}|{position}|{((position - 1) / 6) + 1}|{fileName}|{valenceType.Item1}|{valenceType.Item2}");
            }
        }

        File.WriteAllLines(manifestPath, lines);

        var catalog = PictureBrowseSequenceCatalog.Load(manifestPath, directory.Path);

        Assert.Equal(30, catalog.Get("A").Count);
        Assert.Equal(1, catalog.Get("A")[0].Position);
        Assert.Equal(30, catalog.Get("D")[^1].Position);
        Assert.Equal(1, catalog.Get("A")[0].ValenceType);
        Assert.Equal(2, catalog.Get("A")[1].ValenceType);
    }

    [Fact]
    public void ResolveStableVersion_IsStableForTheSameRunAndPatient()
    {
        var first = PictureBrowseSequenceCatalog.ResolveStableVersion(42, "patient-01");

        Assert.Equal(first, PictureBrowseSequenceCatalog.ResolveStableVersion(42, "patient-01"));
        Assert.Contains(first, PictureBrowseSequenceCatalog.Versions);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ruinao-picture-browse-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
