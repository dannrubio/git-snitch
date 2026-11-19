using System.Diagnostics;
using System.Text;
using GitSnitch.Models;

namespace GitSnitch;

public static class GitBlameService
{
    public static async Task<IReadOnlyList<BlameLine>> ReadAsync(string file, bool ignoreWhitespace)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("blame");
        start.ArgumentList.Add("--line-porcelain");
        if (ignoreWhitespace) start.ArgumentList.Add("-w");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(file);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Git.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var error = await stderrTask;
        var output = await outputTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim().Length > 0 ? error.Trim() : $"git blame exited with code {process.ExitCode}.");
        return Parse(output);
    }

    public static IReadOnlyList<BlameLine> Parse(string porcelain)
    {
        var result = new List<BlameLine>();
        string author = "Unknown", commit = "";
        int lineNumber = 0;
        using var reader = new StringReader(porcelain);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (TryParseHeader(line, out var hash, out var number))
            {
                commit = hash;
                lineNumber = number;
                author = "Unknown";
            }
            else if (line.StartsWith("author ", StringComparison.Ordinal))
                author = line[7..];
            else if (line.StartsWith('\t'))
                result.Add(new BlameLine(author, lineNumber, line[1..], commit));
        }
        return result;
    }

    private static bool TryParseHeader(string line, out string hash, out int finalLine)
    {
        hash = "";
        finalLine = 0;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4) || parts[0].Length is not (40 or 64)) return false;
        if (!parts[0].All(Uri.IsHexDigit)) return false;
        if (!int.TryParse(parts[1], out _) || !int.TryParse(parts[2], out finalLine)) return false;
        if (parts.Length == 4 && !int.TryParse(parts[3], out _)) return false;
        hash = parts[0];
        return true;
    }
}
