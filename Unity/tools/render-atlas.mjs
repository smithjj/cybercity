// Renders the signage neon + color atlases headlessly via @napi-rs/canvas and dumps
// the library rect tables as JSON for the Unity port. Run: node tools/render-atlas.mjs
import { writeFileSync, mkdirSync } from 'node:fs';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);

// ---- polyfills for atlas.js / library.js (browser APIs) ----
const { createCanvas, GlobalFonts, loadImage } = require('@napi-rs/canvas');
globalThis.document = {
  createElement: () => { const c = createCanvas(1, 1); return c; },
  fonts: { check: () => true, load: async () => [] },
};
// System fonts. One missing file must not abort the rest of the list: the previous
// single try/catch meant a single absent font silently dropped every later one.
//
// The lantern cells are drawn with FONTS.mincho ("Yu Mincho", "MS Mincho", serif). Neither
// mincho face is installed here, and @napi-rs/canvas has no per-glyph fallback chain like a
// browser — so 'serif' resolved to a non-CJK face and every lantern glyph came out as the
// .notdef box, which is what the port was rendering. SimSun is the only serif CJK face on the
// machine; register it under both mincho family names so the existing FONTS string matches.
const FONT_DIR = 'C:/Windows/Fonts/';
const fonts = [
  'meiryo.ttc', 'YuGothM.ttc', 'msgothic.ttc', 'arial.ttf', 'arialbd.ttf', 'consola.ttf',
  'simsun.ttc',
];
for (const f of fonts) {
  try { GlobalFonts.registerFromPath(FONT_DIR + f); console.log('font ok:', f); }
  catch (e) { console.log('font skip:', f, e.message); }
}
for (const alias of ['Yu Mincho', 'MS Mincho']) {
  try { GlobalFonts.registerFromPath(FONT_DIR + 'simsun.ttc', alias); console.log('font alias:', alias, '-> simsun'); }
  catch (e) { console.log('font alias fail:', alias, e.message); }
}

const NAimg = await loadImage('assets/textures/signage/lightbox_atlas.png');
const CAimg = await loadImage('assets/textures/signage/posters_atlas.png');
const ADimg = await loadImage('assets/textures/signage/ads_grid.png');

const lib = await import('file:///C:/Users/smith/OneDrive/Documents/GitHub/cybercity/src/systems/signage/library.js');
const { L, NA, CA } = await lib.buildLibrary({ lightbox: NAimg, posters: CAimg, ads: ADimg });

mkdirSync('unity/Assets/Game/Textures/Signage', { recursive: true });
writeFileSync('unity/Assets/Game/Textures/Signage/neon_atlas.png', await NA.canvas.encode('png'));
writeFileSync('unity/Assets/Game/Textures/Signage/color_atlas.png', await CA.canvas.encode('png'));

const strip = (r) => r ? ({ uv: r.uv, aspect: r.w / r.h, natural: [r.w / 190, r.h / 190] }) : null;
const slim = (arr) => arr.map((d) => d.rect ? ({ rect: strip(d.rect), mode: d.mode, natural: d.natural, text: d.text || null, script: !!d.script, chase: !!d.chase }) : null);
const misc = {}; for (const k in L.misc) misc[k] = (L.misc[k] && L.misc[k].rect) ? { rect: strip(L.misc[k].rect), mode: L.misc[k].mode, natural: L.misc[k].natural } : null;
const lantern = {}; for (const k in L.lanternCells) { const e = L.lanternCells[k]; lantern[k] = e ? { uv: e.uv, aspect: e.w / e.h, natural: [e.w / 190, e.h / 190] } : null; }
writeFileSync('unity/Assets/Game/Signage/sign_lib.json', JSON.stringify({
  neonV: slim(L.neonV), neonH: slim(L.neonH), neonIcon: slim(L.neonIcon),
  chanH: slim(L.chanH), boxV: slim(L.boxV), boxH: slim(L.boxH), boxSq: slim(L.boxSq),
  posters: slim(L.posters), screens: slim(L.screens), tenant: slim(L.tenant), misc, lantern,
}));
console.log('atlases rendered: neon', NA.canvas.width, NA.canvas.height, '| color', CA.canvas.width, CA.canvas.height);
console.log('counts: neonV', L.neonV.length, 'neonH', L.neonH.length, 'neonIcon', L.neonIcon.length, 'chanH', L.chanH.length, 'boxV', L.boxV.length, 'boxH', L.boxH.length, 'boxSq', L.boxSq.length, 'posters', L.posters.length, 'screens', L.screens.length, 'tenant', L.tenant.length);
