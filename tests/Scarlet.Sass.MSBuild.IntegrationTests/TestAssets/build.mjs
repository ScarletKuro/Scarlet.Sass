import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { minify } from "terser";
import * as sass from "sass";

const scriptFilename = fileURLToPath(import.meta.url);
const scriptDirectory = path.dirname(scriptFilename);
const jsInputDir = path.join(scriptDirectory, "scripts");
const jsOutputFile = path.join(scriptDirectory, "output/Sassdle.min.js");
const scssInput = path.join(scriptDirectory, "styles/style.scss");
const scssOutput = path.join(scriptDirectory, "output/style.min.css");

async function buildJS() {
  // Note: We can't use the built in Sassdler because our scripts are not modules.
  // This script manually concatenates and minifies them.
  console.log("Building JS Sassdle", jsInputDir);

  if (!fs.existsSync(jsInputDir)) {
    console.error("JS directory missing:", jsInputDir);
    process.exit(1);
  }

  let files = fs
    .readdirSync(jsInputDir)
    .filter((f) => f.endsWith(".js"))
    .sort();

  if (files.length === 0) {
    console.error("No JS files found:", jsInputDir);
    process.exit(1);
  }

  // Concatenate files
  let code = "";
  for (const file of files) {
    const filePath = path.join(jsInputDir, file);
    console.log("Adding", filePath);
    code += fs.readFileSync(filePath, "utf-8") + "\n";
  }

  // Minify
  const minified = await minify(code);

  // Write JS Sassdle
  console.log("Writing JS Sassdle", jsOutputFile);
  const outDir = path.dirname(jsOutputFile);
  if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(jsOutputFile, minified.code, "utf-8");
}

function buildSCSS() {
  console.log("Building SCSS Sassdle", scssInput);

  const result = sass.compile(scssInput, {
    style: "compressed",
    sourceMap: false,
    silenceDeprecations: ["import", "global-builtin"],
  });

  // Write SCSS Sassdle
  console.log("Writing SCSS Sassdle", scssOutput);
  fs.mkdirSync(path.dirname(scssOutput), { recursive: true });
  fs.writeFileSync(scssOutput, result.css);
}

await buildJS();
buildSCSS();

console.log("Build completed successfully!");
