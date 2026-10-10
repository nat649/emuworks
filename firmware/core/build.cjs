// SPDX-License-Identifier: MIT. Requires arm-none-eabi GCC and binutils in PATH or ARM_GCC_BIN.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = __dirname, out = path.join(root, 'build');
fs.mkdirSync(out, { recursive: true });
function tool(name, args) {
  const exe = process.env.ARM_GCC_BIN ? path.join(process.env.ARM_GCC_BIN, name + (process.platform === 'win32' ? '.exe' : '')) : name;
  execFileSync(exe, args, { cwd: root, stdio: 'inherit', windowsHide: true });
}
const flags = ['-mcpu=cortex-m7', '-mthumb', '-mfpu=fpv5-sp-d16', '-mfloat-abi=hard',
  '-Os', '-ffreestanding', '-fno-builtin', '-fno-stack-protector', '-ffunction-sections', '-fdata-sections',
  '-Wall', '-Wextra', '-Werror', '-nostdlib', '-Wl,--gc-sections', '-T', 'linker.ld'];
tool('arm-none-eabi-gcc', [...flags, 'startup.S', 'calc.c', 'main.c', '-o', path.join(out, 'core.elf')]);
tool('arm-none-eabi-objcopy', ['-O', 'binary', path.join(out, 'core.elf'), path.join(out, 'internal.bin')]);
// Current emulator format expects an external image even when all code fits internally.
fs.writeFileSync(path.join(out, 'external.bin'), Buffer.from('EmuWorks Core 0.1\0', 'ascii'));
tool('arm-none-eabi-size', [path.join(out, 'core.elf')]);
tool('arm-none-eabi-gcc', [...flags, 'startup.S', 'calc.c', 'tests.c', '-o', path.join(out, 'tests.elf')]);
tool('arm-none-eabi-objcopy', ['-O', 'binary', path.join(out, 'tests.elf'), path.join(out, 'tests.bin')]);
if (process.argv.includes('--bundle')) {
  const bundled = path.join(root, 'bundled');
  fs.mkdirSync(bundled, { recursive: true });
  for (const file of ['internal.bin', 'external.bin']) fs.copyFileSync(path.join(out, file), path.join(bundled, file));
}
console.log('Firmware original : ' + out);
