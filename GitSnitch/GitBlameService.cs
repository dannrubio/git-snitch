
using System.Diagnostics;
using System.Text;
using GitSnitch.Models;

namespace GitSnitch;

public static class GitBlameService
{
    private static readonly TimeSpan DefaultTimeout =
        TimeSpan.FromSeconds(30);

    private const int MaxOutputChars = 16 * 1024 * 1024;
    private const int MaxErrorChars = 64 * 1024;

    public static async Task<IReadOnlyList<BlameLine>> ReadAsync(
        string file,
        bool ignoreWhitespace,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(file))
            throw new ArgumentException(
                "A file path is required.", nameof(file));

        var effectiveTimeout = timeout ?? DefaultTimeout;

        if (effectiveTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(timeout), "Timeout must be positive.");

        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        start.ArgumentList.Add("blame");
        start.ArgumentList.Add("--line-porcelain");

        if (ignoreWhitespace)
            start.ArgumentList.Add("-w");

        start.ArgumentList.Add("--");
        start.ArgumentList.Add(file);

        using var process = new Process
        {
            StartInfo = start
        };

        using var timeoutSource = new CancellationTokenSource(
            effectiveTimeout);

        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        var token = linkedSource.Token;

        bool started = false;
        Task<string>? stdoutTask = null;
        Task<string>? stderrTask = null;
        Task? exitTask = null;
        bool completedNormally = false;

        try
        {
            token.ThrowIfCancellationRequested();

            if (!process.Start())
                throw new InvalidOperationException("Unable to start Git.");

            started = true;
            stdoutTask = ReadBoundedAsync(
                process.StandardOutput, MaxOutputChars, token);
            stderrTask = ReadBoundedAsync(
                process.StandardError, MaxErrorChars, token);
            exitTask = process.WaitForExitAsync(token);

            // Surface a failed reader promptly; a stopped reader can block Git.
            var pending = new List<Task> { stdoutTask, stderrTask, exitTask };
            while (pending.Count > 0)
            {
                var completed = await Task.WhenAny(pending);
                await completed;
                pending.Remove(completed);
            }

            token.ThrowIfCancellationRequested();
            var output = await stdoutTask;
            var error = await stderrTask;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    !string.IsNullOrWhiteSpace(error)
                        ? error.Trim()
                        : $"git blame exited with code {process.ExitCode}.");

            var parsed = Parse(output, token);
            token.ThrowIfCancellationRequested();
            completedNormally = true;
            return parsed;
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested &&
                  !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Git blame timed out after {effectiveTimeout.TotalSeconds:g} seconds.");
        }
        finally
        {
            using var cleanupDeadline = new CancellationTokenSource(
                TimeSpan.FromSeconds(5));
            if (started && !completedNormally)
            {
                // Stop the producer before waiting for readers that may be
                // blocked on redirected stdout/stderr pipes.
                linkedSource.Cancel();
                await CleanupProcessAsync(process, cleanupDeadline.Token);
            }

            // Observe all reader/process task exceptions, even after an early
            // failure. Bound the wait so cleanup cannot hang indefinitely.
            var tasks = new Task?[] { stdoutTask, stderrTask, exitTask }
                .OfType<Task>().ToArray();
            if (tasks.Length > 0)
            {
                var observation = Task.WhenAll(tasks);
                try
                {
                    await observation.WaitAsync(cleanupDeadline.Token);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Trace.WriteLine($"Git task completion during cleanup: {ex}");
                    // Observe eventual failures if a task is still pending.
                    _ = observation.ContinueWith(
                        t => Trace.WriteLine(t.Exception),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted |
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];

        while (true)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(),
                cancellationToken);

            if (read == 0)
                break;

            if (builder.Length + read > maxChars)
                throw new InvalidOperationException(
                    "Git output exceeded the configured size limit.");

            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<BlameLine> Parse(string porcelain) =>
        Parse(porcelain, CancellationToken.None);

    private static IReadOnlyList<BlameLine> Parse(
        string porcelain,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(porcelain);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<BlameLine>();
        string author = "";
        string commit = "";
        int lineNumber = 0;
        bool hasHeader = false;
        bool hasAuthor = false;
        bool hasFilename = false;

        using var reader = new StringReader(porcelain);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryParseHeader(line, out var hash, out var number))
            {
                if (hasHeader)
                    throw new InvalidOperationException(
                        "Incomplete Git blame record: missing source line.");

                commit = hash;
                lineNumber = number;
                author = "";
                hasAuthor = false;
                hasFilename = false;
                hasHeader = true;
                continue;
            }

            if (!hasHeader)
                throw new InvalidOperationException(
                    $"Unexpected Git blame content outside a record: {line}");

            if (line.StartsWith('\t'))
            {
                if (!hasAuthor || !hasFilename)
                    throw new InvalidOperationException(
                        "Invalid Git blame record: missing author or filename metadata.");

                result.Add(new BlameLine(author, lineNumber, line[1..], commit));
                hasHeader = false;
                continue;
            }

            if (line.StartsWith("author ", StringComparison.Ordinal))
            {
                author = line[7..];
                hasAuthor = !string.IsNullOrWhiteSpace(author);
            }
            else if (line.StartsWith("filename ", StringComparison.Ordinal))
            {
                hasFilename = !string.IsNullOrWhiteSpace(line["filename ".Length..]);
            }
            else if (line.Equals("boundary", StringComparison.Ordinal))
            {
                // Git emits this standalone field for boundary commits.
            }
            else if (string.IsNullOrWhiteSpace(line) || !line.Contains(' '))
            {
                throw new InvalidOperationException(
                    $"Malformed Git blame metadata: {line}");
            }
            // Other porcelain metadata (author-time, summary, boundary, etc.)
            // is allowed; Git can add metadata fields over time.
        }

        if (hasHeader)
            throw new InvalidOperationException(
                "Incomplete Git blame record at end of output.");

        // Full-file blame must contain exactly the line numbers 1..N.
        var seen = new bool[result.Count];
        foreach (var item in result)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = item.LineNumber - 1;
            if (index < 0 || index >= seen.Length || seen[index])
                throw new InvalidOperationException(
                    "Git blame contains invalid, duplicate, or missing line numbers.");

            seen[index] = true;
        }

        return result;
    }

    private static bool TryParseHeader(
        string line,
        out string hash,
        out int finalLine)
    {
        hash = "";
        finalLine = 0;

        var parts = line.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is not (3 or 4) ||
            parts[0].Length is not (40 or 64))
            return false;

        if (!parts[0].All(Uri.IsHexDigit))
            return false;

        if (!int.TryParse(parts[1], out var originalLine) ||
            originalLine <= 0 ||
            !int.TryParse(parts[2], out finalLine) ||
            finalLine <= 0)
            return false;

        if (parts.Length == 4 &&
            (!int.TryParse(parts[3], out var count) ||
             count <= 0))
            return false;

        hash = parts[0];
        return true;
    }

    private static async Task CleanupProcessAsync(
        Process process, CancellationToken cleanupToken)
    {
        try
        {
            if (process.HasExited)
                return;

            process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync(cleanupToken);
        }
        catch (InvalidOperationException ex)
        {
            Trace.WriteLine($"Git process already exited or unavailable: {ex.Message}");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Trace.WriteLine($"Git process termination failed: {ex}");
        }
        catch (OperationCanceledException ex)
        {
            Trace.WriteLine($"Git process cleanup timed out: {ex}");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never replace the original failure with a cleanup exception.
            Trace.WriteLine($"Unexpected Git process cleanup failure: {ex}");
        }
    }
}
