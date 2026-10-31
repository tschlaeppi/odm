using System.IO.Compression;

namespace Odm.Modules.Software.Application;

public static class PsadtZip
{
    private static readonly string[] Scripts = ["Invoke-AppDeployToolkit.ps1", "Deploy-Application.ps1"];

    /// <summary>Checks whether the ZIP is a PSADT package (script in the root directory or one level below).</summary>
    public static string? Validate(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var found = zip.Entries.Any(e =>
            {
                var name = e.FullName.Replace('\\', '/');
                var depth = name.Count(c => c == '/');
                return depth <= 1 && Scripts.Contains(Path.GetFileName(name), StringComparer.OrdinalIgnoreCase);
            });

            return found
                ? null
                : "The ZIP contains no PSADT script (Invoke-AppDeployToolkit.ps1 or Deploy-Application.ps1).";
        }
        catch (InvalidDataException)
        {
            return "The file is not a valid ZIP archive.";
        }
    }
}
