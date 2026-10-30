using System.IO.Compression;

namespace Odm.Modules.Software.Application;

public static class PsadtZip
{
    private static readonly string[] Scripts = ["Invoke-AppDeployToolkit.ps1", "Deploy-Application.ps1"];

    /// <summary>Prüft, ob das ZIP ein PSADT-Paket ist (Skript im Stammverzeichnis oder eine Ebene darunter).</summary>
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
                : "Das ZIP enthält kein PSADT-Skript (Invoke-AppDeployToolkit.ps1 oder Deploy-Application.ps1).";
        }
        catch (InvalidDataException)
        {
            return "Die Datei ist kein gültiges ZIP-Archiv.";
        }
    }
}
