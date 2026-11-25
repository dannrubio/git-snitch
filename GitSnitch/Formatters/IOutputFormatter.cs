using GitSnitch.CommandLine;
using GitSnitch.Models;

namespace GitSnitch.Formatters;

public interface IOutputFormatter
{
    Task WriteAsync(AnalysisResult result, CliOptions options, TextWriter writer,
        CancellationToken cancellationToken = default);
}
