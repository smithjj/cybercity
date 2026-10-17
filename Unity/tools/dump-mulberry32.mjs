// dump-mulberry32.mjs — extract mulberry32 from the real JS source (no transcription risk)
// and dump its output as double bit-patterns for the C# oracle.
//   node unity/tools/dump-mulberry32.mjs
import { readFileSync, writeFileSync, mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import path from 'path';

const here = path.dirname(fileURLToPath(import.meta.url));
const srcPath = path.join(here, '..', '..', 'src', 'systems', 'atmosphere', 'environment.js');
const src = readFileSync(srcPath, 'utf8');
const m = src.match(/function mulberry32\(a\) \{[\s\S]*?\n\}/);
if (!m) throw new Error('mulberry32 not found in ' + srcPath);
const factory = new Function(`${m[0]}; return mulberry32;`); // returns the mulberry32 function
const mulberry32 = factory();

const N = 64;
const seeds = [777, 4242, 0, 1, 2, 12345, 305419896, 4294967295];
const out = { source: 'src/systems/atmosphere/environment.js', n: N, seeds: [] };
const f64 = new Float64Array(1);
for (const seed of seeds) {
  const rnd = mulberry32(seed);
  const hex = [], dec = [];
  for (let i = 0; i < N; i++) {
    const v = rnd();
    f64[0] = v;
    const u64 = new BigUint64Array(f64.buffer)[0];
    hex.push(u64.toString(16).padStart(16, '0'));
    dec.push(v);
  }
  out.seeds.push({ seed, valuesHex: hex });
}
const outDir = path.join(here, '..', 'Temp', 'oracle');
mkdirSync(outDir, { recursive: true });
writeFileSync(path.join(outDir, 'dump-mulberry32.json'), JSON.stringify(out, null, 1));
console.log(`wrote ${out.seeds.length} seeds x ${N} values`);
