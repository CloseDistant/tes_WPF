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
                "83Y04",
                "wpf_package_E_palette_cropped_spinal_tail",
                "result-manifest.json"));

        return File.Exists(manifestPath) ? manifestPath : null;
    }
}
