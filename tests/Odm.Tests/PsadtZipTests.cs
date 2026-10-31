using System.IO.Compression;
using Odm.Modules.Software.Application;

namespace Odm.Tests;

public class PsadtZipTests
{
    private static string Zip(params string[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            zip.CreateEntry(entry);
        }

        return path;
    }

    [Fact]
    public void Linux_packages_need_the_v4_script()
    {
        var v3 = Zip("Deploy-Application.ps1");
        var v4 = Zip("App/Invoke-AppDeployToolkit.ps1");
        try
        {
            Assert.Null(PsadtZip.Validate(v3));
            Assert.NotNull(PsadtZip.Validate(v3, linux: true));
            Assert.Null(PsadtZip.Validate(v4, linux: true));
        }
        finally
        {
            File.Delete(v3);
            File.Delete(v4);
        }
    }
}
