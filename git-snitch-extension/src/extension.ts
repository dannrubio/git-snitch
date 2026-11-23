import * as vscode from "vscode";
import { AnalysisError, AnalysisOptions, runAnalysis } from "./services/gitSnitchRunner";
import { parseAnalysisResults } from "./services/analysisResults";
import { ResultsPanel } from "./views/resultsPanel";

export function activate(context: vscode.ExtensionContext): void {
  const output = vscode.window.createOutputChannel("Git Snitch");
  context.subscriptions.push(output);
  let running = false;
  const resultsPanel = new ResultsPanel();
  context.subscriptions.push(resultsPanel);

  const command = vscode.commands.registerCommand("gitSnitch.analyzeFile", async (resource?: vscode.Uri) => {
    const uri = resource?.scheme === "file" ? resource : vscode.window.activeTextEditor?.document.uri;
    if (!uri || uri.scheme !== "file") {
      void vscode.window.showWarningMessage("Git Snitch: Please select a local file.");
      return;
    }
    if (running) {
      void vscode.window.showWarningMessage("Git Snitch: An analysis is already running.");
      return;
    }
    try {
      const stat = await vscode.workspace.fs.stat(uri);
      if (!(stat.type & vscode.FileType.File)) {
        void vscode.window.showWarningMessage("Git Snitch: Select a file, not a directory.");
        return;
      }
    } catch {
      void vscode.window.showErrorMessage("Git Snitch: Selected file does not exist or cannot be accessed.");
      return;
    }

    const config = vscode.workspace.getConfiguration("gitSnitch", uri);
    const executable = config.get<string>("executablePath", "").trim() || "git-snitch";
    const options: AnalysisOptions = {
      author: config.get<string>("author", ""),
      sort: config.get<"name" | "lines">("sort", "name"),
      commits: config.get<boolean>("commits", false),
      ignoreWhitespace: config.get<boolean>("ignoreWhitespace", false)
    };
    const timeoutSeconds = config.get<number>("timeoutSeconds", 45);
    const timeoutMs = timeoutSeconds * 1000;
    if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) {
      void vscode.window.showErrorMessage("Git Snitch: timeoutSeconds must be positive.");
      return;
    }
    running = true;
    output.clear();
    output.show(true);
    output.appendLine(`Git Snitch\nFile: ${uri.fsPath}\n${"-".repeat(60)}`);
    try {
      await vscode.window.withProgress({
        location: vscode.ProgressLocation.Notification,
        title: "Git Snitch: Analyzing file...",
        cancellable: true
      }, async (_progress, token) => {
        const controller = new AbortController();
        const subscription = token.onCancellationRequested(() => controller.abort());
        try {
          if (token.isCancellationRequested) controller.abort();
          const result = await runAnalysis(executable, uri.fsPath, options, timeoutMs, controller.signal);
          if (result.stdout) {
            output.appendLine(result.stdout);
            resultsPanel.show(uri, parseAnalysisResults(result.stdout, options.commits));
          }
          if (result.stderr) output.appendLine(result.stderr);
          void vscode.window.showInformationMessage("Git Snitch analysis completed.");
        } finally {
          subscription.dispose();
        }
      });
    } catch (error) {
      const err = error as Error;
      output.appendLine(`ERROR: ${err.message}`);
      if (error instanceof AnalysisError) {
        if (error.exitCode !== undefined) output.appendLine(`Exit code: ${error.exitCode}`);
        if (error.stderr) output.appendLine(`stderr:\n${error.stderr}`);
      }
      if (err.message.includes("executable was not found")) {
        void vscode.window.showErrorMessage("Git Snitch executable was not found. Configure gitSnitch.executablePath or add git-snitch to PATH.");
      } else if (err.message === "Analysis cancelled.") {
        output.appendLine("Analysis cancelled by user.");
      } else {
        void vscode.window.showErrorMessage("Git Snitch analysis failed. Check the output panel.");
      }
    } finally {
      running = false;
    }
  });
  context.subscriptions.push(command);
}

export function deactivate(): void {}
