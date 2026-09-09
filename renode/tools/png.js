// Convertit une capture brute RGB888 de l'ecran (320x240) en PNG.
//   node png.js ecran.raw ecran.png
const fs = require("fs"), zlib = require("zlib");
const [, , src, dst] = process.argv;
const W = 320, H = 240;
const raw = fs.readFileSync(src);
const lines = Buffer.alloc((W * 3 + 1) * H);
for (let y = 0; y < H; y++) {
  lines[y * (W * 3 + 1)] = 0;                                   // filtre "none"
  raw.copy(lines, y * (W * 3 + 1) + 1, y * W * 3, (y + 1) * W * 3);
}
const crcTable = [];
for (let n = 0; n < 256; n++) { let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1;
  crcTable[n] = c >>> 0; }
const crc = b => { let c = 0xFFFFFFFF;
  for (const x of b) c = crcTable[(c ^ x) & 0xFF] ^ (c >>> 8);
  return (c ^ 0xFFFFFFFF) >>> 0; };
const chunk = (type, data) => {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, "latin1"), data]);
  const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
  return Buffer.concat([len, td, c]); };
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(W, 0); ihdr.writeUInt32BE(H, 4);
ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
fs.writeFileSync(dst, Buffer.concat([
  Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
  chunk("IHDR", ihdr),
  chunk("IDAT", zlib.deflateSync(lines)),
  chunk("IEND", Buffer.alloc(0))]));
console.log(dst + " ecrit (" + W + "x" + H + ")");
