
const esbuild = require("esbuild");

const production = process.argv.includes("--production");
const watch = process.argv.includes("--watch");

const config = {
  entryPoints: ["src/extension.ts"],
  bundle: true,
  format: "cjs",
  platform: "node",
  target: "node22",
  outfile: "dist/extension.js",
  external: ["vscode"],
  sourcemap: !production,
  minify: production,
  logLevel: "info"
};

async function main() {
  if (watch) {
    const context = await esbuild.context(config);
    await context.watch();
    console.log("Git Snitch: Watching for changes...");
  } else {
    await esbuild.build(config);
    console.log("Git Snitch: Build completed.");
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
