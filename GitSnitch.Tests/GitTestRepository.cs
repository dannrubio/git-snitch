using System.Diagnostics;
using System.ComponentModel;
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
            // Do not mask the original failure with a cleanup exception.
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);

                using var cleanupDeadline = new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanupDeadline.Token);

                // Drain both redirected streams before disposing the process.
                await Task.WhenAll(stdout, stderr).WaitAsync(cleanupDeadline.Token);
            }
            catch (Exception cleanupError) when (
                cleanupError is InvalidOperationException or Win32Exception or
                IOException or OperationCanceledException or TimeoutException)
            {
                Trace.WriteLine($"Test process cleanup failed: {cleanupError}");
                _ = Task.WhenAll(stdout, stderr).ContinueWith(
                    t => Trace.WriteLine(t.Exception),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted |
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        const int maxAttempts = 10;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(Root))
                    return;

                ClearReadOnlyAttributes(Root);

                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (Exception ex) when (
                (ex is IOException ||
                ex is UnauthorizedAccessException) &&
                attempt < maxAttempts)
            {
                await Task.Delay(200);
            }
        }
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(
            directory, "*", SearchOption.AllDirectories))
        {
            var attributes = System.IO.File.GetAttributes(file);

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                System.IO.File.SetAttributes(
                    file,
                    attributes & ~FileAttributes.ReadOnly);
            }
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(
            directory, "*", SearchOption.AllDirectories))
        {
            var attributes = System.IO.File.GetAttributes(subdirectory);

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                System.IO.File.SetAttributes(
                    subdirectory,
                    attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}

internal sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
