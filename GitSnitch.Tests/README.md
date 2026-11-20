# Git Snitch .NET 8 tests

Run from the repository root:

```powershell
dotnet restore GitSnitch.Tests/GitSnitch.Tests.csproj
dotnet test GitSnitch.Tests/GitSnitch.Tests.csproj -c Release --verbosity normal
```

Requirements: .NET 8 SDK and Git on PATH. The integration tests create temporary Git repositories, configure repository-local author details, and delete their temporary files afterward. CLI tests invoke `dotnet` with the built GitSnitch assembly. No network access is required after NuGet packages have been restored.

The 60,000-line cancellation-under-load stress test is explicitly skipped by default because a 10 ms timer cannot deterministically prove Git was already running. The pre-cancellation and immediate-deadline tests still run. A deterministic in-flight cancellation test will require an injectable/controllable process launcher; do not mistake the skipped stress test for verified process-tree termination. To run it manually, temporarily remove the `Skip` attribute.

The test process helper waits for terminated child processes and observes redirected stream readers before deleting temporary repositories. CLI and service integration tests also exercise absolute paths from outside the temporary repository.

The production service now resolves absolute file paths using the file's parent directory as the Git working directory, so calls made outside the repository can succeed. Relative paths retain the caller's working directory.
