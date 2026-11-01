using System.Text.RegularExpressions;

namespace Odm.Tests;

/// <summary>
/// Log messages, API messages, comments and scripts are English (the UI translates server messages). Catches the
/// obvious slips: German umlauts and a few common German words in source files and scripts.
/// </summary>
public class EnglishOnlyTests
{
    private static readonly Regex German = new(
        @"[äöüÄÖÜß]|\b(fehlt|fehlgeschlagen|nicht|konnte|konnten|wurde|erforderlich|gestartet|installiert|Fehler|Neustart|Auftrag|Gerät|Unbekannte[rs]?)\b",
        RegexOptions.Compiled);

    [Fact]
    public void Source_files_and_scripts_contain_no_german_text()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpenDeviceManager.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);

        var files = new[] { "src", "scripts", "deploy" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root!.FullName, d), "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".cs") || f.EndsWith(".sh") || f.EndsWith(".ps1") || f.EndsWith(".yml") || f.EndsWith(".yaml"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}") &&
                        !f.EndsWith("ModelSnapshot.cs") && !f.EndsWith(".Designer.cs"));

        var hits = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (File: Path.GetRelativePath(root!.FullName, f), Line: i + 1, Text: line)))
            .Where(x => German.IsMatch(x.Text))
            .Select(x => $"{x.File}:{x.Line}: {x.Text.Trim()}")
            .ToList();

        Assert.True(hits.Count == 0, "German text found:\n" + string.Join('\n', hits));
    }
}
