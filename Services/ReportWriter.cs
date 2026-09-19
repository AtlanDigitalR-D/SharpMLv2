using System.Text.Json;
using SharpML2.Models;

namespace SharpML2.Services;

public sealed class ReportWriter : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private readonly StreamWriter _writer;

    public ReportWriter(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        _writer = new StreamWriter(fullPath, append: false);
    }

    public async Task WriteAsync(Finding finding, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(finding, JsonOptions);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _writer.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
    }
}
