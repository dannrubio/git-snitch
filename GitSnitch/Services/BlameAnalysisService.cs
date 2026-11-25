using GitSnitch.CommandLine;
using GitSnitch.Models;

namespace GitSnitch.Services;

public static class BlameAnalysisService
{
    public static AnalysisResult Analyze(IReadOnlyList<BlameLine> lines, CliOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groups = lines.Where(x => options.AuthorFilter is null ||
                x.Author.Contains(options.AuthorFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Author, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AuthorGroup(g.Key, g.OrderBy(x => x.LineNumber).ToArray()));
        var ordered = options.SortBy == "lines"
            ? groups.OrderByDescending(g => g.LineCount).ThenBy(g => g.Author, StringComparer.OrdinalIgnoreCase)
            : groups.OrderBy(g => g.Author, StringComparer.OrdinalIgnoreCase);
        return new AnalysisResult(options.FilePath, ordered.ToArray());
    }
}
