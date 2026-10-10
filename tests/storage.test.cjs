const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { parse, rebuild, findStorage, locateStorage, main, backup, restore } = require('../renode/tools/storage');
function fixture(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'emuworks-test-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  fs.mkdirSync(path.join(dir, 'scripts'));
  return dir;
}
function blank() {
  const b = Buffer.alloc(0x8014);
  b.writeUInt32LE(0xEE0BDDBA); b.writeUInt32LE(0xEE0BDDBA, 0x8004);
  b.writeUInt32LE(0x2000ebec, 0x8008);
  return b;
}
function dump(region) { const b = Buffer.alloc(0x40000); region.copy(b, 0xcf8); return b; }
function upsilon() {
  const b = Buffer.alloc(64020);
  b.writeUInt32LE(0x12345678, 0); b.writeUInt32LE(0x20001000, 4);
  b.writeUInt32LE(0xEE0BDDBA, 8); b.writeUInt32LE(0xEE0BDDBA, 64012);
  b.writeUInt32LE(0x2001abcd, 64016);
  return b;
}
test('Upsilon supports large scripts, preserves its delegate and clears the preceding cache', t => {
  const dir = fixture(t), bin = path.join(dir, 'sram.bin');
  const code = '# large script\n' + 'x=1\n'.repeat(9000);
  const region = rebuild(upsilon(), [{ name: 'large.py', body: Buffer.concat([Buffer.from([1]), Buffer.from(code), Buffer.from([0])]) }]);
  assert.equal(region.readUInt32LE(0), 0); assert.equal(region.readUInt32LE(4), 0);
  assert.equal(region.readUInt32LE(64012), 0xEE0BDDBA);
  assert.equal(region.readUInt32LE(64016), 0x2001abcd);
  const sram = dump(region);
  sram.writeUInt32LE(0xdeadbeef, 0xcf8 + region.length);
  assert.equal(findStorage(sram), 0xd00);
  assert.equal(locateStorage(sram).start, 0xcf8);
  fs.writeFileSync(bin, sram);
  main(['pull', bin, dir]); main(['push', dir]);
  assert.equal(fs.readFileSync(path.join(dir, 'scripts/large.py'), 'utf8'), code);
  const rebuilt = fs.readFileSync(path.join(dir, '.storage.bin'));
  assert.deepEqual(parse(rebuilt), parse(region));
  assert.equal(rebuilt.length, 64020);
  assert.match(fs.readFileSync(path.join(dir, 'load.resc'), 'utf8'), /0x20000cf8/);
  rebuilt.copy(sram, locateStorage(sram).start);
  assert.equal(sram.readUInt32LE(0xcf8 + region.length), 0xdeadbeef);
});
test('Upsilon corrupt footer, truncated region, overflow and ambiguous layouts are rejected', () => {
  const region = upsilon(), original = Buffer.from(region);
  assert.throws(() => rebuild(region, [{ name: 'full.py', body: Buffer.alloc(64000) }]));
  assert.deepEqual(region, original);
  assert.throws(() => parse(region.subarray(0, -4)));
  const bad = dump(region); bad.writeUInt32LE(0, 0xcf8 + 64012);
  assert.throws(() => findStorage(bad));
  const ambiguous = dump(blank()); upsilon().copy(ambiguous, 0x10000);
  assert.throws(() => findStorage(ambiguous));
});
test('roundtrip Python UTF-8, NUL final, drapeau et records non Python', t => {
  const dir = fixture(t), bin = path.join(dir, 'sram.bin');
  const code = 'print("été")\n';
  fs.writeFileSync(bin, dump(rebuild(blank(), [
    { name: 'test.py', body: Buffer.concat([Buffer.from([1]), Buffer.from(code), Buffer.from([0])]) },
    { name: 'f.func', body: Buffer.from([9, 8, 7]) }
  ])));
  main(['pull', bin, dir]);
  assert.equal(fs.readFileSync(path.join(dir, 'scripts/test.py'), 'utf8'), code);
  main(['push', dir]);
  const region = fs.readFileSync(path.join(dir, '.storage.bin'));
  assert.equal(region.readUInt32LE(0x8008), 0x2000ebec);
  const records = parse(region);
  assert.deepEqual(records.find(r => r.name === 'f.func').body, Buffer.from([9,8,7]));
  assert.equal(records.find(r => r.name === 'test.py').body[0], 1);
  assert.equal(records.find(r => r.name === 'test.py').body.at(-1), 0);
});
test('vidage incomplet, footer corrompu et faux magic rejetes', () => {
  assert.throws(() => findStorage(Buffer.alloc(100)));
  const b = dump(blank()); b.writeUInt32LE(0, 0xcf8 + 0x8004);
  assert.throws(() => findStorage(b));
  const good = dump(blank()); good.writeUInt32LE(0xEE0BDDBA, 0);
  assert.equal(findStorage(good), 0xcf8);
});
test('stockage plein laisse le delegate et le footer intacts', () => {
  const b = blank(), original = Buffer.from(b);
  assert.throws(() => rebuild(b, [{ name:'x.py', body: Buffer.alloc(32760) }]));
  assert.deepEqual(b, original);
});
test('nom traversant et stockage vide ne touchent pas aux scripts', t => {
  const dir = fixture(t), bin = path.join(dir, 'sram.bin');
  fs.writeFileSync(path.join(dir, 'scripts/local.py'), 'keep');
  fs.writeFileSync(bin, dump(rebuild(blank(), [{ name:'../escape.py', body:Buffer.from([0, 0]) }])));
  assert.throws(() => main(['pull', bin, dir]));
  fs.writeFileSync(bin, dump(blank()));
  assert.throws(() => main(['pull', bin, dir]));
  assert.equal(fs.readFileSync(path.join(dir, 'scripts/local.py'), 'utf8'), 'keep');
});
test('historique restaure, sauvegarde alteree refusee', t => {
  const dir = fixture(t), file = path.join(dir, 'scripts/test.py');
  fs.writeFileSync(file, 'version 1'); const saved = backup(dir);
  fs.writeFileSync(file, 'version 2'); restore(dir, saved);
  assert.equal(fs.readFileSync(file, 'utf8'), 'version 1');
  assert.equal(fs.readdirSync(path.join(dir, 'sauvegardes')).length, 2);
  fs.writeFileSync(path.join(saved, 'test.py'), 'corruption');
  assert.throws(() => restore(dir, saved));
  assert.equal(fs.readFileSync(file, 'utf8'), 'version 1');
});
test('echec de remplacement restaure le dossier precedent', t => {
  const dir = fixture(t), file = path.join(dir, 'scripts/test.py');
  fs.writeFileSync(file, 'v1'); const saved = backup(dir); fs.writeFileSync(file, 'v2');
  const rename = fs.renameSync;
  fs.renameSync = (a,b) => {
    if (path.basename(a).startsWith('.scripts-') && !a.endsWith('.scripts-rollback') && b.endsWith('scripts')) throw Error('panne simulee');
    return rename(a,b);
  };
  try { assert.throws(() => restore(dir, saved)); }
  finally { fs.renameSync = rename; }
  assert.equal(fs.readFileSync(file, 'utf8'), 'v2');
});
test('reprise apres interruption entre les deux renommages', t => {
  const dir = fixture(t);
  fs.writeFileSync(path.join(dir, 'scripts/test.py'), 'recupere');
  fs.renameSync(path.join(dir, 'scripts'), path.join(dir, '.scripts-rollback'));
  backup(dir);
  assert.equal(fs.readFileSync(path.join(dir, 'scripts/test.py'), 'utf8'), 'recupere');
});
