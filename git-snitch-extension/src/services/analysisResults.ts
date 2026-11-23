export interface SourceLine { number: number; code: string; commit?: string }
export interface AuthorGroup { author: string; lines: SourceLine[] }
export interface AnalysisResults { heading: string; groups: AuthorGroup[]; message?: string }

/** Parse the human-readable output emitted by GitSnitch/Program.cs. */
export function parseAnalysisResults(stdout: string, showCommits = false): AnalysisResults {
  const rows = stdout.replace(/\r\n/g, "\n").split("\n");
  const heading = rows.shift() ?? "Git Snitch";
  if (!heading.startsWith("Git Snitch:")) throw new Error("Unexpected Git Snitch output header.");
  const groups: AuthorGroup[] = [];
  let current: AuthorGroup | undefined;
  const messages: string[] = [];
  for (const row of rows) {
    const author = /^Author: (.*) \((\d+) lines\)$/.exec(row);
    if (author) {
      current = { author: author[1], lines: [] };
      groups.push(current);
      continue;
    }
    if (!current) {
      if (row.trim()) messages.push(row);
      continue;
    }
    if (/^-{55}$/.test(row) || row === "") continue;
    const line = /^\s*(\d+) \| (.*)$/.exec(row);
    if (!line) throw new Error("Unrecognized Git Snitch output line.");
    let code = line[2];
    let commit: string | undefined;
    const match = showCommits ? / \[([0-9a-f]{8}|uncommi)\]$/.exec(code) : null;
    if (match) {
      commit = match[1];
      code = code.slice(0, -match[0].length);
    }
    current.lines.push({ number: Number(line[1]), code, ...(commit ? { commit } : {}) });
  }
  return { heading, groups, ...(messages.length ? { message: messages.join("\n") } : {}) };
}
