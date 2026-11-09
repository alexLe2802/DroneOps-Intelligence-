import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const helperSource = await readFile(new URL("../src/lib/uav-image-file.ts", import.meta.url), "utf8");
const helperCode = ts.transpileModule(helperSource, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { uavImageFileError, MAX_UAV_IMAGE_BYTES } = await import(`data:text/javascript;base64,${Buffer.from(helperCode).toString("base64")}`);

test("UAV image validation accepts the backend-supported image contract", () => {
  for (const type of ["image/jpeg", "image/png", "image/webp"]) {
    assert.equal(uavImageFileError({ type, size: MAX_UAV_IMAGE_BYTES }), null);
  }
});

test("UAV image validation rejects invalid type, empty data and oversized files", () => {
  assert.match(uavImageFileError({ type: "image/gif", size: 1024 }), /JPEG, PNG or WebP/);
  assert.match(uavImageFileError({ type: "image/png", size: 0 }), /empty/);
  assert.match(uavImageFileError({ type: "image/png", size: MAX_UAV_IMAGE_BYTES + 1 }), /5 MB/);
});

test("Add and Edit share image preview and upload behavior without retrying UAV creation", async () => {
  const source = await readFile(new URL("../src/app/portal/uav/uav-form.tsx", import.meta.url), "utf8");
  assert.match(source, /createUav, updateAssignedUav, uploadUavImage/);
  assert.match(source, /const result = await createUav\(request\);\s*id = result\.id/);
  assert.match(source, /try \{ await uploadUavImage\(id, imageFile\); \}\s*catch \(reason\) \{[\s\S]*imageUploadFailed = true;[\s\S]*imageUploadMessage = reason instanceof Error/);
  assert.match(source, /detailQuery\.set\("imageUploadMessage", imageUploadMessage\)/);
  assert.match(source, /imagePreview \|\| aircraft\?\.imageUrl/);
  assert.match(source, /disabled=\{saving\}/);
});
