namespace GitSnitch.CommandLine;

public static class CliArgumentParser
{
    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "-h"))
            return new CliOptions { ShowHelp = true };

        string? file = null;
        string? authorFilter = null;
        string sort = "name";

        var format = OutputFormat.Text;
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
                        throw new ArgumentException("Missing value for --author.");
                    }

                    authorFilter = args[i];
                    break;

                case "--sort":
                    if (++i >= args.Length ||
                        args[i].StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("Missing value for --sort.");
                    }

                    sort = args[i];

                    if (sort is not ("name" or "lines"))
                    {
                        throw new ArgumentException("--sort must be name or lines.");
                    }

                    break;

                case "--format":
                    if (++i >= args.Length || args[i].StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException("Missing value for --format.");
                    format = args[i] switch
                    {
                        "text" => OutputFormat.Text,
                        "json" => OutputFormat.Json,
                        _ => throw new ArgumentException("--format must be text or json.")
                    };
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
                        throw new ArgumentException("Expected exactly one file after --.");
                    }

                    file = args[i + 1];
                    i = args.Length;
                    break;

                default:
                    if (args[i].StartsWith('-') || file is not null)
                    {
                        throw new ArgumentException($"Unexpected argument: {args[i]}");
                    }

                    file = args[i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException("A file is required.");
        }

        return new CliOptions
        {
            FilePath = file, AuthorFilter = authorFilter, SortBy = sort,
            IncludeCommits = commits, IgnoreWhitespace = ignoreWhitespace, Format = format
        };
    }
}
