import fs from 'node:fs';
import path from 'node:path';
import { collectPackage, makeTarGz } from './lib.mjs';

const [pkgDir, out] = process.argv.slice(2);
if (!pkgDir || !out) {
  console.error('Usage: node build-unitypackage.mjs <packageDir> <out.unitypackage>');
  process.exit(2);
}

const pkg = collectPackage(pkgDir);
const prefix = `Packages/${pkg.name}`;
const entries = [];
for (const { rel, guid, isDir } of pkg.assets) {
  entries.push({ name: `${guid}/pathname`, data: Buffer.from(`${prefix}/${rel}\n`, 'utf8') });
  if (!isDir) entries.push({ name: `${guid}/asset`, data: fs.readFileSync(path.join(pkgDir, rel)) });
  entries.push({ name: `${guid}/asset.meta`, data: fs.readFileSync(path.join(pkgDir, rel + '.meta')) });
}
fs.mkdirSync(path.dirname(path.resolve(out)), { recursive: true });
fs.writeFileSync(out, makeTarGz(entries));
console.log(`${out}: ${pkg.assets.length} assets -> ${prefix}`);
