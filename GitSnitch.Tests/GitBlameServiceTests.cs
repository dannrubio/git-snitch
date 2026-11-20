using GitSnitch;
using Xunit;

namespace GitSnitch.Tests;

public sealed class GitBlameServiceTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";

    private static string Record(int number, string code, bool boundary = false) =>
        $"{Hash} {number} {number} 1\n" +
        "author Test Author\n" +
        (boundary ? "boundary\n" : "") +
        "filename Program.cs\n" +
        $"\t{code}\n";

    [Fact]
    public void Parse_EmptyFile_ReturnsNoLines() =>
        Assert.Empty(GitBlameService.Parse(""));

    [Fact]
    public void Parse_BoundaryMetadata_AcceptsValidRecord()
    {
        var line = Assert.Single(GitBlameService.Parse(Record(1, "hello", true)));
        Assert.Equal("Test Author", line.Author);
        Assert.Equal("hello", line.Code);
        Assert.Equal(1, line.LineNumber);
    }

    [Fact]
    public void Parse_MultipleLines_ReturnsAllLines() =>
        Assert.Equal(2, GitBlameService.Parse(Record(1, "one") + Record(2, "two")).Count);

    [Fact]
    public void Parse_MissingAuthor_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            GitBlameService.Parse($"{Hash} 1 1 1\nfilename f\n\tcode\n"));

    [Fact]
    public void Parse_WhitespaceFilename_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            GitBlameService.Parse($"{Hash} 1 1 1\nauthor A\nfilename   \n\tcode\n"));

    [Fact]
    public void Parse_DuplicateLineNumber_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            GitBlameService.Parse(Record(1, "one") + Record(1, "two")));

    [Fact]
    public void Parse_GapInLineNumbers_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            GitBlameService.Parse(Record(1, "one") + Record(3, "three")));

    [Fact]
    public void Parse_UnexpectedContent_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            GitBlameService.Parse("not porcelain"));

    [Fact]
    public void Parse_Sha256CommitHash_AcceptsRecord()
    {
        var sha256 = new string('a', 64);
        var line = Assert.Single(GitBlameService.Parse(
            Record(1, "hello").Replace(Hash, sha256)));
        Assert.Equal(sha256, line.Commit);
    }

    [Fact]
    public void Parse_UnicodeAuthorAndEmbeddedTab_PreservesContent()
    {
        var porcelain = Record(1, "alpha\tbeta")
            .Replace("Test Author", "José 山田");
        var line = Assert.Single(GitBlameService.Parse(porcelain));
        Assert.Equal("José 山田", line.Author);
        Assert.Equal("alpha\tbeta", line.Code);
    }

    [Fact]
    public void Parse_TruncatedRecord_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            GitBlameService.Parse($"{Hash} 1 1 1\nauthor Test Author\nfilename Program.cs\n"));

    [Fact]
    public async Task ReadAsync_PreCancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GitBlameService.ReadAsync("Program.cs", false, cancellation.Token));
    }

    [Fact]
    public async Task ReadAsync_InvalidTimeout_Throws() =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            GitBlameService.ReadAsync("Program.cs", false, timeout: TimeSpan.Zero));
}
