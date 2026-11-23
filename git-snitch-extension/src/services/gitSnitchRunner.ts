import { execFile, spawn } from "node:child_process";
import * as path from "node:path";

export interface AnalysisOptions {
  author: string;
  sort: "name" | "lines";
  commits: boolean;
  ignoreWhitespace: boolean;
}

export function buildArguments(filePath: string, options: AnalysisOptions): string[] {
  const args: string[] = [];
  if (options.author.trim()) args.push("--author", options.author.trim());
  args.push("--sort", options.sort);
  if (options.commits) args.push("--commits");
  if (options.ignoreWhitespace) args.push("-w");
  args.push("--", filePath);
  return args;
}

export class AnalysisError extends Error {
  constructor(message: string, public readonly stderr = "", public readonly exitCode?: number | string) {
    super(message);
    this.name = "AnalysisError";
  }
}

const MAX_BUFFER = 20 * 1024 * 1024;
const MAX_ERROR_TEXT = 64 * 1024;

function limit(text: string): string {
  return text.length > MAX_ERROR_TEXT ? text.slice(0, MAX_ERROR_TEXT) + "\n[truncated]" : text;
}

export async function runAnalysis(
  executable: string,
  filePath: string,
  options: AnalysisOptions,
  timeoutMs: number,
  signal?: AbortSignal
): Promise<{ stdout: string; stderr: string }> {
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new RangeError("Timeout must be positive");
  if (signal?.aborted) throw new AnalysisError("Analysis cancelled.");
  const args = buildArguments(filePath, options);
  return new Promise((resolve, reject) => {
    let settled = false;
    let termination: "cancelled" | "timeout" | undefined;
    let killer: ReturnType<typeof spawn> | undefined;
    const child = execFile(executable, args, {
      cwd: path.dirname(filePath), windowsHide: true, maxBuffer: MAX_BUFFER,
      encoding: "utf8"
    }, (error, stdout, stderr) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      signal?.removeEventListener("abort", onAbort);
      if (termination) {
        reject(new AnalysisError(termination === "timeout" ? "Analysis timed out." : "Analysis cancelled.", limit(stderr)));
      } else if (error) {
        const e = error as NodeJS.ErrnoException & { code?: string | number; killed?: boolean };
        reject(new AnalysisError(e.code === "ENOENT" ? "Git Snitch executable was not found." : e.message, limit(stderr), e.code));
      } else {
        resolve({ stdout, stderr });
      }
    });
    // On Windows, Node's child.kill() does not reliably kill subprocess descendants.
    const terminate = (reason: "cancelled" | "timeout") => {
      if (settled || termination) return;
      termination = reason;
      if (process.platform === "win32" && child.pid) {
        killer = spawn("taskkill", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true, stdio: "ignore" });
        killer.on("error", () => child.kill());
        killer.on("exit", (code) => { if (code !== 0 && child.exitCode === null) child.kill(); });
      } else {
        child.kill("SIGKILL");
      }
    };
    const onAbort = () => terminate("cancelled");
    signal?.addEventListener("abort", onAbort, { once: true });
    const timer = setTimeout(() => terminate("timeout"), timeoutMs);
    // Handle a cancellation racing with process creation/listener registration.
    if (signal?.aborted) onAbort();
  });
}
