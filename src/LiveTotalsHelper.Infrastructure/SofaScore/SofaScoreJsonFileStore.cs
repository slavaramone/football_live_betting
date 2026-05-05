using System.Text.Json;

namespace LiveTotalsHelper.Infrastructure.SofaScore;

public sealed class SofaScoreJsonFileStore
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<FileWriteResult> WriteJsonAsync(string path, string json, bool overwrite, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        if (File.Exists(path) && !overwrite)
            return FileWriteResult.Skipped(path);

        await File.WriteAllTextAsync(path, PrettyPrintJson(json), cancellationToken);
        return FileWriteResult.Written(path);
    }

    public async Task<FileWriteResult> WriteObjectAsync<T>(string path, T value, bool overwrite, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        if (File.Exists(path) && !overwrite)
            return FileWriteResult.Skipped(path);

        string json = JsonSerializer.Serialize(value, ManifestJsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return FileWriteResult.Written(path);
    }

    private static string PrettyPrintJson(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, ManifestJsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }
}

public sealed record FileWriteResult(string Path, bool WasWritten)
{
    public static FileWriteResult Written(string path) => new(path, true);
    public static FileWriteResult Skipped(string path) => new(path, false);
}
