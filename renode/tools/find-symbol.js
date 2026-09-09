// Cherche des symboles dans un ELF ARM 32 bits (firmware NumWorks).
//   node find-symbol.js <fichier.elf> [motif]
// Sert notamment a localiser le stockage des scripts Python :
//   node find-symbol.js epsilon.onboarding.elf storageArea
const fs = require("fs");
const file = process.argv[2];
const pattern = new RegExp(process.argv[3] || "[Ss]torage");
if (!file) { console.error("usage: node find-symbol.js <fichier.elf> [motif]"); process.exit(1); }

const b = fs.readFileSync(file);
if (b.readUInt32LE(0) !== 0x464c457f) { console.error("pas un ELF"); process.exit(1); }
if (b[4] !== 1) { console.error("ELF 64 bits non gere (les firmwares NumWorks sont en 32 bits)"); process.exit(1); }

const shoff = b.readUInt32LE(0x20), shentsize = b.readUInt16LE(0x2E);
const shnum = b.readUInt16LE(0x30), shstrndx = b.readUInt16LE(0x32);
const sh = i => { const o = shoff + i * shentsize; return {
  name: b.readUInt32LE(o), off: b.readUInt32LE(o + 0x10),
  size: b.readUInt32LE(o + 0x14), entsize: b.readUInt32LE(o + 0x24) }; };
const shstr = sh(shstrndx);
const nameAt = i => b.toString("latin1", shstr.off + i, b.indexOf(0, shstr.off + i));

let symtab = null, strtab = null;
for (let i = 0; i < shnum; i++) {
  const s = sh(i), n = nameAt(s.name);
  if (n === ".symtab") symtab = s;
  if (n === ".strtab") strtab = s;
}
if (!symtab) { console.error("aucune table de symboles (.symtab) dans cet ELF"); process.exit(1); }

const out = [];
for (let i = 0; i < symtab.size / symtab.entsize; i++) {
  const o = symtab.off + i * symtab.entsize;
  const ni = b.readUInt32LE(o);
  const name = b.toString("latin1", strtab.off + ni, b.indexOf(0, strtab.off + ni));
  const val = b.readUInt32LE(o + 4), size = b.readUInt32LE(o + 8);
  if (size > 0 && pattern.test(name)) out.push({ name, val, size });
}
out.sort((a, b2) => b2.size - a.size);
if (!out.length) { console.log("aucun symbole ne correspond a /" + pattern.source + "/"); process.exit(0); }

console.log("adresse      taille   symbole");
for (const s of out.slice(0, 20)) {
  console.log("0x" + s.val.toString(16).padStart(8, "0"),
              String(s.size).padStart(8), " " + s.name);
}
console.log("\nPour vider cette zone depuis le moniteur Renode :");
const t = out[0];
console.log('  mem Save "C:/EmuWorks/zone.bin" 0x' + t.val.toString(16) +
            " 0x" + t.size.toString(16));
console.log('  mem Load "C:/EmuWorks/zone.bin" 0x' + t.val.toString(16));
