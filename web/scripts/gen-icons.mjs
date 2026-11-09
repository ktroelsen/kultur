// Generates the PWA / home-screen icons from the Kultur logo (no dependencies).
//   node scripts/gen-icons.mjs
// Draws the logo (dark rounded square + violet ticket, 64-unit design grid, as favicon.svg) with
// 4×4 supersampling and writes PNGs to public/icons/.
import { mkdirSync, writeFileSync } from 'node:fs';
import { deflateSync } from 'node:zlib';

const BG = [0x12, 0x0f, 0x17];
const TILE = [0x24, 0x1e, 0x2e];
const LINE = [0xc4, 0xa1, 0xff];

// The ticket: a rectangle with a round notch in each short side and a perforation line.
function inTicket(x, y) {
  if (!insideRoundedRect2(x, y, 12, 20, 40, 24, 2)) return false;
  if (Math.hypot(x - 12, y - 32) < 5 || Math.hypot(x - 52, y - 32) < 5) return false;
  const perforated = Math.abs(x - 40) <= 1.25 && y >= 22 && y <= 42 && (y - 22) % 6 < 3;
  return !perforated;
}

function insideRoundedRect2(x, y, x0, y0, w, h, r) {
  const cx = Math.max(x0 + r, Math.min(x, x0 + w - r));
  const cy = Math.max(y0 + r, Math.min(y, y0 + h - r));
  return Math.hypot(x - cx, y - cy) <= r;
}

function insideRoundedRect(x, y, x0, y0, size, r) {
  const cx = Math.max(x0 + r, Math.min(x, x0 + size - r));
  const cy = Math.max(y0 + r, Math.min(y, y0 + size - r));
  return Math.hypot(x - cx, y - cy) <= r;
}

// maskable: full-bleed background, logo shrunk into the 80 % safe zone.
function render(size, { maskable = false } = {}) {
  const px = new Uint8Array(size * size * 4);
  const scale = maskable ? 0.72 : 1;
  const offset = (64 - 64 * scale) / 2;
  const S = 4;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let sy = 0; sy < S; sy++) {
        for (let sx = 0; sx < S; sx++) {
          const ux = ((x + (sx + 0.5) / S) / size) * 64;
          const uy = ((y + (sy + 0.5) / S) / size) * 64;
          let col = maskable ? BG : null;
          const lx = (ux - offset) / scale, ly = (uy - offset) / scale;
          const inTile = lx >= 0 && ly >= 0 && lx <= 64 && ly <= 64 && insideRoundedRect(lx, ly, 0, 0, 64, 14);
          if (inTile) col = TILE;
          if (inTile || maskable) {
            if (inTicket(lx, ly)) col = LINE;
          }
          if (col) { r += col[0]; g += col[1]; b += col[2]; a += 255; }
        }
      }
      const n = S * S, i = (y * size + x) * 4;
      const cov = a / 255;
      px[i] = cov ? r / cov : 0; px[i + 1] = cov ? g / cov : 0; px[i + 2] = cov ? b / cov : 0; px[i + 3] = a / n;
    }
  }
  return png(size, px);
}

const CRC = new Int32Array(256).map((_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c;
});
const crc32 = (buf) => {
  let c = -1;
  for (const byte of buf) c = CRC[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
};
function chunk(type, data) {
  const out = Buffer.alloc(12 + data.length);
  out.writeUInt32BE(data.length, 0);
  out.write(type, 4, 'ascii');
  data.copy(out, 8);
  out.writeUInt32BE(crc32(out.subarray(4, 8 + data.length)), 8 + data.length);
  return out;
}
function png(size, rgba) {
  const raw = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0; y < size; y++) {
    raw[y * (size * 4 + 1)] = 0; // filter: none
    Buffer.from(rgba.buffer, y * size * 4, size * 4).copy(raw, y * (size * 4 + 1) + 1);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0); ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw, { level: 9 })), chunk('IEND', Buffer.alloc(0)),
  ]);
}

mkdirSync(new URL('../public/icons/', import.meta.url), { recursive: true });
const out = (name, buf) => writeFileSync(new URL(`../public/icons/${name}`, import.meta.url), buf);
out('icon-192.png', render(192));
out('icon-512.png', render(512));
out('maskable-512.png', render(512, { maskable: true }));
out('apple-touch-icon.png', render(180, { maskable: true })); // iOS rounds the corners itself
console.log('icons written to public/icons/');
