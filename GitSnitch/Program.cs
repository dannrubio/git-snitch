using GitSnitch;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Usage: git snitch [--author NAME] [--sort name|lines] [--commits] [-w] <file>");
    Console.WriteLine("Group Git blame source lines by their last attributed author.");
    return;
}

string? file = null, authorFilter = null;
string sort = "name";
bool commits = false, ignoreWhitespace = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--author":
            if (++i >= args.Length) { Fail("Missing value for --author."); return; }
            authorFilter = args[i];
            break;
        case "--sort":
            if (++i >= args.Length) { Fail("Missing value for --sort."); return; }
            sort = args[i];
            if (sort is not ("name" or "lines")) { Fail("--sort must be name or lines."); return; }
            break;
        case "--commits": commits = true; break;
        case "-w": case "--ignore-whitespace": ignoreWhitespace = true; break;
        case "--":
            if (++i >= args.Length || file is not null || i != args.Length - 1)
            { Fail("Expected one file after --."); return; }
            file = args[i];
            break;
        default:
            if (args[i].StartsWith('-') || file is not null)
            { Fail($"Unexpected argument: {args[i]}"); return; }
            file = args[i];
            break;
    }
}

if (file is null) { Fail("A file is required."); return; }

try
{
    var lines = await GitBlameService.ReadAsync(file, ignoreWhitespace);
    var groups = lines
        .Where(x => authorFilter is null || x.Author.Contains(authorFilter, StringComparison.OrdinalIgnoreCase))
        .GroupBy(x => x.Author);
    var ordered = sort == "lines"
        ? groups.OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        : groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

    Console.WriteLine($"Git Snitch: {file}");
    foreach (var group in ordered)
    {
        Console.WriteLine($"\nAuthor: {group.Key} ({group.Count()} lines)");
        Console.WriteLine(new string('-', 55));
        foreach (var line in group.OrderBy(x => x.LineNumber))
        {
            var suffix = commits ? $" [{line.Commit[..Math.Min(8, line.Commit.Length)]}]" : "";
            Console.WriteLine($"{line.LineNumber,5} | {line.Code}{suffix}");
        }
    }
}
catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Fail(ex.Message);
}

static void Fail(string message)
{
    Console.Error.WriteLine($"git-snitch: {message}");
    Environment.ExitCode = 1;
}
