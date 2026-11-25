namespace GitSnitch.CommandLine;

public sealed record CliOptions
{
    public string FilePath { get; init; } = string.Empty;
    public string? AuthorFilter { get; init; }
    public string SortBy { get; init; } = "name";
    public bool IncludeCommits { get; init; }
    public bool IgnoreWhitespace { get; init; }
    public bool ShowHelp { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Text;
}
