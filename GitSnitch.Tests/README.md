# Git Snitch .NET 8 tests

Run from the repository root:

```powershell
dotnet restore GitSnitch.Tests/GitSnitch.Tests.csproj
dotnet test GitSnitch.Tests/GitSnitch.Tests.csproj -c Release --verbosity normal
```

Requirements: .NET 8 SDK and Git on PATH. The integration tests create temporary Git repositories, configure repository-local author details, and delete their temporary files afterward. CLI tests invoke `dotnet` with the built GitSnitch assembly. No network access is required after NuGet packages have been restored.

The cancellation-under-load test uses a 60,000-line committed file and a 10 ms cancellation timer; it tests cooperative cancellation but cannot deterministically prove that the Git child process had already started. Process-tree termination and timeout behavior should also be exercised on Windows in CI.

The production service now resolves absolute file paths using the file's parent directory as the Git working directory, so calls made outside the repository can succeed. Relative paths retain the caller's working directory.
