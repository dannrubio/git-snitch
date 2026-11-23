import * as vscode from "vscode";
import * as path from "node:path";
import { randomBytes } from "node:crypto";
import { AnalysisResults } from "../services/analysisResults";

function escapeHtml(text: string): string {
  return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}

export class ResultsPanel implements vscode.Disposable {
  private panel: vscode.WebviewPanel | undefined;
  private readonly disposables: vscode.Disposable[] = [];
  private file: vscode.Uri | undefined;

  show(file: vscode.Uri, results: AnalysisResults): void {
    this.file = file;
    if (!this.panel) {
      this.panel = vscode.window.createWebviewPanel("gitSnitchResults", "Git Snitch Results", vscode.ViewColumn.Beside, {
        enableScripts: true,
        retainContextWhenHidden: true
      });
      this.panel.webview.onDidReceiveMessage(async (message: unknown) => {
        if (!message || typeof message !== "object") return;
        const data = message as { command?: unknown; line?: unknown };
        if (data.command !== "openLine" || !Number.isSafeInteger(data.line) || (data.line as number) < 1 || !this.file) return;
        const document = await vscode.workspace.openTextDocument(this.file);
        const editor = await vscode.window.showTextDocument(document, { viewColumn: vscode.ViewColumn.One, preserveFocus: false });
        const index = Math.min((data.line as number) - 1, Math.max(0, document.lineCount - 1));
        const range = document.lineAt(index).range;
        editor.selection = new vscode.Selection(range.start, range.start);
        editor.revealRange(range, vscode.TextEditorRevealType.InCenter);
      }, undefined, this.disposables);
      this.panel.onDidDispose(() => { this.panel = undefined; }, undefined, this.disposables);
    }
    this.panel.title = `Git Snitch: ${path.basename(file.fsPath)}`;
    this.panel.webview.html = renderResultsHtml(results);
    this.panel.reveal(vscode.ViewColumn.Beside, false);
  }

  dispose(): void {
    this.panel?.dispose();
    for (const disposable of this.disposables) disposable.dispose();
  }
}

export function renderResultsHtml(results: AnalysisResults): string {
  const nonce = randomBytes(16).toString("base64");
  const total = results.groups.reduce((sum, group) => sum + group.lines.length, 0);
  const groups = results.groups.map((group, index) => `
    <details class="author" ${index === 0 ? "open" : ""}>
      <summary><span class="author-name">${escapeHtml(group.author)}</span><span class="count">${group.lines.length} lines</span></summary>
      <div class="lines">${group.lines.map(line => `
        <div class="source-line"><button class="line-number" type="button" data-line="${line.number}" title="Open source line ${line.number}">${line.number}</button><code>${escapeHtml(line.code) || "&nbsp;"}</code>${line.commit ? `<span class="commit" title="Commit">${escapeHtml(line.commit)}</span>` : ""}</div>`).join("")}</div>
    </details>`).join("");
  return `<!DOCTYPE html>
<html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1.0">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<title>Git Snitch Results</title>
<style>
  body { color:var(--vscode-foreground);background:var(--vscode-editor-background);font-family:var(--vscode-font-family);padding:16px; }
  h1 { font-size:1.2rem;margin:0 0 6px; } .subtitle { color:var(--vscode-descriptionForeground);overflow-wrap:anywhere;margin-bottom:18px;font-size:12px; }
  .toolbar {display:flex;justify-content:space-between;align-items:center;gap:10px;margin-bottom:12px;}
  .toolbar button {background:var(--vscode-button-secondaryBackground);color:var(--vscode-button-secondaryForeground);border:0;border-radius:4px;padding:6px 10px;cursor:pointer;}
  .author {border:1px solid var(--vscode-panel-border);border-radius:6px;margin-bottom:10px;overflow:hidden;}
  summary {display:flex;align-items:center;gap:10px;cursor:pointer;padding:12px;background:var(--vscode-sideBar-background);font-weight:600;}
  summary::before {content:'▸';color:var(--vscode-descriptionForeground);} details[open] summary::before {content:'▾';}
  .author-name {overflow-wrap:anywhere;min-width:0;} .count {margin-left:auto;white-space:nowrap;color:var(--vscode-descriptionForeground);font-size:12px;}
  .lines {padding:5px 0;overflow-x:auto;} .source-line {display:flex;align-items:baseline;gap:12px;padding:3px 10px;min-width:max-content;}
  .source-line:hover {background:var(--vscode-list-hoverBackground);} .line-number {font:inherit;font-size:12px;color:var(--vscode-textLink-foreground);background:transparent;border:0;cursor:pointer;text-align:right;min-width:40px;padding:0;}
  .line-number:hover {text-decoration:underline;} code {white-space:pre;font-family:var(--vscode-editor-font-family),monospace;font-size:12px;}
  .commit {font-family:monospace;font-size:11px;color:var(--vscode-descriptionForeground);margin-left:auto;padding-left:12px;}
  .empty {padding:16px;border:1px solid var(--vscode-panel-border);border-radius:6px;}
</style></head><body>
<h1>Git Snitch</h1><div class="subtitle">${escapeHtml(results.heading)}</div>
<div class="toolbar"><span>${results.groups.length} authors · ${total} lines</span><div><button id="expand" type="button">Expand all</button> <button id="collapse" type="button">Collapse all</button></div></div>
${groups || `<p class="empty">${escapeHtml(results.message || "No attributable lines found.")}</p>`}
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  document.getElementById('expand').addEventListener('click', () => document.querySelectorAll('details.author').forEach(d => d.open = true));
  document.getElementById('collapse').addEventListener('click', () => document.querySelectorAll('details.author').forEach(d => d.open = false));
  document.addEventListener('click', event => {
    const button = event.target.closest('button[data-line]');
    if (button) vscode.postMessage({command:'openLine',line:Number(button.dataset.line)});
  });
</script></body></html>`;
}
