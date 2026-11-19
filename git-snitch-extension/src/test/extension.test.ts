
import * as assert from "node:assert";
import * as vscode from "vscode";

suite("Git Snitch Extension Test Suite", () => {
  test("Git Snitch command is registered", async () => {
    const extension = vscode.extensions.getExtension(
      "dannrubio.git-snitch-extension"
    );

    assert.ok(extension, "Extension should exist");

    await extension.activate();

    const commands = await vscode.commands.getCommands(true);

    assert.ok(
      commands.includes("gitSnitch.analyzeFile"),
      "Git Snitch analyze command should be registered"
    );
  });
});
