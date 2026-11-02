using System.Text;

namespace Vigie.Core.Logging;

/// <summary>
/// Applies the French rule of a non-breaking space before the double
/// punctuation marks <c>: ; ! ?</c> and inside guillemets, so journal lines
/// written with ordinary spaces read correctly.
/// </summary>
public static class FrenchTypography
{
    private const char NoBreakSpace = ' ';

    public static string Fix(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c is ':' or ';' or '!' or '?' && i > 0 && builder[^1] == ' ')
            {
                builder[^1] = NoBreakSpace;
            }
            else if (c == '«' && i + 1 < text.Length && text[i + 1] == ' ')
            {
                builder.Append(c).Append(NoBreakSpace);
                i++;
                continue;
            }
            else if (c == '»' && builder.Length > 0 && builder[^1] == ' ')
            {
                builder[^1] = NoBreakSpace;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
