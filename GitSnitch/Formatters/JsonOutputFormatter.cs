using System.Text.Json;
using System.Text.Json.Serialization;
using GitSnitch.CommandLine;
using GitSnitch.Models;

namespace GitSnitch.Formatters;

public sealed class JsonOutputFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task WriteAsync(AnalysisResult result, CliOptions options, TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = new
        {
            result.SchemaVersion, result.File, result.TotalLines, result.TotalAuthors,
            Groups = result.Groups.Select(g => new
            {
                g.Author, g.LineCount,
                Lines = g.Lines.Select(line => new
                {
                    line.LineNumber, line.Code,
                    Commit = options.IncludeCommits ? line.Commit : null
                })
            })
        };
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}
