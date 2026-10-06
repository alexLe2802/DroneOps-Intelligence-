import { build } from "esbuild";
import path from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

await build({
  entryPoints: [path.join(projectRoot, "node_modules/maplibre-gl/dist/maplibre-gl-worker.mjs")],
  bundle: true,
  format: "esm",
  platform: "browser",
  target: ["safari15"],
  minify: true,
  sourcemap: false,
  legalComments: "none",
  outfile: path.join(projectRoot, "public/maplibre-gl-worker.mjs"),
});
