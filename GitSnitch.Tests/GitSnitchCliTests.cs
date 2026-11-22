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
    public async Task AbsolutePathFromOutsideRepository_ExitsZero()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("nested/sample.cs", "hello from another repository\n");

        var result = await CliAsync(Path.GetTempPath(), repo.File("nested/sample.cs"));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Test Author", result.Stdout);
        Assert.Contains("hello from another repository", result.Stdout);
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
    [Fact]
    public async Task AuthorFilter_MatchesCaseInsensitively()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "hello\n", "Alice");

        var result = await CliAsync(repo.Root, "--author", "aLiCe", "sample.cs");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Author: Alice (1 lines)", result.Stdout);
        Assert.Contains("hello", result.Stdout);
    }

    [Theory]
    [InlineData("name", "Alice", "Zoe")]
    [InlineData("lines", "Zoe", "Alice")]
    public async Task Sort_OrdersAuthorHeadings(string sort, string first, string second)
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "one\ntwo\nthree\n", "Zoe");
        await repo.WriteAndCommitAsync("sample.cs", "ONE\ntwo\nthree\n", "Alice");

        var result = await CliAsync(repo.Root, "--sort", sort, "sample.cs");

        Assert.Equal(0, result.ExitCode);
        var firstIndex = result.Stdout.IndexOf($"Author: {first} (", StringComparison.Ordinal);
        var secondIndex = result.Stdout.IndexOf($"Author: {second} (", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex, result.Stdout);
    }

    [Fact]
    public void ApplicationRuntimeConfiguration_Exists()
    {
        var runtimeConfig = Path.ChangeExtension(ApplicationDll, ".runtimeconfig.json");
        Assert.True(File.Exists(runtimeConfig),
            $"CLI runtime configuration is missing: {runtimeConfig}");
    }
}
