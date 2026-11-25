# Revision 16 — Core refactoring (R16-01 through R16-07)

Default text output remains unchanged; use `--format json` for schemaVersion 1 JSON. `--commits` includes full commit identifiers in JSON and retains abbreviated identifiers in text. JSON source lines inherit their author from the containing group. Counts reflect filtered results. Errors remain on stderr.

GitBlameService, BlameLine, project dependencies and the VS Code extension are unchanged. Explicit console UTF-8 and comprehensive cancellation hardening are deferred. JSON is serialized in memory before writing; large-output behavior needs further verification.

Run `dotnet build GitSnitch.Tests/GitSnitch.Tests.csproj -c Release -warnaserror` and `dotnet test GitSnitch.Tests/GitSnitch.Tests.csproj -c Release --no-build`. Ten new test cases supplement the existing 39. Build and tests have not been executed in the preparation environment (no .NET SDK).
