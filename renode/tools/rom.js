// Synchronise un dossier avec le contenu de la calculatrice emulee.
//
//   node rom.js pull <sram.bin> <dossier>   la calculatrice -> le dossier
//   node rom.js push <dossier>              le dossier -> image recharcheable
//
// "pull" cherche tout seul le magic du stockage dans un vidage de SRAM complet,
// donc ca marche quel que soit le firmware (Omega, Epsilon, un fork...).
// "push" reconstruit la zone de stockage et genere le script Renode qui la
// recharge a la bonne adresse.

const fs = require("fs");
const path = require("path");
const MAGIC = 0xEE0BDDBA;
const BUF_OFF = 4;          // debut du tampon d enregistrements
const BUF_SIZE = 32768;     // Storage::k_storageSize
const FOOTER_OFF = 32772;   // magic de fin
const SRAM_BASE = 0x20000000;

const [, , cmd, a1, a2, a3] = process.argv;

function findStorage(sram) {
  for (let i = 0; i + 8 <= sram.length; i += 4) {
    if (sram.readUInt32LE(i) !== MAGIC) continue;
    // plausible si on sait parcourir au moins un enregistrement ou tomber sur le terminateur
    const size = sram.readUInt16LE(i + 4);
    if (size === 0 || (size > 2 && i + 4 + size < sram.length)) return i;
  }
  return -1;
}

function parse(region) {
  const recs = [];
  let p = 4;
  while (p + 2 <= region.length) {
    const size = region.readUInt16LE(p);
    if (size === 0) break;
    const e = region.indexOf(0, p + 2);
    if (e < 0 || e >= p + size) break;
    recs.push({ name: region.toString("latin1", p + 2, e), body: region.slice(e + 1, p + size) });
    p += size;
  }
  return recs;
}

function rebuild(region, recs) {
  const out = Buffer.from(region);
  // n efface QUE le tampon d enregistrements : au-dela vivent le magic de fin
  // (0x8004), le delegue (0x8008) et le cache (0x800C/0x8010) d Epsilon.
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
  // invalide le cache de recherche pour qu Epsilon reparcoure la liste
  if(out.length >= 0x8014) { out.writeUInt32LE(0, 0x800C); out.writeUInt32LE(0, 0x8010); }
  if(out.readUInt32LE(FOOTER_OFF) !== MAGIC) {
    console.error("ATTENTION : magic de fin absent, l image serait rejetee par Epsilon.");
  }
  return out;
}

const REGION = 0x8014;   // taille de Ion::staticStorageArea

if (cmd === "pull") {
  if (!a1 || !a2) { console.error("usage: node rom.js pull <sram.bin> <dossier>"); process.exit(1); }
  if (!fs.existsSync(a1)) { console.error("Vidage SRAM introuvable : " + a1); process.exit(1); }
  const sram = fs.readFileSync(a1);
  const off = findStorage(sram);
  if (off < 0) { console.error("Stockage introuvable dans ce vidage de SRAM."); process.exit(1); }
  const addr = SRAM_BASE + off;
  const region = sram.slice(off, off + REGION);
  const dir = path.join(a2, "scripts");
  fs.mkdirSync(dir, { recursive: true });

  const recs = parse(region);
  const flags = {};
  // Garde-fou : un stockage vide alors que le dossier contient des scripts
  // signifie presque toujours que la calculatrice a plante avant de les lire.
  // On refuse alors d effacer le dossier -- sauf --force, pour le cas ou on a
  // vraiment tout supprime sur la calculatrice.
  const existants = fs.readdirSync(dir);
  if (recs.length === 0 && existants.length > 0 && a3 !== "--force") {
    console.error("Stockage vide alors que " + dir + " contient " + existants.length + " fichier(s).");
    console.error("Le dossier est laisse intact. Pour effacer quand meme : ajouter --force");
    process.exit(2);
  }
  for (const f of existants) fs.unlinkSync(path.join(dir, f));
  for (const r of recs) {
    fs.writeFileSync(path.join(dir, r.name), r.body.slice(1));
    flags[r.name] = r.body[0];
  }
  fs.writeFileSync(path.join(a2, ".storage.bin"), region);
  fs.writeFileSync(path.join(a2, ".storage.json"),
    JSON.stringify({ address: "0x" + addr.toString(16), flags }, null, 2));
  console.log("stockage trouve a 0x" + addr.toString(16) + " (offset SRAM 0x" + off.toString(16) + ")");
  console.log(recs.length + " fichier(s) ecrit(s) dans " + dir);
  for (const r of recs) console.log("   " + r.name + "  (" + (r.body.length - 1) + " o)");

} else if (cmd === "push") {
  if (!a1) { console.error("usage: node rom.js push <dossier>"); process.exit(1); }
  const metaPath = path.join(a1, ".storage.json");
  if (!fs.existsSync(metaPath)) {
    console.error("Fais d'abord un 'pull' : il faut une image de reference du stockage.");
    process.exit(1);
  }
  const meta = JSON.parse(fs.readFileSync(metaPath, "utf8"));
  const region = fs.readFileSync(path.join(a1, ".storage.bin"));
  const dir = path.join(a1, "scripts");
  const recs = [];
  for (const name of fs.readdirSync(dir).sort()) {
    const code = fs.readFileSync(path.join(dir, name));
    const flag = meta.flags[name] === undefined ? 0 : meta.flags[name];
    recs.push({ name, body: Buffer.concat([Buffer.from([flag]), code]) });
  }
  fs.writeFileSync(path.join(a1, ".storage.bin"), rebuild(region, recs));
  const abs = path.resolve(a1).split(String.fromCharCode(92)).join("/");
  fs.writeFileSync(path.join(a1, "load.resc"),
    'mem Load "' + abs + '/.storage.bin" ' + meta.address + "\n");
  console.log(recs.length + " script(s) empaquete(s) -> " + a1 + "/.storage.bin");
  for (const r of recs) console.log("   " + r.name);
  console.log("\nDans Renode :  runMacro $pousser");

} else if (cmd === "ref") {
  // Rafraichit UNIQUEMENT l image de reference (.storage.bin/.storage.json) a
  // partir d un vidage de SRAM. Ne touche pas a scripts/ : c est ce qu il faut
  // au demarrage, quand le dossier fait autorite et pas la calculatrice.
  if (!a1 || !a2) { console.error("usage: node rom.js ref <sram.bin> <dossier>"); process.exit(1); }
  const sram = fs.readFileSync(a1);
  const off = findStorage(sram);
  if (off < 0) { console.error("Stockage introuvable dans ce vidage de SRAM."); process.exit(1); }
  const addr = SRAM_BASE + off;
  const region = sram.slice(off, off + REGION);
  const dir = path.join(a2, "scripts");
  fs.mkdirSync(dir, { recursive: true });
  // on garde les drapeaux deja connus, on complete avec ceux de la calculatrice
  const metaPath = path.join(a2, ".storage.json");
  const flags = fs.existsSync(metaPath) ? (JSON.parse(fs.readFileSync(metaPath, "utf8")).flags || {}) : {};
  for (const r of parse(region)) if (flags[r.name] === undefined) flags[r.name] = r.body[0];
  fs.writeFileSync(path.join(a2, ".storage.bin"), region);
  fs.writeFileSync(metaPath, JSON.stringify({ address: "0x" + addr.toString(16), flags }, null, 2));
  console.log("reference : stockage a 0x" + addr.toString(16));

} else {
  console.error("usage:\n  node rom.js pull <sram.bin> <dossier>\n  node rom.js push <dossier>\n  node rom.js ref  <sram.bin> <dossier>");
  process.exit(1);
}
