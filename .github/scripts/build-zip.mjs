// Usage: node build-zip.mjs <packageDir> <out.zip>
// Builds the VPM/UPM package zip: package.json sits at the zip root, only package files are included.
import fs from 'node:fs';
import path from 'node:path';
import { collectPackage, makeZip } from './lib.mjs';

const [pkgDir, out] = process.argv.slice(2);
if (!pkgDir || !out) {
  console.error('Usage: node build-zip.mjs <packageDir> <out.zip>');
  process.exit(2);
}

const pkg = collectPackage(pkgDir);
const entries = pkg.files.map((rel) => ({ name: rel, data: fs.readFileSync(path.join(pkgDir, rel)) }));
fs.mkdirSync(path.dirname(path.resolve(out)), { recursive: true });
fs.writeFileSync(out, makeZip(entries));
console.log(`${out}: ${entries.length} files (${pkg.name} ${pkg.version})`);
