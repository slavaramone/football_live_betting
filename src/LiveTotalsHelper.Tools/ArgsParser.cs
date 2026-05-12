namespace LiveTotalsHelper.Tools;

public sealed class ParsedArgs
{
    private readonly Dictionary<string, string?> _values;

    public ParsedArgs(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public bool Has(string name) => _values.ContainsKey(Normalize(name));

    public IReadOnlyDictionary<string, string?> Values => _values;


    public string RequiredString(string name)
    {
        string key = Normalize(name);
        if (!_values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing required argument --{key}.");

        return value;
    }

    public string String(string name, string defaultValue)
    {
        string key = Normalize(name);
        return _values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    public int RequiredInt(string name)
    {
        string value = RequiredString(name);
        return int.TryParse(value, out int parsed)
            ? parsed
            : throw new ArgumentException($"Argument --{Normalize(name)} must be an integer.");
    }

    public int Int(string name, int defaultValue)
    {
        string key = Normalize(name);
        if (!_values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return int.TryParse(value, out int parsed)
            ? parsed
            : throw new ArgumentException($"Argument --{key} must be an integer.");
    }

    public double RequiredDouble(string name)
    {
        string value = RequiredString(name);
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new ArgumentException($"Argument --{Normalize(name)} must be a number.");
    }

    public double Double(string name, double defaultValue)
    {
        string key = Normalize(name);
        if (!_values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new ArgumentException($"Argument --{key} must be a number.");
    }

    public bool Bool(string name, bool defaultValue)
    {
        string key = Normalize(name);
        if (!_values.TryGetValue(key, out string? value))
            return defaultValue;

        if (value is null)
            return true;

        if (bool.TryParse(value, out bool parsed))
            return parsed;

        if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new ArgumentException($"Argument --{key} must be true or false.");
    }

    private static string Normalize(string name) => name.TrimStart('-').Trim().ToLowerInvariant();
}

public static class ArgsParser
{
    public static ParsedArgs Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected argument '{token}'. Arguments must use --name value format.");

            string key;
            string? value;
            int equalsIndex = token.IndexOf('=');
            if (equalsIndex > 2)
            {
                key = token[2..equalsIndex];
                value = token[(equalsIndex + 1)..];
            }
            else
            {
                key = token[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    value = args[++i];
                else
                    value = null;
            }

            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Empty argument name.");

            values[key.ToLowerInvariant()] = value;
        }

        return new ParsedArgs(values);
    }
}
