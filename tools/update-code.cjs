// SPDX-License-Identifier: MIT
// Import the committed firmware and dependency notices from a local emuworks-code checkout.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
function main() {
  if (!process.argv[2]) throw Error('Usage: node tools/update-code.cjs <path-to-emuworks-code>');
  const source = path.resolve(process.argv[2]);
  const git = (...args) => execFileSync('git', ['-C', source, ...args], { windowsHide: true });
  if (git('status', '--porcelain').toString().trim()) throw Error('Commit Code changes before importing.');
  const revision = git('rev-parse', 'HEAD').toString().trim();
  const images = ['internal.bin', 'external.bin'];
  const contents = Object.fromEntries(images.map(name => [name, git('show', 'HEAD:bundled/' + name)]));
  const license = git('show', 'HEAD:LICENSE');
  const noticePaths = git('ls-tree', '-r', '--name-only', 'HEAD', 'licenses').toString().trim().split('\n').filter(Boolean);
  const notices = noticePaths.map(file => [path.basename(file), git('show', 'HEAD:' + file)]);
  const thirdParty = git('show', 'HEAD:THIRD_PARTY_NOTICES.md');
  if (!license.toString().includes('MIT License')) throw Error('Expected an MIT license for the original Code sources.');
  if (contents['internal.bin'].subarray(0x100, 0x111).toString() !== 'EMUWORKS_CODE_V1\0') throw Error('Unrecognized Code firmware image.');
  if (!contents['external.bin'].length) throw Error('Empty external image.');
  const destination = path.join(__dirname, '../firmware/code');
  fs.mkdirSync(path.join(destination, 'bundled'), { recursive: true });
  const hashes = {};
  for (const name of images) {
    fs.writeFileSync(path.join(destination, 'bundled', name), contents[name]);
    hashes[name] = crypto.createHash('sha256').update(contents[name]).digest('hex');
  }
  fs.writeFileSync(path.join(destination, 'LICENSE'), license);
  fs.mkdirSync(path.join(destination, 'licenses'), { recursive: true });
  for (const [name, data] of notices) fs.writeFileSync(path.join(destination, 'licenses', name), data);
  fs.writeFileSync(path.join(destination, 'THIRD_PARTY_NOTICES.md'), thirdParty);
  fs.writeFileSync(path.join(destination, 'source.json'), JSON.stringify({
    repository: 'https://github.com/nat649/emuworks-code', revision, license: 'MIT (original code); see THIRD_PARTY_NOTICES.md', sha256: hashes
  }, null, 2) + '\n');
  console.log('Code imported: ' + revision + '\nRebuild EmuWorks to embed this version.');
}
try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
