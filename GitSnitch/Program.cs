using GitSnitch;
using GitSnitch.CommandLine;
using GitSnitch.Formatters;
using GitSnitch.Services;

CliOptions options;
try
{
    options = CliArgumentParser.Parse(args);
}
catch (ArgumentException ex)
{
    Fail(ex.Message);
    return;
}

if (options.ShowHelp)
{
    PrintHelp();
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
    var lines = await GitBlameService.ReadAsync(options.FilePath,
        options.IgnoreWhitespace, cancellation.Token);
    var result = BlameAnalysisService.Analyze(lines, options, cancellation.Token);
    IOutputFormatter formatter = options.Format == OutputFormat.Json
        ? new JsonOutputFormatter() : new TextOutputFormatter();
    await formatter.WriteAsync(result, options, Console.Out, cancellation.Token);
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
    Console.WriteLine("  --format text|json   Output format (default: text)");
    Console.WriteLine("  --commits            Show commit hashes");
    Console.WriteLine("  -w                   Ignore whitespace changes");
    Console.WriteLine("  --ignore-whitespace  Same as -w");
    Console.WriteLine("  --                   End option parsing");
    Console.WriteLine("  -h, --help           Show help");
}

static void Fail(string message)
{
    Console.Error.WriteLine($"git-snitch: {message}");
    Environment.ExitCode = 1;
}
