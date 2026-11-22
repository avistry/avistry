// Shared helpers for the release scripts. Node 20+, no dependencies.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

// Only these paths ship. Everything else (Website~, .github, dotfiles) stays in the repo.
const ROOT_FILES = ['package.json', 'README.md', 'CHANGELOG.md', 'LICENSE', 'Third Party Notices.md'];
const ROOT_DIRS = ['Editor'];

function isSkipped(name, isDir) {
  return name.startsWith('.') || (isDir && name.endsWith('~'));
}

function guidOf(metaPath) {
  const m = /^guid:\s*([0-9a-f]{32})\s*$/m.exec(fs.readFileSync(metaPath, 'utf8'));
  if (!m) throw new Error(`No guid in ${metaPath}`);
  return m[1];
}

/** Returns { name, version, files: string[] (every shipped file incl. .meta), assets: {rel, guid, isDir}[] }. */
export function collectPackage(pkgDir) {
  const pkg = JSON.parse(fs.readFileSync(path.join(pkgDir, 'package.json'), 'utf8'));
  const files = [];
  const assets = [];

  const addAsset = (rel, isDir) => {
    const meta = path.join(pkgDir, rel + '.meta');
    if (!fs.existsSync(meta)) throw new Error(`Missing .meta for ${rel}`);
    files.push(rel + '.meta');
    if (!isDir) files.push(rel);
    assets.push({ rel, guid: guidOf(meta), isDir });
  };

  const walk = (rel) => {
    for (const entry of fs.readdirSync(path.join(pkgDir, rel), { withFileTypes: true })) {
      if (isSkipped(entry.name, entry.isDirectory()) || entry.name.endsWith('.meta')) continue;
      const childRel = `${rel}/${entry.name}`;
      addAsset(childRel, entry.isDirectory());
      if (entry.isDirectory()) walk(childRel);
    }
    for (const entry of fs.readdirSync(path.join(pkgDir, rel))) {
      if (!entry.endsWith('.meta')) continue;
      if (!fs.existsSync(path.join(pkgDir, rel, entry.slice(0, -5)))) throw new Error(`Orphan .meta: ${rel}/${entry}`);
    }
  };

  for (const f of ROOT_FILES) {
    if (!fs.existsSync(path.join(pkgDir, f))) throw new Error(`Required file missing: ${f}`);
    addAsset(f, false);
  }
  for (const d of ROOT_DIRS) {
    if (!fs.existsSync(path.join(pkgDir, d))) throw new Error(`Required folder missing: ${d}`);
    addAsset(d, true);
    walk(d);
  }

  files.sort();
  assets.sort((a, b) => (a.guid < b.guid ? -1 : 1));
  return { name: pkg.name, version: pkg.version, files, assets };
}

// zip (deflate, fixed timestamps so the output is reproducible)

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

/** entries: { name: string (forward slashes), data: Buffer }[] */
export function makeZip(entries) {
  const DOS_TIME = 0;
  const DOS_DATE = (0 << 9) | (1 << 5) | 1; // 1980-01-01
  const locals = [];
  const centrals = [];
  let offset = 0;

  for (const { name, data } of entries) {
    const nameBuf = Buffer.from(name, 'utf8');
    const comp = zlib.deflateRawSync(data, { level: 9 });
    const crc = crc32(data);

    const lh = Buffer.alloc(30);
    lh.writeUInt32LE(0x04034b50, 0);
    lh.writeUInt16LE(20, 4);
    lh.writeUInt16LE(0x0800, 6);
    lh.writeUInt16LE(8, 8);
    lh.writeUInt16LE(DOS_TIME, 10);
    lh.writeUInt16LE(DOS_DATE, 12);
    lh.writeUInt32LE(crc, 14);
    lh.writeUInt32LE(comp.length, 18);
    lh.writeUInt32LE(data.length, 22);
    lh.writeUInt16LE(nameBuf.length, 26);
    locals.push(lh, nameBuf, comp);

    const ch = Buffer.alloc(46);
    ch.writeUInt32LE(0x02014b50, 0);
    ch.writeUInt16LE((3 << 8) | 20, 4);
    ch.writeUInt16LE(20, 6);
    ch.writeUInt16LE(0x0800, 8);
    ch.writeUInt16LE(8, 10);
    ch.writeUInt16LE(DOS_TIME, 12);
    ch.writeUInt16LE(DOS_DATE, 14);
    ch.writeUInt32LE(crc, 16);
    ch.writeUInt32LE(comp.length, 20);
    ch.writeUInt32LE(data.length, 24);
    ch.writeUInt16LE(nameBuf.length, 28);
    ch.writeUInt32LE(((0o100644 << 16) >>> 0), 38);
    ch.writeUInt32LE(offset, 42);
    centrals.push(ch, nameBuf);

    offset += lh.length + nameBuf.length + comp.length;
  }

  const cd = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(cd.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, cd, end]);
}

/** Returns the bytes of one entry from a zip buffer, or null when absent. */
export function readZipEntry(buf, entryName) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65557); i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('Not a zip file');
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  for (let i = 0; i < count; i++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error('Bad central directory');
    const method = buf.readUInt16LE(p + 10);
    const csize = buf.readUInt32LE(p + 20);
    const nameLen = buf.readUInt16LE(p + 28);
    const extraLen = buf.readUInt16LE(p + 30);
    const commentLen = buf.readUInt16LE(p + 32);
    const localOff = buf.readUInt32LE(p + 42);
    const name = buf.toString('utf8', p + 46, p + 46 + nameLen);
    if (name === entryName) {
      const dataStart = localOff + 30 + buf.readUInt16LE(localOff + 26) + buf.readUInt16LE(localOff + 28);
      const raw = buf.subarray(dataStart, dataStart + csize);
      return method === 0 ? Buffer.from(raw) : zlib.inflateRawSync(raw);
    }
    p += 46 + nameLen + extraLen + commentLen;
  }
  return null;
}

// tar.gz (ustar, fixed mtime/mode so the output is reproducible)

function tarHeader(name, size) {
  const h = Buffer.alloc(512);
  const nameBuf = Buffer.from(name, 'utf8');
  if (nameBuf.length > 100) throw new Error(`Tar name too long: ${name}`);
  nameBuf.copy(h, 0);
  h.write('0000644\0', 100);
  h.write('0000000\0', 108);
  h.write('0000000\0', 116);
  h.write(size.toString(8).padStart(11, '0') + '\0', 124);
  h.write('00000000000\0', 136);
  h.fill(0x20, 148, 156);
  h.write('0', 156);
  h.write('ustar\0', 257);
  h.write('00', 263);
  let sum = 0;
  for (const b of h) sum += b;
  h.write(sum.toString(8).padStart(6, '0') + '\0 ', 148);
  return h;
}

/** entries: { name: string, data: Buffer }[] */
export function makeTarGz(entries) {
  const parts = [];
  for (const { name, data } of entries) {
    parts.push(tarHeader(name, data.length), data);
    const pad = (512 - (data.length % 512)) % 512;
    if (pad) parts.push(Buffer.alloc(pad));
  }
  parts.push(Buffer.alloc(1024));
  return zlib.gzipSync(Buffer.concat(parts), { level: 9 });
}
