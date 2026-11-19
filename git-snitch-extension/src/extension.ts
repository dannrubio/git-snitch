
import * as vscode from "vscode";
import * as path from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

export function activate(
  context: vscode.ExtensionContext
): void {
  const output = vscode.window.createOutputChannel(
    "Git Snitch"
  );

  context.subscriptions.push(output);

  const analyzeFile = vscode.commands.registerCommand(
    "gitSnitch.analyzeFile",
    async (resource?: vscode.Uri) => {
      const uri =
        resource?.scheme === "file"
          ? resource
          : vscode.window.activeTextEditor?.document.uri;

      if (!uri || uri.scheme !== "file") {
        vscode.window.showWarningMessage(
          "Git Snitch: Please select a local file."
        );
        return;
      }

      const config = vscode.workspace.getConfiguration(
        "gitSnitch",
        uri
      );

      const executablePath =
        config.get<string>("executablePath", "").trim()
        || "git-snitch";

      const author = config.get<string>("author", "");
      const sort = config.get<string>("sort", "name");
      const commits = config.get<boolean>("commits", false);
      const ignoreWhitespace =
        config.get<boolean>("ignoreWhitespace", false);

      const filePath = uri.fsPath;
      const workingDirectory = path.dirname(filePath);

      const args: string[] = [];

      if (author.trim()) {
        args.push("--author", author.trim());
      }

      args.push("--sort", sort);

      if (commits) {
        args.push("--commits");
      }

      if (ignoreWhitespace) {
        args.push("-w");
      }

      args.push(filePath);

      output.clear();
      output.show(true);

      output.appendLine("Git Snitch");
      output.appendLine(`File: ${filePath}`);
      output.appendLine("-".repeat(60));

      try {
        await vscode.window.withProgress(
          {
            location: vscode.ProgressLocation.Notification,
            title: "Git Snitch: Analyzing file...",
            cancellable: false
          },
          async () => {
            const { stdout, stderr } = await execFileAsync(
              executablePath,
              args,
              {
                cwd: workingDirectory,
                windowsHide: true,
                maxBuffer: 10 * 1024 * 1024
              }
            );

            if (stdout) {
              output.appendLine(stdout);
            }

            if (stderr) {
              output.appendLine(stderr);
            }

            vscode.window.showInformationMessage(
              "Git Snitch analysis completed."
            );
          }
        );
      } catch (error) {
        const message =
          error instanceof Error
            ? error.message
            : String(error);

        output.appendLine(`ERROR: ${message}`);

        vscode.window.showErrorMessage(
          "Git Snitch analysis failed. Check the output panel."
        );
      }
    }
  );

  context.subscriptions.push(analyzeFile);
}

export function deactivate(): void {}
