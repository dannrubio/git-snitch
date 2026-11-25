using System.Text.Json;
using GitSnitch.CommandLine;
using GitSnitch.Formatters;
using GitSnitch.Models;
using GitSnitch.Services;
using Xunit;

namespace GitSnitch.Tests;

public sealed class Revision16Tests
{
    [Fact]
    public void DefaultFormat_IsText() => Assert.Equal(OutputFormat.Text, CliArgumentParser.Parse(new[] { "file.cs" }).Format);

    [Fact]
    public void Parser_PreservesOptions()
    {
        var options = CliArgumentParser.Parse(new[] { "--format", "json", "--author", "Alice", "--sort", "lines", "--commits", "-w", "--", "-file.cs" });
        Assert.Equal(OutputFormat.Json, options.Format);
        Assert.Equal("Alice", options.AuthorFilter);
        Assert.Equal("lines", options.SortBy);
        Assert.Equal("-file.cs", options.FilePath);
        Assert.True(options.IncludeCommits);
        Assert.True(options.IgnoreWhitespace);
    }

    [Theory]
    [InlineData("xml", "--format must be text or json.")]
    [InlineData("--commits", "Missing value for --format.")]
    public void InvalidFormat_IsRejected(string value, string message) =>
        Assert.Equal(message, Assert.Throws<ArgumentException>(() => CliArgumentParser.Parse(new[] { "--format", value, "file.cs" })).Message);

    [Fact]
    public void Analysis_FiltersGroupsAndOrdersLines()
    {
        var result = BlameAnalysisService.Analyze(new BlameLine[]
        {
            new("Alice", 3, "third", "abc"), new("Bob", 2, "second", "def"), new("ALICE", 1, "first", "abc")
        }, new CliOptions { FilePath = "file.cs", AuthorFilter = "ali" });
        var group = Assert.Single(result.Groups);
        Assert.Equal("Alice", group.Author);
        Assert.Equal(new[] { 1, 3 }, group.Lines.Select(x => x.LineNumber));
        Assert.Equal(2, result.TotalLines);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Json_UsesVersionedContractAndOptionalFullCommit(bool commits)
    {
        const string hash = "0123456789012345678901234567890123456789";
        const string code = "\"quoted\" \\ 日本語";
        var options = new CliOptions { FilePath = "file.cs", IncludeCommits = commits };
        var result = BlameAnalysisService.Analyze(new[] { new BlameLine("Alice", 1, code, hash) }, options);
        using var writer = new StringWriter();
        await new JsonOutputFormatter().WriteAsync(result, options, writer);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("totalAuthors").GetInt32());
        Assert.Equal(1, root.GetProperty("totalLines").GetInt32());
        var group = root.GetProperty("groups")[0];
        Assert.Equal(1, group.GetProperty("lineCount").GetInt32());
        var line = group.GetProperty("lines")[0];
        Assert.Equal(code, line.GetProperty("code").GetString());
        Assert.Equal(commits, line.TryGetProperty("commit", out var commit));
        if (commits) Assert.Equal(hash, commit.GetString());
    }

    [Fact]
    public async Task Json_EmptyResultsAreValid()
    {
        using var writer = new StringWriter();
        await new JsonOutputFormatter().WriteAsync(new AnalysisResult("file.cs", Array.Empty<AuthorGroup>()), new CliOptions(), writer);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("groups").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("totalLines").GetInt32());
    }

    [Fact]
    public async Task Text_PreservesLegacyLayout()
    {
        var options = new CliOptions { FilePath = "file.cs", IncludeCommits = true };
        var result = BlameAnalysisService.Analyze(new[] { new BlameLine("Alice", 1, "hello", "0123456789") }, options);
        using var writer = new StringWriter();
        await new TextOutputFormatter().WriteAsync(result, options, writer);
        var nl = writer.NewLine;
        Assert.Equal($"Git Snitch: file.cs{nl}\nAuthor: Alice (1 lines){nl}{new string('-', 55)}{nl}    1 | hello [01234567]{nl}", writer.ToString());
    }
}
