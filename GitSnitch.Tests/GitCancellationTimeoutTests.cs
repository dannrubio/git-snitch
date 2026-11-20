using GitSnitch;
using Xunit;

namespace GitSnitch.Tests;

public sealed class GitCancellationTimeoutTests
{
    [Fact]
    public async Task ReadAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GitBlameService.ReadAsync("unused.cs", false, cancellation.Token));
    }

    [Fact]
    public async Task ReadAsync_ExpiredDeadline_ThrowsTimeoutException()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        await repo.WriteAndCommitAsync("sample.cs", "content\n");

        // A one-tick deadline is already expired by the first async scheduling point.
        await Assert.ThrowsAsync<TimeoutException>(() =>
            GitBlameService.ReadAsync(repo.File("sample.cs"), false,
                timeout: TimeSpan.FromTicks(1)));
    }

    // A timer cannot guarantee cancellation occurs after Git starts and before it exits.
    // Retained as an opt-in stress test; deterministic process-lifecycle testing
    // requires a controllable process launcher.
    [Fact(Skip = "Timing-sensitive stress test; run manually when investigating cancellation")]
    public async Task ReadAsync_CancellationDuringLargeBlame_StopsPromptly()
    {
        await using var repo = await GitTestRepository.CreateAsync();
        var content = string.Join('\n', Enumerable.Range(1, 60000).Select(i => $"line {i}")) + "\n";
        await repo.WriteAndCommitAsync("large.cs", content);

        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(10));
        var task = GitBlameService.ReadAsync(repo.File("large.cs"), false, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await task.WaitAsync(TimeSpan.FromSeconds(15)));
    }
}
