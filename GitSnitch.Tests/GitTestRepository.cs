using System.Diagnostics;
using System.Text;

namespace GitSnitch.Tests;

/// <summary>A real, isolated Git repository with no dependency on global Git identity.</summary>
internal sealed class GitTestRepository : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "git-snitch-tests-" + Guid.NewGuid().ToString("N"));

    private GitTestRepository() { }

    public static async Task<GitTestRepository> CreateAsync()
    {
        var repo = new GitTestRepository();
        Directory.CreateDirectory(repo.Root);
        try
        {
            await repo.GitAsync("init", "--quiet");
            await repo.GitAsync("config", "user.name", "Test Author");
            await repo.GitAsync("config", "user.email", "test@example.invalid");
            await repo.GitAsync("config", "commit.gpgsign", "false");
            return repo;
        }
        catch
        {
            await repo.DisposeAsync();
            throw;
        }
    }

    public string File(string name) => Path.Combine(Root, name);

    public async Task WriteAndCommitAsync(string name, string content, string author = "Test Author")
    {
        var path = File(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await System.IO.File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
        await GitAsync("add", "--", name);
        await GitAsync("-c", $"user.name={author}", "commit", "--quiet", "-m", "test commit");
    }

    public Task<CommandResult> GitAsync(params string[] arguments) =>
        RunAsync("git", Root, arguments);

    public static async Task<CommandResult> RunAsync(
        string executable, string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException($"Unable to start {executable}.");

        // Read both pipes concurrently to avoid deadlock on verbose failures.
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            var result = new CommandResult(process.ExitCode, await stdout, await stderr);
            if (executable == "git" && result.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.Stderr}");
            return result;
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        // Windows can briefly retain handles after child process exit.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
                break;
            }
            catch (IOException) when (attempt < 4) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(100); }
        }
        return ValueTask.CompletedTask;
    }
}

internal sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
