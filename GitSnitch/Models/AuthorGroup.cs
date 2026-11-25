namespace GitSnitch.Models;

public sealed record AuthorGroup(string Author, IReadOnlyList<BlameLine> Lines)
{
    public int LineCount => Lines.Count;
}
