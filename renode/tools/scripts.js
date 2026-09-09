// Manipule les scripts Python d'une NumWorks depuis un vidage memoire.
//
//   node scripts.js list  <storage.bin>
//   node scripts.js get   <storage.bin> <nom.py> [sortie.py]
//   node scripts.js add   <storage.bin> <fichier.py> [nom.py]
//   node scripts.js del   <storage.bin> <nom.py>
//
// Le vidage s'obtient depuis le moniteur Renode (adresse via find-symbol.js) :
//   mem Save "C:/NumWorks/storage.bin" 0x20000cf8 0x8014
//   mem Load "C:/NumWorks/storage.bin" 0x20000cf8
//
// Format Epsilon : Magic | [taille uint16 | nom\0 | corps]... | 0x0000 | Magic
// Le 1er octet du corps d'un script est le drapeau d'importation automatique.

const fs = require("fs");
const MAGIC = 0xEE0BDDBA;
const BUF_OFF = 4, BUF_SIZE = 32768, FOOTER_OFF = 32772;

const [, , cmd, file, arg3, arg4] = process.argv;
if (!cmd || !file) {
  console.error(fs.readFileSync(__filename, "utf8").split("\n").slice(2, 14).join("\n").replace(/^\/\/ ?/gm, ""));
  process.exit(1);
}

const buf = fs.readFileSync(file);
if (buf.readUInt32LE(0) !== MAGIC) {
  console.error("Ce fichier ne commence pas par le magic 0xEE0BDDBA.");
  console.error("Verifie l'adresse du vidage (node find-symbol.js firmware.elf staticStorageArea).");
  process.exit(1);
}

function parse(b) {
  const recs = [];
  let p = 4;
  while (p + 2 <= b.length) {
    const size = b.readUInt16LE(p);
    if (size === 0) break;
    const e = b.indexOf(0, p + 2);
    if (e < 0 || e >= p + size) break;
    recs.push({ name: b.toString("latin1", p + 2, e), body: b.slice(e + 1, p + size), start: p, size });
    p += size;
  }
  return { recs, end: p };
}

function rebuild(b, recs) {
  const out = Buffer.from(b);
  // n efface QUE le tampon : au-dela vivent le magic de fin, le delegue et le cache
  out.fill(0, BUF_OFF, BUF_OFF + BUF_SIZE);
  let p = 4;
  for (const r of recs) {
    const size = 2 + r.name.length + 1 + r.body.length;
    if (p + size + 2 > out.length) { console.error("Stockage plein (32 Ko)."); process.exit(1); }
    out.writeUInt16LE(size, p);
    out.write(r.name, p + 2, "latin1");
    out[p + 2 + r.name.length] = 0;
    r.body.copy(out, p + 2 + r.name.length + 1);
    p += size;
  }
  out.writeUInt16LE(0, p);
  if(out.length >= 0x8014) { out.writeUInt32LE(0, 0x800C); out.writeUInt32LE(0, 0x8010); }
  if(out.length > FOOTER_OFF && out.readUInt32LE(FOOTER_OFF) !== MAGIC) {
    console.error("ATTENTION : magic de fin absent, image rejetee par Epsilon.");
  }
  return out;
}

const { recs, end } = parse(buf);
const used = end - 4, total = buf.length - 8;

if (cmd === "list") {
  if (!recs.length) console.log("(aucun script)");
  for (const r of recs) {
    const auto = r.body[0] === 1 ? " [import auto]" : "";
    console.log(String(r.size).padStart(6) + " o  " + r.name + auto);
  }
  console.log("\n" + used + " / " + total + " octets utilises");

} else if (cmd === "get") {
  const r = recs.find(x => x.name === arg3);
  if (!r) { console.error("Script introuvable : " + arg3); process.exit(1); }
  const code = r.body.slice(1);        // on saute le drapeau
  if (arg4) { fs.writeFileSync(arg4, code); console.log(arg3 + " -> " + arg4 + " (" + code.length + " o)"); }
  else process.stdout.write(code.toString("latin1"));

} else if (cmd === "add") {
  if (!arg3) { console.error("Indique le fichier .py a ajouter."); process.exit(1); }
  const name = arg4 || require("path").basename(arg3);
  const code = fs.readFileSync(arg3);
  const body = Buffer.concat([Buffer.from([0]), code]);
  const kept = recs.filter(r => r.name !== name);
  kept.push({ name, body });
  fs.writeFileSync(file, rebuild(buf, kept));
  console.log("ajoute : " + name + " (" + code.length + " o de code) dans " + file);

} else if (cmd === "del") {
  const kept = recs.filter(r => r.name !== arg3);
  if (kept.length === recs.length) { console.error("Script introuvable : " + arg3); process.exit(1); }
  fs.writeFileSync(file, rebuild(buf, kept));
  console.log("supprime : " + arg3);

} else {
  console.error("Commande inconnue : " + cmd);
  process.exit(1);
}
