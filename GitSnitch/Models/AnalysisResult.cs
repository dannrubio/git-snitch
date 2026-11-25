namespace GitSnitch.Models;

public sealed record AnalysisResult(string File, IReadOnlyList<AuthorGroup> Groups)
{
    public int SchemaVersion => 1;
    public int TotalLines => Groups.Sum(group => group.LineCount);
    public int TotalAuthors => Groups.Count;
}
