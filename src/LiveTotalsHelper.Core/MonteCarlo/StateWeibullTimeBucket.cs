using System.Globalization;

namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class StateWeibullTimeBucket
{
    public StateWeibullTimeBucket(double startMinute, double endMinute)
    {
        if (startMinute < 0)
            throw new ArgumentOutOfRangeException(nameof(startMinute));
        if (endMinute <= startMinute)
            throw new ArgumentException("Time bucket end minute must be greater than start minute.");

        StartMinute = startMinute;
        EndMinute = endMinute;
        Key = $"{FormatBoundary(startMinute)}_{FormatBoundary(endMinute)}";
    }

    public string Key { get; }
    public double StartMinute { get; }
    public double EndMinute { get; }
    public double LengthMinutes => EndMinute - StartMinute;

    public bool Overlaps(double startMinute, double endMinute)
        => endMinute > StartMinute && startMinute < EndMinute;

    public static IReadOnlyList<StateWeibullTimeBucket> DefaultBuckets()
        =>
        [
            new(0, 15),
            new(15, 30),
            new(30, 45),
            new(45, 55),
            new(55, 65),
            new(65, 75),
            new(75, 85),
            new(85, 90),
            new(90, 96),
            new(96, 105)
        ];

    public static IReadOnlyList<StateWeibullTimeBucket> ParseList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultBuckets();

        var buckets = new List<StateWeibullTimeBucket>();
        foreach (string token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = token.Replace('_', '-');
            string[] parts = normalized.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double start) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double end))
                throw new ArgumentException($"Invalid time bucket '{token}'. Use '<start>-<end>', for example 45-55.");

            buckets.Add(new StateWeibullTimeBucket(start, end));
        }

        if (buckets.Count == 0)
            throw new ArgumentException("At least one time bucket is required.");

        return buckets.OrderBy(x => x.StartMinute).ThenBy(x => x.EndMinute).ToList();
    }

    private static string FormatBoundary(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', 'p');
}
