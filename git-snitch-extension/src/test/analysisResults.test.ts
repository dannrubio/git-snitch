import * as assert from "node:assert";
import { parseAnalysisResults } from "../services/analysisResults";
import { renderResultsHtml } from "../views/resultsPanel";

suite("Git Snitch results panel", () => {
  test("groups authors and preserves source lines", () => {
    const result = parseAnalysisResults("Git Snitch: Program.cs\n\nAuthor: Alice (2 lines)\n" + "-".repeat(55) + "\n    1 | using GitSnitch; [abcdef12]\n    2 | \n\nAuthor: Bob (1 lines)\n" + "-".repeat(55) + "\n    3 | Console.WriteLine(1);\n", true);
    assert.equal(result.groups.length, 2);
    assert.deepEqual(result.groups[0].lines[0], { number: 1, code: "using GitSnitch;", commit: "abcdef12" });
    assert.equal(result.groups[0].lines[1].code, "");
    assert.equal(result.groups[1].author, "Bob");
  });
  test("handles empty result", () => {
    const result = parseAnalysisResults("Git Snitch: Program.cs\nNo attributable lines found.\n");
    assert.equal(result.groups.length, 0);
    assert.equal(result.message, "No attributable lines found.");
  });
  test("escapes untrusted author and code in Webview HTML", () => {
    const html = renderResultsHtml({ heading: "Git Snitch: x", groups: [{ author: "<script>alert(1)</script>", lines: [{ number: 1, code: "<img src=x>" }] }] });
    assert.ok(html.includes("&lt;script&gt;alert(1)&lt;/script&gt;"));
    assert.ok(html.includes("&lt;img src=x&gt;"));
    assert.ok(!html.includes("<img src=x>"));
    assert.ok(html.includes('data-line="1"'));
  });
  test("rejects unexpected CLI output", () => {
    assert.throws(() => parseAnalysisResults("unexpected"), /Unexpected/);
  });
});
