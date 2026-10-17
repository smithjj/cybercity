// dump-noise.mjs — run the REAL JS createNoiseTexture (noiseTex.js) in node by stubbing
// the THREE API it touches, and dump the RGBA bytes for the C# oracle.
//   node unity/tools/dump-noise.mjs
import { readFileSync, writeFileSync, mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import path from 'path';

const here = path.dirname(fileURLToPath(import.meta.url));
const src = readFileSync(path.join(here, '..', '..', 'src', 'systems', 'atmosphere', 'noiseTex.js'), 'utf8');

// strip ESM import/export, provide a minimal THREE stub
const body = src
  .replace(/^import .*$/m, '')
  .replace(/export function createNoiseTexture/, 'function createNoiseTexture')
  .replace(/tex\.needsUpdate = true;\s*/g, '');
const THREE = {
  DataTexture: class { constructor(d, w, h) { this.image = { data: d, width: w, height: h }; } },
  RGBAFormat: 0, UnsignedByteType: 0, RepeatWrapping: 0, LinearFilter: 0,
  LinearMipmapLinearFilter: 0, NoColorSpace: 0,
};
const factory = new Function('THREE', body + '\nreturn createNoiseTexture;');
const createNoiseTexture = factory(THREE);
const tex = createNoiseTexture(256, 4242);
const data = Array.from(tex.image.data); // Uint8Array -> number[]

const outDir = path.join(here, '..', 'Temp', 'oracle');
mkdirSync(outDir, { recursive: true });
writeFileSync(path.join(outDir, 'dump-noise.json'), JSON.stringify({ size: 256, seed: 4242, data }, null, 0));
console.log(`wrote ${data.length} bytes`);
