
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

        try
        {
            token.ThrowIfCancellationRequested();

            if (!process.Start())
                throw new InvalidOperationException(
                    "Unable to start Git.");

            started = true;    

            var stdoutTask = ReadBoundedAsync(
                process.StandardOutput,
                MaxOutputChars,
                token);

            var stderrTask = ReadBoundedAsync(
                process.StandardError,
                MaxErrorChars,
                token);

            var exitTask = process.WaitForExitAsync(token);

            await Task.WhenAll(
                stdoutTask,
                stderrTask,
                exitTask);

            var output = await stdoutTask;
            var error = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    !string.IsNullOrWhiteSpace(error)
                        ? error.Trim()
                        : $"git blame exited with code {process.ExitCode}.");
            }

            return Parse(output);
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
            if (started)
                await CleanupProcessAsync(process);
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

    public static IReadOnlyList<BlameLine> Parse(
        string porcelain)
    {
        var result = new List<BlameLine>();

        string author = "Unknown";
        string commit = "";
        int lineNumber = 0;
        bool hasHeader = false;
        bool hasAuthor = false;

        using var reader = new StringReader(porcelain);

        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (TryParseHeader(
                line,
                out var hash,
                out var number))
            {
                if (hasHeader)
                    throw new InvalidOperationException(
                        "Incomplete Git blame record.");

                commit = hash;
                lineNumber = number;
                author = "Unknown";
                hasAuthor = false;
                hasHeader = true;
            }
            else if (line.StartsWith(
                "author ",
                StringComparison.Ordinal))
            {
                if (hasHeader)
                {
                    author = line[7..];
                    hasAuthor = true;
                }
            }
            else if (line.StartsWith('\t'))
            {
                if (!hasHeader || !hasAuthor)
                    throw new InvalidOperationException(
                        "Invalid Git blame record: missing header or author.");

                result.Add(new BlameLine(
                    author,
                    lineNumber,
                    line[1..],
                    commit));

                hasHeader = false;
            }
        }

        if (hasHeader)
            throw new InvalidOperationException(
                "Incomplete Git blame record at end of output.");

        if (result.Any(x => x.LineNumber <= 0) ||
            result.Select(x => x.LineNumber).Distinct().Count()
                != result.Count)
        {
            throw new InvalidOperationException(
                "Git blame contains invalid or duplicate line numbers.");
        }

        var orderedNumbers = result
            .Select(x => x.LineNumber)
            .OrderBy(x => x)
            .ToArray();

        for (int i = 0; i < orderedNumbers.Length; i++)
        {
            if (orderedNumbers[i] != i + 1)
            {
                throw new InvalidOperationException(
                    "Git blame contains missing or out-of-order line numbers.");
            }
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

    private static async Task CleanupProcessAsync(Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            process.Kill(entireProcessTree: true);

            using var cleanupTimeout =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(5));

            await process.WaitForExitAsync(
                cleanupTimeout.Token);
        }
        catch (InvalidOperationException)
        {
            // Process already exited or is unavailable.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Process termination failed.
        }
        catch (OperationCanceledException)
        {
            // Cleanup timed out.
        }
    }
}
