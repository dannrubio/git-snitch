import * as assert from "node:assert/strict";
import * as path from "node:path";
import { buildArguments, runAnalysis, AnalysisOptions, AnalysisError } from "../services/gitSnitchRunner";

const defaults: AnalysisOptions = { author: "", sort: "name", commits: false, ignoreWhitespace: false };

suite("Git Snitch runner", () => {
  test("default arguments include option terminator and exact file path", () => {
    assert.deepEqual(buildArguments("C:\\My Project\\-file.cs", defaults), ["--sort", "name", "--", "C:\\My Project\\-file.cs"]);
  });
  test("optional arguments are correctly assembled", () => {
    assert.deepEqual(buildArguments("file.cs", { author: " Alice ", sort: "lines", commits: true, ignoreWhitespace: true }),
      ["--author", "Alice", "--sort", "lines", "--commits", "-w", "--", "file.cs"]);
  });
  test("missing executable returns classified error", async () => {
    await assert.rejects(runAnalysis("nonexistent-git-snitch-executable-xyz", path.join(process.cwd(), "test.cs"), defaults, 2000),
      (e: unknown) => e instanceof AnalysisError && e.message.includes("executable was not found"));
  });
  test("pre-cancelled signal prevents execution", async () => {
    const controller = new AbortController();
    controller.abort();
    await assert.rejects(runAnalysis(process.execPath, path.join(process.cwd(), "test.cs"), defaults, 2000, controller.signal),
      (e: unknown) => e instanceof AnalysisError && e.message === "Analysis cancelled.");
  });
  test("invalid timeout is rejected", async () => {
    await assert.rejects(runAnalysis(process.execPath, path.join(process.cwd(), "test.cs"), defaults, 0), RangeError);
  });
});
