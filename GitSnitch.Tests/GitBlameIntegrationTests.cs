using GitSnitch;
using Xunit;

namespace GitSnitch.Tests;

public sealed class GitBlameIntegrationTests
{
    [Fact]
    public async Task ReadAsync_CommittedFile_UsesRealGitMetadata()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "first\nsecond\n");

        var lines = await GitBlameService.ReadAsync(repo.File("sample.cs"), false);

        Assert.Equal(2, lines.Count);
        Assert.Equal(new[] { 1, 2 }, lines.Select(x => x.LineNumber));
        Assert.All(lines, x => Assert.Equal("Test Author", x.Author));
        Assert.Equal(new[] { "first", "second" }, lines.Select(x => x.Code));
        Assert.All(lines, x => Assert.Matches("^[a-fA-F0-9]{40,64}$", x.Commit));
    }

    [Fact]
    public async Task ReadAsync_MultipleCommits_AttributesEachLineToCorrectAuthor()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "alpha\nbeta\n", "Alice");
        await repo.WriteAndCommitAsync("sample.cs", "alpha\nbeta changed\n", "Bob");

        var lines = await GitBlameService.ReadAsync(repo.File("sample.cs"), false);

        Assert.Equal(2, lines.Count);
        Assert.Equal("Alice", lines[0].Author);
        Assert.Equal("Bob", lines[1].Author);
        Assert.NotEqual(lines[0].Commit, lines[1].Commit);
    }

    [Fact]
    public async Task ReadAsync_EmptyTrackedFile_ReturnsEmpty()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("empty.cs", "");

        Assert.Empty(await GitBlameService.ReadAsync(repo.File("empty.cs"), false));
    }

    [Fact]
    public async Task ReadAsync_UncommittedChanges_BlamesWorkingTreeContent()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "committed\n");
        await File.WriteAllTextAsync(repo.File("sample.cs"), "uncommitted\n");

        var line = Assert.Single(await GitBlameService.ReadAsync(repo.File("sample.cs"), false));
        Assert.Equal("uncommitted", line.Code);
        Assert.Equal("Not Committed Yet", line.Author);
    }

    [Fact]
    public async Task ReadAsync_AbsolutePathOutsideRepository_UsesTargetRepository()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("nested/sample.cs", "absolute path works\n");

        // xUnit's working directory is outside this independently created repository.
        Assert.False(Path.GetFullPath(Environment.CurrentDirectory).StartsWith(
            Path.GetFullPath(repo.Root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));

        var line = Assert.Single(await GitBlameService.ReadAsync(
            repo.File("nested/sample.cs"), false));
        Assert.Equal("Test Author", line.Author);
        Assert.Equal("absolute path works", line.Code);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_ThrowsGitError()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("existing.cs", "content\n");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GitBlameService.ReadAsync(repo.File("missing.cs"), false));
    }

    [Fact]
    public async Task ReadAsync_WhitespaceOption_UsesRealGit()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "one\ntwo\n");
        await repo.WriteAndCommitAsync("sample.cs", "one\n  two\n", "Bob");

        var lines = await GitBlameService.ReadAsync(repo.File("sample.cs"), true);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, x => Assert.Equal("Test Author", x.Author));
    }
}
