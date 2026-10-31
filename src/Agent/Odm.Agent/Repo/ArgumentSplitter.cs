using System.Text;

namespace Odm.Agent.Repo;

/// <summary>
/// Splits an argument line into single arguments like a shell would (spaces, '...' and "..."),
/// without starting a shell. Backslashes are kept (Windows paths); only \" counts as a quotation mark inside "...".
/// </summary>
public static class ArgumentSplitter
{
    public static IReadOnlyList<string> Split(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var current = new StringBuilder();
        var hasToken = false;
        var quote = '\0';

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quote == '\0')
            {
                if (char.IsWhiteSpace(c))
                {
                    if (hasToken)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                }
                else if (c is '"' or '\'')
                {
                    quote = c;
                    hasToken = true;
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }
            else if (c == quote)
            {
                quote = '\0';
            }
            else if (quote == '"' && c == '\\' && i + 1 < text.Length && text[i + 1] == '"')
            {
                current.Append('"');
                i++;
            }
            else
            {
                current.Append(c);
            }
        }

        if (quote != '\0')
        {
            throw new FormatException("A quotation mark in the arguments is not closed.");
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
