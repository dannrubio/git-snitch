using GitSnitch.CommandLine;
using GitSnitch.Models;

namespace GitSnitch.Formatters;

public sealed class TextOutputFormatter : IOutputFormatter
{
    public async Task WriteAsync(AnalysisResult result, CliOptions options, TextWriter writer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await writer.WriteLineAsync($"Git Snitch: {result.File}");
        if (result.Groups.Count == 0)
        {
            await writer.WriteLineAsync(options.AuthorFilter is null
                ? "No attributable lines found." : $"No lines found for author: {options.AuthorFilter}");
            return;
        }
        foreach (var group in result.Groups)
        {
            await writer.WriteLineAsync($"\nAuthor: {group.Author} ({group.LineCount} lines)");
            await writer.WriteLineAsync(new string('-', 55));
            foreach (var line in group.Lines)
            {
                var suffix = options.IncludeCommits
                    ? $" [{line.Commit[..Math.Min(8, line.Commit.Length)]}]" : "";
                await writer.WriteLineAsync($"{line.LineNumber,5} | {line.Code}{suffix}");
            }
        }
    }
}
