import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';
const source = await readFile(new URL('../src/lib/license-document.ts', import.meta.url), 'utf8');
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext } }).outputText;
const { isLicenseDocument, licenseDocumentLabel } = await import(`data:text/javascript;base64,${Buffer.from(js).toString('base64')}`);
test('license accepts PDF and Word, rejects images and mismatched types', () => {
  for (const [name, type] of [['scan.pdf', 'application/pdf'], ['scan.DOC', 'application/msword'], ['scan.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document']]) {
    assert.equal(isLicenseDocument({name, type}), true);
  }
  for (const [name, type] of [['scan.pdf', ''], ['scan.jpg', 'image/jpeg'], ['scan.png', 'image/png'], ['scan.exe', 'application/pdf'], ['scan.pdf', 'image/png']]) {
    assert.equal(isLicenseDocument({name, type}), false);
  }
});
test('signed document links display Word and PDF as documents and preserve legacy images', () => {
  assert.equal(licenseDocumentLabel('https://example.com/licenses/scan.docx?token=test'), 'Word');
  assert.equal(licenseDocumentLabel('https://example.com/licenses/scan.pdf?token=test'), 'PDF');
  assert.equal(licenseDocumentLabel('https://example.com/licenses/scan.jpg?token=test'), null);
});
