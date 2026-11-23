# Git Snitch

Git Snitch is a Visual Studio Code extension that helps developers understand who last modified each line of a Git-tracked source file.

It uses Git blame information to group source lines by author, making it easier to review code ownership and modification history.

## Features

- Analyze the currently selected source file.
- Group Git blame results by author.
- View source lines attributed to each author.
- Sort results by author name or number of lines.
- Filter results by author.
- Display commit information when supported by the Git Snitch CLI.
- Ignore whitespace changes when analyzing Git history.

## Requirements

- Visual Studio Code
- Git installed and available on your system
- Git Snitch command-line application installed or published
- A Git repository containing the file you want to analyze

## Installation

1. Install the Git Snitch extension in Visual Studio Code.
2. Publish or install the Git Snitch CLI.
3. Configure the `gitSnitch.executablePath` setting to point to the Git Snitch executable.
4. Open a Git-tracked source file.

## Usage

1. Open a file in a Git repository.
2. Right-click the file in the editor or Explorer.
3. Select **Git Snitch: Analyze File**.
4. Review the analysis results.

## Configuration

### `gitSnitch.executablePath`

Specifies the path to the Git Snitch command-line executable.

Example for Windows:

```json
{
  "gitSnitch.executablePath": "C:\\Tools\\GitSnitch\\git-snitch.exe"
}
```

## Troubleshooting

**Git Snitch executable not found**

Verify that the executable exists and that `gitSnitch.executablePath` points to the correct location.

**Git blame fails**

Ensure the selected file belongs to a Git repository and that Git is installed.

**No results**

Check that the file is tracked by Git and contains committed history.

## Development

Install dependencies:

```bash
npm ci
```

Compile and lint:

```bash
npm run compile
```

Run extension tests:

```bash
npm test
```

Package the extension:

```bash
npx @vscode/vsce package
```

## License

See the project's LICENSE file for licensing information.

### Analysis timeout and cancellation

The `gitSnitch.timeoutSeconds` setting defaults to 45 seconds (range 1–600).
While analyzing, select **Cancel** on the VS Code progress notification to stop the CLI process.
Only one analysis runs at a time; errors and CLI stderr appear in the Git Snitch output channel.
