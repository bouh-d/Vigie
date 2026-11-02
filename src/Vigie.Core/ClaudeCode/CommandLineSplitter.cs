using System.Text;

namespace Vigie.Core.ClaudeCode;

/// <summary>
/// Splits the "additional arguments" field into separate arguments so each one
/// can be passed on its own. Whitespace separates arguments, double quotes
/// group them, and a doubled quote inside quotes stands for a literal quote.
/// Backslashes have no special meaning, so Windows paths need no escaping.
/// </summary>
public static class CommandLineSplitter
{
    /// <returns>False when a quote is left open.</returns>
    public static bool TrySplit(string input, out IReadOnlyList<string> arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < input.Length && input[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (inQuotes)
        {
            arguments = [];
            return false;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        arguments = result;
        return true;
    }
}
