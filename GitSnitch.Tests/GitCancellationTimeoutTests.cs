using System.Diagnostics;
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

        await Assert.ThrowsAsync<TimeoutException>(() =>
            GitBlameService.ReadAsync(repo.File("sample.cs"), false,
                timeout: TimeSpan.FromTicks(1)));
    }

    [Fact]
    public async Task ReadAsync_CancellationAfterProcessStarted_TerminatesChild()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = GitBlameService.ReadWithProcessAsync(
            "unused.cs", false, CreateBlockingProcess,
            process => started.TrySetResult(process.Id),
            cancellation.Token, TimeSpan.FromSeconds(20));

        var pid = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(10)));
        AssertProcessExited(pid);
    }

    [Fact]
    public async Task ReadAsync_TimeoutAfterProcessStarted_TerminatesChild()
    {
        var started = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = GitBlameService.ReadWithProcessAsync(
            "unused.cs", false, CreateBlockingProcess,
            process => started.TrySetResult(process.Id),
            timeout: TimeSpan.FromSeconds(2));

        var pid = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await operation.WaitAsync(TimeSpan.FromSeconds(10)));
        AssertProcessExited(pid);
    }

    // The process remains alive until the service kills it. No arbitrary
    // large-file workload or cancellation delay is involved.
    private static Process CreateBlockingProcess(ProcessStartInfo gitStart)
    {
        var start = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            WorkingDirectory = gitStart.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("ping -n 60 127.0.0.1 >nul");
        }
        else
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("sleep 60");
        }

        return new Process { StartInfo = start };
    }

    private static void AssertProcessExited(int pid)
    {
        try
        {
            using var child = Process.GetProcessById(pid);
            Assert.True(child.HasExited, $"Child process {pid} remains running.");
        }
        catch (ArgumentException)
        {
            // The OS no longer has this PID: process termination succeeded.
        }
    }
}
