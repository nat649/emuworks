// SPDX-License-Identifier: MIT. No proprietary firmware needed.
const fs = require('node:fs'), path = require('node:path'), os = require('node:os');
const { execFileSync, spawn } = require('node:child_process');
const assert = require('node:assert/strict');
const root = path.join(__dirname, '..'), build = path.join(root, 'firmware/core/build');
const renode = process.argv[2];
const nm = path.join(process.env.ARM_GCC_BIN || '', 'arm-none-eabi-nm' + (process.platform === 'win32' ? '.exe' : ''));
function symbols(file) {
  const text = execFileSync(nm, [file], { encoding: 'utf8', windowsHide: true });
  const map = {};
  for (const line of text.split('\n')) { const match = line.match(/^([0-9a-f]+)\s+\w\s+(\w+)/); if (match) map[match[2]] = parseInt(match[1], 16) - 0x20000000; }
  return map;
}
function run(dir, script) {
  return new Promise((resolve, reject) => {
    fs.writeFileSync(path.join(dir, 'renode/test.resc'), script.join('\n') + '\n');
    const p = spawn(renode, ['--console', '--disable-xwt', '--hide-log', '-e', 'i @test.resc'], {
      cwd: path.join(dir, 'renode'), windowsHide: true, env: {...process.env, EMUWORKS_BASE: dir}
    });
    let log = '';
    p.stdout.on('data', b => log += b); p.stderr.on('data', b => log += b);
    p.on('error', reject);
    const timer = setTimeout(() => { p.kill(); reject(Error('Renode timeout\n' + log)); }, 90000);
    p.on('exit', code => { clearTimeout(timer); fs.writeFileSync(path.join(dir, 'test.log'), log); code ? reject(Error(log)) : resolve(log); });
  });
}
(async () => {
  assert(renode, 'Indiquer le chemin de Renode.exe');
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'EmuWorksCore-'));
  assert(!dir.includes(' '), 'Dossier temporaire sans espace necessaire');
  fs.cpSync(path.join(root, 'renode'), path.join(dir, 'renode'), { recursive: true });
  fs.mkdirSync(path.join(dir, 'rom'));
  fs.copyFileSync(path.join(build, 'external.bin'), path.join(dir, 'rom/external.bin'));
  const setup = [ 'i @NumWorksDisplay.cs', 'i @NumWorksKeyboard.cs', 'i @NumWorksCrc.cs', 'i @NumWorksAdc.cs', 'i @MemFile.cs',
    'mach add "core"', 'mach set "core"', 'machine LoadPlatformDescription @numworks_n0110.repl',
    'sysbus LoadBinary @../rom/internal.bin 0x08000000', 'cpu VectorTableOffset 0x08000000' ];
  const snapshot = name => 'mem SaveSram "' + name + '"';
  fs.copyFileSync(path.join(build, 'tests.bin'), path.join(dir, 'rom/internal.bin'));
  await run(dir, [...setup, 'emulation RunFor "0.01"', snapshot('unit.bin'), 'quit']);
  const report = symbols(path.join(build, 'tests.elf')).test_report;
  const memory = fs.readFileSync(path.join(dir, 'unit.bin'));
  assert.equal(memory.readUInt32LE(report), 0xC0DEC0DE, 'Tests ARM non termines');
  assert.equal(memory.readUInt32LE(report+8), 0, 'Echec du test ARM ' + memory.readUInt32LE(report+12));
  console.log('PASS ' + memory.readUInt32LE(report+4) + ' calculs/erreurs/formats sur Cortex-M7');
  fs.copyFileSync(path.join(build, 'internal.bin'), path.join(dir, 'rom/internal.bin'));
  const script = [...setup, 'emulation RunFor "0.05"', 'lcd Dump "' + path.join(dir,'home.raw').replace(/\\/g,'/') + '"'];
  function keys(names) { for (const name of names.split(' ')) script.push('keyboard TapKey "' + name + '"', 'emulation RunFor "0.03"'); }
  keys('LEFTPARENTHESIS TWO PLUS THREE RIGHTPARENTHESIS MULTIPLICATION FOUR EXE');
  script.push(snapshot('twenty.bin'));
  keys('UP'); script.push(snapshot('recall.bin'));
  keys('BACK ONE DIVISION ZERO EXE'); script.push(snapshot('zero.bin'));
  keys('BACK TWO POWER THREE POWER TWO EXE'); script.push(snapshot('power.bin'));
  script.push('lcd Dump "' + path.join(dir,'result.raw').replace(/\\/g,'/') + '"', 'quit');
  await run(dir, script);
  const sym = symbols(path.join(build, 'core.elf'));
  assert.equal(fs.readFileSync(path.join(dir,'twenty.bin')).readFloatLE(sym.ans), 20);
  const recalled = fs.readFileSync(path.join(dir,'recall.bin'));
  assert.equal(recalled.subarray(sym.input, recalled.indexOf(0, sym.input)).toString(), '(2+3)*4');
  assert.equal(fs.readFileSync(path.join(dir,'zero.bin')).readUInt32LE(sym.last_error), 2);
  const power = fs.readFileSync(path.join(dir,'power.bin'));
  assert.equal(power.readFloatLE(sym.ans), 512);
  assert.equal(power.readUInt32LE(sym.history_count), 2);
  console.log('PASS clavier matriciel, priorites, historique, division par zero, puissance');
  execFileSync(process.execPath, [path.join(root, 'renode/tools/png.js'), path.join(dir, 'result.raw'), path.join(dir, 'result.png')]);
  console.log('Capture : ' + path.join(dir, 'result.png'));
})().catch(e => { console.error(e); process.exitCode = 1; });
