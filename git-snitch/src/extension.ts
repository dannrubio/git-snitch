
import * as vscode from "vscode";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

export function activate(context: vscode.ExtensionContext) {
  const output = vscode.window.createOutputChannel("Git Snitch");

  const analyze = vscode.commands.registerCommand(
    "gitSnitch.analyze",
    async (resource?: vscode.Uri) => {
      const uri = resource ?? vscode.window.activeTextEditor?.document.uri;

      if (!uri || uri.scheme !== "file") {
        vscode.window.showWarningMessage("Open a local source file first.");
        return;
      }

      const folder = vscode.workspace.getWorkspaceFolder(uri);
      const cwd = folder?.uri.fsPath;

      if (!cwd) {
        vscode.window.showErrorMessage(
          "Open a Git repository folder in VS Code."
        );
        return;
      }

      const configuredPath = vscode.workspace
        .getConfiguration("gitSnitch")
        .get<string>("executablePath", "")
        .trim();

      const executable = configuredPath || "git-snitch";

      try {
        const { stdout, stderr } = await execFileAsync(
          executable,
          [uri.fsPath],
          {
            cwd,
            windowsHide: true,
            maxBuffer: 10 * 1024 * 1024
          }
        );

        output.clear();
        output.appendLine(`Git Snitch: ${uri.fsPath}`);
        output.appendLine("");
        output.append(stdout);

        if (stderr) {
          output.appendLine(stderr);
        }

        output.show(true);
      } catch (error) {
        const message =
          error instanceof Error ? error.message : String(error);

        vscode.window.showErrorMessage(
          `Git Snitch failed: ${message}`
        );
      }
    }
  );

  context.subscriptions.push(analyze, output);
}

export function deactivate() {}
