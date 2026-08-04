using System.IO;

namespace RuinaoSoftwareWpf;

public sealed class BundledFemSampleResultLocator : IFemSampleResultLocator
{
    public string? FindBundledManifest()
    {
        var manifestPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "FemViewer",
                "data",
                "126426",
                "l2_v5_2d_legacy_3d",
                "result-manifest.json"));

        return File.Exists(manifestPath) ? manifestPath : null;
    }
}
