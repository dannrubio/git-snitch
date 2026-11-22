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
            try
            {
                await repo.DisposeAsync();
            }
            catch (Exception cleanupError) when (cleanupError is not OutOfMemoryException)
            {
                Trace.WriteLine($"Repository initialization cleanup failed: {cleanupError}");
            }
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
        await GitAsync("commit", "--quiet", $"--author={author} <test@example.invalid>", "-m", "test commit");
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
        if (string.Equals(executable, "git", StringComparison.OrdinalIgnoreCase))
        {
            // Prevent machine-level identity, hooks and global configuration from
            // changing the outcome of integration tests.
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(
                workingDirectory, ".git-snitch-empty-global-config");
            start.Environment["GIT_AUTHOR_NAME"] = "Test Author";
            start.Environment["GIT_AUTHOR_EMAIL"] = "test@example.invalid";
            start.Environment["GIT_COMMITTER_NAME"] = "Test Author";
            start.Environment["GIT_COMMITTER_EMAIL"] = "test@example.invalid";
            // Remove inherited GIT_CONFIG_KEY_n / VALUE_n overrides as a group.
            foreach (var key in start.Environment.Keys
                .Where(k => k.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase) ||
                            k.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase))
                .ToArray())
                start.Environment.Remove(key);
            start.Environment.Remove("GIT_CONFIG_COUNT");
            start.Environment.Remove("GIT_CONFIG_KEY_0");
            start.Environment.Remove("GIT_CONFIG_VALUE_0");
            start.Environment.Remove("GIT_CONFIG_PARAMETERS");
            start.Environment.Remove("GIT_CONFIG");
            start.Environment.Remove("GIT_DIR");
            start.Environment.Remove("GIT_WORK_TREE");
            start.Environment.Remove("GIT_INDEX_FILE");
            start.Environment.Remove("GIT_COMMON_DIR");
            start.Environment.Remove("GIT_NAMESPACE");
            start.Environment.Remove("GIT_OBJECT_DIRECTORY");
            start.Environment.Remove("GIT_ALTERNATE_OBJECT_DIRECTORIES");
            start.Environment.Remove("GIT_AUTHOR_DATE");
            start.Environment.Remove("GIT_COMMITTER_DATE");
            // -c user.name in WriteAndCommitAsync controls the author.
        }
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException($"Unable to start {executable}.");

        // Read both pipes concurrently to avoid deadlock on verbose failures.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = ReadBoundedAsync(process.StandardOutput, 4 * 1024 * 1024, deadline.Token);
        var stderr = ReadBoundedAsync(process.StandardError, 256 * 1024, deadline.Token);
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

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return output.ToString();
            if (output.Length + read > maxChars)
                throw new InvalidOperationException("Test command output exceeded its size limit.");
            output.Append(buffer, 0, read);
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
