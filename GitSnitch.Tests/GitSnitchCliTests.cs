using GitSnitch;
using Xunit;

namespace GitSnitch.Tests;

public sealed class GitSnitchCliTests
{
    private static string ApplicationDll => typeof(GitBlameService).Assembly.Location;

    private static Task<CommandResult> CliAsync(string workingDirectory, params string[] arguments) =>
        GitTestRepository.RunAsync("dotnet", workingDirectory,
            new[] { ApplicationDll }.Concat(arguments).ToArray());

    [Fact]
    public async Task Help_ExitsZeroAndDisplaysUsage()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        var result = await CliAsync(repo.Root, "--help");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: git-snitch", result.Stdout);
        Assert.Equal("", result.Stderr);
    }

    [Theory]
    [InlineData("--sort", "invalid", "--sort must be name or lines")]
    [InlineData("--sort", "--commits", "Missing value for --sort")]
    [InlineData("--author", "-w", "Missing value for --author")]
    [InlineData("--unknown", "file.cs", "Unexpected argument")]
    public async Task InvalidArguments_ExitOneAndReportError(string first, string second, string message)
    {
        await using var repo = await GitTestRepository.CreateAsync();
        var result = await CliAsync(repo.Root, first, second);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Stderr);
    }

    [Fact]
    public async Task MissingFile_ExitsOne()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        var result = await CliAsync(repo.Root, "--commits");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("A file is required", result.Stderr);
    }

    [Fact]
    public async Task CommitsOption_PrintsCommitAndSource()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "hello world\n");
        var result = await CliAsync(repo.Root, "--commits", "sample.cs");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Author: Test Author (1 lines)", result.Stdout);
        Assert.Contains("hello world", result.Stdout);
        Assert.Matches(@"\[[a-fA-F0-9]{8}\]", result.Stdout);
    }

    [Fact]
    public async Task AuthorFilter_NoMatch_ExitsZeroWithMessage()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "hello\n");
        var result = await CliAsync(repo.Root, "--author", "Nobody", "sample.cs");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("No lines found for author: Nobody", result.Stdout);
    }

    [Fact]
    public async Task SortLinesAndEndOfOptions_ExitsZero()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("-special.cs", "hello\n");
        var result = await CliAsync(repo.Root, "--sort", "lines", "--", "-special.cs");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.Stdout);
    }

    [Fact]
    public async Task MissingGitFile_ExitsOneAndWritesStderr()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "hello\n");
        var result = await CliAsync(repo.Root, "missing.cs");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("git-snitch:", result.Stderr);
    }
}
