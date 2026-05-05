using System.Text;

namespace LiveTotalsHelper.Infrastructure.SofaScore;

public static class FileNameSanitizer
{
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var builder = new StringBuilder(value.Length);
        bool previousWasSeparator = false;

        foreach (char ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        string result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }
}
