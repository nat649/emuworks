// Test local optionnel : firmware fourni par l'utilisateur, jamais ajoute au depot.
// node tests/renode.integration.cjs <Renode.exe> <dossier contenant les deux .bin>
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { spawn } = require('node:child_process');
const { once } = require('node:events');
const assert = require('node:assert/strict');
const storage = require('../renode/tools/storage');
const delay = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const [exe, firmware] = process.argv.slice(2);
  if (!exe || !firmware) throw Error('Indiquer Renode.exe et le dossier du firmware.');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'EmuWorksIntegration-'));
  if (dir.includes(' ')) throw Error('Le dossier temporaire doit etre sans espace.');
  fs.cpSync(path.join(__dirname, '../renode'), path.join(dir, 'renode'), { recursive: true });
  const rom = path.join(dir, 'rom');
  fs.mkdirSync(path.join(rom, 'scripts'), { recursive: true });
  for (const name of ['internal.bin', 'external.bin']) fs.copyFileSync(path.join(firmware, name), path.join(rom, name));
  const code = 'print("EmuWorks integration")\n';
  fs.writeFileSync(path.join(rom, 'scripts/verification.py'), code);
  const dump = path.join(rom, 'session.bin');
  const child = spawn(exe, ['--console', '--disable-xwt', '--hide-log', '-e', 'i @numworks-embarque.resc'], {
    cwd: path.join(dir, 'renode'), windowsHide: true,
    env: { ...process.env, EMUWORKS_BASE: dir, EMUWORKS_SESSION_SRAM: dump }
  });
  let logs = '', exited = false, socket;
  child.stdout.on('data', b => { logs += b; }); child.stderr.on('data', b => { logs += b; });
  const exit = once(child, 'exit').then(([code]) => { exited = true; return code; });
  try {
    const end = Date.now() + 100000;
    while (Date.now() < end && !exited) {
      socket = new net.Socket();
      try { await new Promise((resolve, reject) => { socket.once('error', reject); socket.connect(3555, '127.0.0.1', resolve); }); break; }
      catch { socket.destroy(); socket = null; await delay(300); }
    }
    assert(socket, 'Pas de serveur ecran :\n' + logs);
    let bytes = 0;
    await new Promise((resolve, reject) => {
      socket.setTimeout(10000, () => reject(Error('Trame incomplete')));
      socket.on('error', reject);
      socket.on('data', b => { bytes += b.length; if (bytes === 320*240*2) resolve(); });
      socket.write(Buffer.from([1]));
    });
    socket.destroy();
    child.stdin.write('pause\nmem SaveSram "' + dump.replace(/\\/g, '/') + '"\nquit\n');
    const result = await Promise.race([exit, delay(15000).then(() => { throw Error('Arret Renode bloque'); })]);
    assert.equal(result, 0, logs);
    const sram = fs.readFileSync(dump), { region } = storage.locateStorage(sram);
    const records = storage.parse(region);
    assert.equal(records.find(r => r.name === 'verification.py').body.subarray(1, -1).toString(), code);
    storage.main(['pull', dump, rom]);
    assert.equal(fs.readFileSync(path.join(rom, 'scripts/verification.py'), 'utf8'), code);
    const saved = storage.backup(rom);
    fs.writeFileSync(path.join(rom, 'scripts/verification.py'), 'changed');
    storage.restore(rom, saved);
    assert.equal(fs.readFileSync(path.join(rom, 'scripts/verification.py'), 'utf8'), code);
    console.log('PASS Renode : compilation peripheriques, amorcage, trame, injection, sauvegarde, restauration');
  } finally {
    socket?.destroy();
    if (!exited) { child.kill(); await exit; }
    fs.writeFileSync(path.join(dir, 'integration.log'), logs);
    console.log('Donnees et journal du test : ' + dir);
  }
})().catch(e => { console.error(e); process.exitCode = 1; });
