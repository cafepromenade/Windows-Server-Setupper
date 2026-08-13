'use strict';

const fs = require('node:fs');
const path = require('node:path');

const SIZES = Object.freeze([16, 24, 32, 48, 64, 128, 256]);
const destination = path.join(__dirname, '..', 'assets', 'app.ico');

function pixel(size, x, y) {
  const nx = (x + 0.5) / size;
  const ny = (y + 0.5) / size;
  const radius = Math.min(nx, 1 - nx, ny, 1 - ny);
  if (radius < 0.075 && ((nx < 0.075 || nx > 0.925) && (ny < 0.075 || ny > 0.925))) return [0, 0, 0, 0];

  let color = [17, 42, 70, 255];
  if (nx > 0.13 && nx < 0.87 && ny > 0.17 && ny < 0.85) color = [31, 95, 145, 255];
  if ((Math.abs(nx - 0.13) < 0.035 || Math.abs(nx - 0.87) < 0.035) && ny > 0.17 && ny < 0.85) color = [114, 215, 255, 255];
  if ((Math.abs(ny - 0.17) < 0.035 || Math.abs(ny - 0.85) < 0.035) && nx > 0.13 && nx < 0.87) color = [114, 215, 255, 255];

  const inEnvelope = nx > 0.24 && nx < 0.76 && ny > 0.38 && ny < 0.70;
  if (inEnvelope) color = [248, 251, 255, 255];
  const diagonal = Math.abs(ny - (0.38 + Math.abs(nx - 0.5) * 0.62)) < 0.035;
  if (inEnvelope && diagonal) color = [17, 42, 70, 255];

  const arrowStem = Math.abs(nx - 0.5) < 0.04 && ny > 0.18 && ny < 0.48;
  const arrowWing = ny > 0.38 && ny < 0.53 && Math.abs(Math.abs(nx - 0.5) - (0.53 - ny)) < 0.045;
  if (arrowStem || arrowWing) color = [114, 215, 255, 255];
  return color;
}

function dib(size) {
  const xorBytes = size * size * 4;
  const maskStride = Math.ceil(size / 32) * 4;
  const maskBytes = maskStride * size;
  const buffer = Buffer.alloc(40 + xorBytes + maskBytes);
  buffer.writeUInt32LE(40, 0);
  buffer.writeInt32LE(size, 4);
  buffer.writeInt32LE(size * 2, 8);
  buffer.writeUInt16LE(1, 12);
  buffer.writeUInt16LE(32, 14);
  buffer.writeUInt32LE(0, 16);
  buffer.writeUInt32LE(xorBytes, 20);
  buffer.writeInt32LE(2835, 24);
  buffer.writeInt32LE(2835, 28);

  for (let row = 0; row < size; row += 1) {
    const y = size - 1 - row;
    for (let x = 0; x < size; x += 1) {
      const [r, g, b, a] = pixel(size, x, y);
      const offset = 40 + (row * size + x) * 4;
      buffer[offset] = b;
      buffer[offset + 1] = g;
      buffer[offset + 2] = r;
      buffer[offset + 3] = a;
      if (a === 0) {
        const maskOffset = 40 + xorBytes + row * maskStride + Math.floor(x / 8);
        buffer[maskOffset] |= 0x80 >> (x % 8);
      }
    }
  }
  return buffer;
}

const images = SIZES.map((size) => ({ size, data: dib(size) }));
const headerSize = 6 + images.length * 16;
const output = Buffer.alloc(headerSize + images.reduce((total, image) => total + image.data.length, 0));
output.writeUInt16LE(0, 0);
output.writeUInt16LE(1, 2);
output.writeUInt16LE(images.length, 4);

let imageOffset = headerSize;
images.forEach((image, index) => {
  const entry = 6 + index * 16;
  output[entry] = image.size === 256 ? 0 : image.size;
  output[entry + 1] = image.size === 256 ? 0 : image.size;
  output[entry + 2] = 0;
  output[entry + 3] = 0;
  output.writeUInt16LE(1, entry + 4);
  output.writeUInt16LE(32, entry + 6);
  output.writeUInt32LE(image.data.length, entry + 8);
  output.writeUInt32LE(imageOffset, entry + 12);
  image.data.copy(output, imageOffset);
  imageOffset += image.data.length;
});

fs.mkdirSync(path.dirname(destination), { recursive: true });
fs.writeFileSync(destination, output);
console.log(`Generated ${destination} with ${images.length} embedded sizes: ${SIZES.join(', ')}.`);
