
using GitSnitch;

if (args.Length == 0 ||
    (args.Length == 1 &&
     args[0] is "--help" or "-h"))
{
    PrintHelp();
    return;
}

string? file = null;
string? authorFilter = null;
string sort = "name";

bool commits = false;
bool ignoreWhitespace = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--author":
            if (++i >= args.Length ||
                string.IsNullOrWhiteSpace(args[i]) ||
                args[i].StartsWith("-", StringComparison.Ordinal))
            {
                Fail("Missing value for --author.");
                return;
            }

            authorFilter = args[i];
            break;

        case "--sort":
            if (++i >= args.Length ||
                args[i].StartsWith("-", StringComparison.Ordinal))
            {
                Fail("Missing value for --sort.");
                return;
            }

            sort = args[i];

            if (sort is not ("name" or "lines"))
            {
                Fail("--sort must be name or lines.");
                return;
            }

            break;

        case "--commits":
            commits = true;
            break;

        case "-w":
        case "--ignore-whitespace":
            ignoreWhitespace = true;
            break;

        case "--":
            if (file is not null || i + 2 != args.Length)
            {
                Fail("Expected exactly one file after --.");
                return;
            }

            file = args[i + 1];
            i = args.Length;
            break;

        default:
            if (args[i].StartsWith('-') || file is not null)
            {
                Fail($"Unexpected argument: {args[i]}");
                return;
            }

            file = args[i];
            break;
    }
}

if (string.IsNullOrWhiteSpace(file))
{
    Fail("A file is required.");
    return;
}

using var cancellation = new CancellationTokenSource();

ConsoleCancelEventHandler handler = (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

Console.CancelKeyPress += handler;

try
{
    var lines = await GitBlameService.ReadAsync(
        file,
        ignoreWhitespace,
        cancellation.Token
    );

    var groups = lines
        .Where(x =>
            authorFilter is null ||
            x.Author.Contains(
                authorFilter,
                StringComparison.OrdinalIgnoreCase
            ))
            .GroupBy(
                x => x.Author,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

    if (groups.Count == 0)
    {
        Console.WriteLine(
            authorFilter is null
                ? "No attributable lines found."
                : $"No lines found for author: {authorFilter}"
        );

        return;
    }

    var ordered = sort == "lines"
        ? groups.OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        : groups.OrderBy(
            g => g.Key,
            StringComparer.OrdinalIgnoreCase
        );

    Console.WriteLine($"Git Snitch: {file}");

    foreach (var group in ordered)
    {
        Console.WriteLine(
            $"\nAuthor: {group.Key} ({group.Count()} lines)"
        );

        Console.WriteLine(new string('-', 55));

        foreach (var line in group.OrderBy(x => x.LineNumber))
        {
            var suffix = commits
                ? $" [{line.Commit[..Math.Min(8, line.Commit.Length)]}]"
                : "";

            Console.WriteLine(
                $"{line.LineNumber,5} | {line.Code}{suffix}"
            );
        }
    }
}
catch (OperationCanceledException)
{
    Fail("Operation cancelled.");
}
catch (Exception ex) when (
    ex is InvalidOperationException or
    TimeoutException or
    System.ComponentModel.Win32Exception or
    IOException or
    UnauthorizedAccessException or
    ArgumentException)
{
    Fail(ex.Message);
}
finally
{
    Console.CancelKeyPress -= handler;
}

static void PrintHelp()
{
    Console.WriteLine(
        "Usage: git-snitch [options] <file>"
    );

    Console.WriteLine(
        "Group Git blame source lines by their last attributed author."
    );

    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --author NAME        Filter by author");
    Console.WriteLine("  --sort name|lines    Sort results");
    Console.WriteLine("  --commits            Show commit hashes");
    Console.WriteLine("  -w                   Ignore whitespace changes");
    Console.WriteLine("  --ignore-whitespace  Same as -w");
    Console.WriteLine("  -h, --help           Show help");
}

static void Fail(string message)
{
    Console.Error.WriteLine($"git-snitch: {message}");
    Environment.ExitCode = 1;
}
