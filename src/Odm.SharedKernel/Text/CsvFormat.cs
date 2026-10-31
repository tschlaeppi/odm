using System.Text;

namespace Odm.SharedKernel.Text;

/// <summary>CSV output shared by the report endpoints.</summary>
public static class CsvFormat
{
    /// <summary>CSV cell; values starting with a formula character are neutralised (CSV injection in spreadsheets).</summary>
    public static string Cell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
        {
            text = "'" + text;
        }

        return text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    /// <summary>UTF-8 file content with BOM (so that Excel detects the encoding), header and rows separated by CRLF.</summary>
    public static byte[] Build(string[] header, IEnumerable<string?[]> rows)
    {
        var csv = new StringBuilder("﻿");
        csv.Append(string.Join(',', header.Select(Cell))).Append("\r\n");
        foreach (var row in rows)
        {
            csv.Append(string.Join(',', row.Select(Cell))).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(csv.ToString());
    }
}
