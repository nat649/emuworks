// SPDX-License-Identifier: MIT
// Import the committed MIT firmware from a local emuworks-core checkout.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
function main() {
  if (!process.argv[2]) throw Error('Usage: node tools/update-core.cjs <chemin-du-depot-emuworks-core>');
  const source = path.resolve(process.argv[2]);
  const git = (...args) => execFileSync('git', ['-C', source, ...args], { windowsHide: true });
  if (git('status', '--porcelain').toString().trim()) throw Error('Committer les changements de Core avant l’import.');
  const revision = git('rev-parse', 'HEAD').toString().trim();
  const images = ['internal.bin', 'external.bin'];
  const contents = Object.fromEntries(images.map(name => [name, git('show', 'HEAD:bundled/' + name)]));
  const license = git('show', 'HEAD:LICENSE');
  if (!license.toString().includes('MIT License')) throw Error('Licence MIT attendue dans le depot Core.');
  if (contents['internal.bin'].subarray(0x100, 0x111).toString() !== 'EMUWORKS_CORE_V1\0') throw Error('Image Core non reconnue.');
  if (!contents['external.bin'].length) throw Error('Image externe vide.');
  const destination = path.join(__dirname, '../firmware/core');
  fs.mkdirSync(path.join(destination, 'bundled'), { recursive: true });
  const hashes = {};
  for (const name of images) {
    fs.writeFileSync(path.join(destination, 'bundled', name), contents[name]);
    hashes[name] = crypto.createHash('sha256').update(contents[name]).digest('hex');
  }
  fs.writeFileSync(path.join(destination, 'LICENSE'), license);
  fs.writeFileSync(path.join(destination, 'source.json'), JSON.stringify({
    repository: 'https://github.com/nat649/emuworks-core', revision, license: 'MIT', sha256: hashes
  }, null, 2) + '\n');
  console.log('Core importe : ' + revision + '\nRecompiler EmuWorks pour embarquer cette version.');
}
try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
