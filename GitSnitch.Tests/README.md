# Git Snitch .NET 8 tests

Run from the repository root:

```powershell
dotnet restore GitSnitch.Tests/GitSnitch.Tests.csproj
dotnet test GitSnitch.Tests/GitSnitch.Tests.csproj -c Release --verbosity normal
```

Requirements: .NET 8 SDK and Git on PATH. The integration tests create temporary Git repositories, configure repository-local author details, and delete their temporary files afterward. CLI tests invoke `dotnet` with the built GitSnitch assembly. No network access is required after NuGet packages have been restored.

Cancellation and timeout coverage includes deterministic tests that start a blocking
child process, confirm its PID, request cancellation or await the deadline, and
verify that the child process has terminated. These replace the skipped,
timing-sensitive 60,000-line stress test. The production API still starts Git;
tests use an internal process factory seam to substitute a blocking OS process.

The test process helper waits for terminated child processes and observes redirected stream readers before deleting temporary repositories. CLI and service integration tests also exercise absolute paths from outside the temporary repository.

The production service now resolves absolute file paths using the file's parent directory as the Git working directory, so calls made outside the repository can succeed. Relative paths retain the caller's working directory.

## November 2026 test hardening

- Test repositories suppress system/global Git configuration and inherited Git author/committer identities.
- Commit fixtures use explicit `--author` values.
- Test process stdout/stderr are bounded (4 MiB / 256 KiB).
- Repository creation preserves its original failure if cleanup also fails.
- CLI tests verify case-insensitive author filtering, actual sorting order, and runtime configuration availability.

**Verification:** Run the suite on Windows and in CI. The blocking-process
lifecycle tests require `cmd.exe` on Windows or `/bin/sh` and `sleep` on Unix.
They validate the process termination and cancellation paths without relying
on Git execution speed.
