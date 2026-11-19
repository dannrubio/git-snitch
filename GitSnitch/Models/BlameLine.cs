namespace GitSnitch.Models;

public sealed record BlameLine(string Author, int LineNumber, string Code, string Commit);
