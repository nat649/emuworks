const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const MAGIC = 0xEE0BDDBA, BASE = 0x20000000;
// Upsilon places its record cache before the header; Epsilon places it after the delegate.
const LAYOUTS = [
  { size: 32788, header: 0, end: 32772, cache: 32780 },
  { size: 64020, header: 8, end: 64012, cache: 0 }
];
function layout(region) {
  const format = LAYOUTS.find(f => region.length === f.size);
  if (!format || region.readUInt32LE(format.header) !== MAGIC || region.readUInt32LE(format.end) !== MAGIC)
    throw new Error('Truncated storage or invalid markers.');
  return format;
}
function atomicWrite(file, data) {
  const temp = file + '.' + crypto.randomUUID() + '.tmp';
  try {
    const fd = fs.openSync(temp, 'wx');
    try { fs.writeFileSync(fd, data); fs.fsyncSync(fd); } finally { fs.closeSync(fd); }
    fs.renameSync(temp, file);
  } finally { if (fs.existsSync(temp)) fs.unlinkSync(temp); }
}
function safeName(name) {
  if (!/^[a-zA-Z0-9_][a-zA-Z0-9_.-]*\.py$/.test(name) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])\./i.test(name))
    throw new Error('Nom de script refuse : ' + name);
}
function parse(region) {
  const { header, end: END } = layout(region);
  const records = [], names = new Set();
  let p = header + 4;
  while (p + 2 <= END) {
    const size = region.readUInt16LE(p);
    if (!size) return records;
    if (size < 4 || p + size > END) throw new Error('Enregistrement hors limites.');
    const end = region.indexOf(0, p + 2);
    if (end <= p + 2 || end >= p + size) throw new Error('Nom de record invalide.');
    const name = region.toString('latin1', p + 2, end);
    if (names.has(name.toLowerCase())) throw new Error('Nom de record duplique : ' + name);
    names.add(name.toLowerCase());
    records.push({ name, body: Buffer.from(region.subarray(end + 1, p + size)) });
    p += size;
  }
  throw new Error('Terminateur absent.');
}
function locateStorage(sram) {
  if (sram.length !== 0x40000) throw new Error('Le vidage SRAM doit faire exactement 256 Ko.');
  const found = [];
  for (let off = 0; off + 4 <= sram.length; off += 4) {
    if (sram.readUInt32LE(off) !== MAGIC) continue;
    for (const format of LAYOUTS) {
      const start = off - format.header;
      if (start < 0 || start + format.size > sram.length) continue;
      const region = sram.subarray(start, start + format.size);
      try { parse(region); found.push({ offset: off, start, region }); } catch { /* Invalid candidate. */ }
    }
  }
  if (found.length !== 1) throw new Error('Stockage valide introuvable ou ambigu.');
  return found[0];
}
function findStorage(sram) { return locateStorage(sram).offset; }
function rebuild(region, records) {
  parse(region);
  const { header, end: END, cache } = layout(region);
  const out = Buffer.from(region);
  out.fill(0, header + 4, END);
  let p = header + 4;
  for (const r of records) {
    const name = Buffer.from(r.name, 'latin1'), size = 3 + name.length + r.body.length;
    if (size > 65535 || p + size + 2 > END) throw new Error('Storage is full (' + (END - header - 4) + ' bytes).');
    out.writeUInt16LE(size, p); name.copy(out, p + 2); r.body.copy(out, p + 3 + name.length); p += size;
  }
  out.writeUInt32LE(0, cache); out.writeUInt32LE(0, cache + 4);
  parse(out);
  return out;
}
function recover(dir) {
  const live = path.join(dir, 'scripts'), old = path.join(dir, '.scripts-rollback');
  if (!fs.existsSync(live) && fs.existsSync(old)) fs.renameSync(old, live);
}
function readScripts(dir) {
  const files = new Map(), names = new Set();
  if (!fs.existsSync(dir)) return files;
  for (const name of fs.readdirSync(dir).sort()) {
    safeName(name);
    if (!fs.lstatSync(path.join(dir, name)).isFile()) throw new Error('Fichier ordinaire attendu : ' + name);
    if (names.has(name.toLowerCase())) throw new Error('Noms ambigus.');
    names.add(name.toLowerCase()); files.set(name, fs.readFileSync(path.join(dir, name)));
  }
  return files;
}
const digest = data => crypto.createHash('sha256').update(data).digest('hex');
function backup(dir) {
  recover(dir);
  const files = readScripts(path.join(dir, 'scripts')), root = path.join(dir, 'sauvegardes');
  fs.mkdirSync(root, { recursive: true });
  const id = new Date().toISOString().replace(/[:.]/g, '-') + '-' + crypto.randomUUID().slice(0, 8);
  const stage = path.join(root, '.tmp-' + id), dest = path.join(root, id);
  fs.mkdirSync(stage);
  const hashes = {};
  for (const [name, data] of files) { atomicWrite(path.join(stage, name), data); hashes[name] = digest(data); }
  atomicWrite(path.join(stage, 'manifest.json'), JSON.stringify({ version: 1, files: hashes }, null, 2));
  fs.renameSync(stage, dest);
  console.log('Sauvegarde : ' + dest);
  return dest;
}
function replaceScripts(dir, files) {
  recover(dir);
  const live = path.join(dir, 'scripts'), old = path.join(dir, '.scripts-rollback');
  const stage = path.join(dir, '.scripts-' + crypto.randomUUID());
  fs.mkdirSync(stage);
  try {
    for (const [name, data] of files) { safeName(name); atomicWrite(path.join(stage, name), data); }
    // La sauvegarde horodatee est terminee avant tout remplacement.
    if (fs.existsSync(old)) fs.rmSync(old, { recursive: true });
    if (fs.existsSync(live)) fs.renameSync(live, old);
    try { fs.renameSync(stage, live); }
    catch (err) { if (fs.existsSync(old)) fs.renameSync(old, live); throw err; }
  } finally { if (fs.existsSync(stage)) fs.rmSync(stage, { recursive: true }); }
}
function restore(dir, source) {
  const manifest = JSON.parse(fs.readFileSync(path.join(source, 'manifest.json'), 'utf8'));
  if (manifest.version !== 1 || !manifest.files || typeof manifest.files !== 'object') throw new Error('Sauvegarde invalide.');
  const files = new Map(), names = new Set();
  for (const [name, hash] of Object.entries(manifest.files)) {
    safeName(name);
    if (names.has(name.toLowerCase())) throw new Error('Noms ambigus.');
    names.add(name.toLowerCase());
    const file = path.join(source, name);
    if (!fs.lstatSync(file).isFile()) throw new Error('Fichier de sauvegarde invalide.');
    const data = fs.readFileSync(file);
    if (digest(data) !== hash) throw new Error('Sauvegarde alteree : ' + name);
    files.set(name, data);
  }
  backup(dir); replaceScripts(dir, files);
  console.log(files.size + ' script(s) restaure(s).');
}
function main([cmd, a1, a2, flag]) {
  if (!a1) throw new Error('Usage : rom.js backup|push <rom> / restore <rom> <sauvegarde> / pull|ref <sram> <rom>');
  if (cmd === 'backup') return backup(a1);
  if (cmd === 'restore') return restore(a1, a2);
  if (cmd === 'ref' || cmd === 'pull') {
    const sram = fs.readFileSync(a1), { start, region } = locateStorage(sram);
    const records = parse(region);
    fs.mkdirSync(a2, { recursive: true }); recover(a2);
    const metaPath = path.join(a2, '.storage.json');
    let flags = {};
    if (cmd === 'ref' && fs.existsSync(metaPath)) flags = JSON.parse(fs.readFileSync(metaPath, 'utf8')).flags || {};
    const files = new Map();
    for (const r of records.filter(r => r.name.endsWith('.py'))) {
      safeName(r.name);
      if (r.body.length < 2 || r.body[r.body.length - 1] !== 0) throw new Error('Script non termine : ' + r.name);
      files.set(r.name, r.body.subarray(1, r.body.length - 1));
      if (flags[r.name] === undefined) flags[r.name] = r.body[0];
    }
    if (cmd === 'pull') {
      if (!files.size && readScripts(path.join(a2, 'scripts')).size && flag !== '--force')
        throw new Error('Stockage Python vide : scripts conserves. Utiliser --force pour confirmer leur suppression.');
      backup(a2); replaceScripts(a2, files);
    } else fs.mkdirSync(path.join(a2, 'scripts'), { recursive: true });
    atomicWrite(path.join(a2, '.storage.bin'), region);
    atomicWrite(metaPath, JSON.stringify({ address: '0x' + (BASE + start).toString(16), flags }, null, 2));
    console.log(cmd + ' : ' + files.size + ' script(s), stockage a 0x' + (BASE + start).toString(16)); return;
  }
  if (cmd === 'push') {
    recover(a1);
    const meta = JSON.parse(fs.readFileSync(path.join(a1, '.storage.json'), 'utf8')), address = Number(meta.address);
    const region = fs.readFileSync(path.join(a1, '.storage.bin'));
    layout(region);
    if (!Number.isInteger(address) || address % 4 || address < BASE || address + region.length > BASE + 0x40000) throw new Error('Adresse de stockage invalide.');
    // Les autres records ne sont jamais interpretes comme du Python.
    const records = parse(region).filter(r => !r.name.endsWith('.py'));
    const files = readScripts(path.join(a1, 'scripts'));
    for (const [name, raw] of files) {
      const code = raw.length && raw[raw.length - 1] === 0 ? raw.subarray(0, -1) : raw;
      if (code.includes(0)) throw new Error('Octet NUL dans le script : ' + name);
      records.push({ name, body: Buffer.concat([Buffer.from([(meta.flags || {})[name] || 0]), code, Buffer.from([0])]) });
    }
    const result = rebuild(region, records), abs = path.resolve(a1).replace(/\\/g, '/');
    if (/["\r\n]/.test(abs)) throw new Error('Chemin de ROM invalide.');
    atomicWrite(path.join(a1, '.storage.bin'), result);
    atomicWrite(path.join(a1, 'load.resc'), 'mem Load "' + abs + '/.storage.bin" 0x' + address.toString(16) + '\n');
    console.log(files.size + ' script(s) empaquete(s).'); return;
  }
  throw new Error('Commande inconnue : ' + cmd);
}
module.exports = { parse, rebuild, findStorage, locateStorage, main, backup, restore, atomicWrite };
