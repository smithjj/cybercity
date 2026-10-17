import { LAYOUT } from '../../src/world/layout.js';
import { buildPlan } from '../../src/systems/buildings/plan.js';
import { buildLotArchitecture } from '../../src/systems/buildings/architecture.js';
import { buildProps } from '../../src/systems/buildings/props.js';
import { ChunkedBuckets } from '../../src/systems/buildings/geom.js';
import { writeFileSync } from 'node:fs';
const ctx = { layout: LAYOUT, assets: { gltf: async (path) => ({ scene: { updateMatrixWorld() {}, traverse() {} } }) } };
const { lots } = buildPlan(LAYOUT);
const B = new ChunkedBuckets(96);
const out = { colliders: [], lights: [], facades: [] };
for (const lot of lots) buildLotArchitecture(lot, B, out);
// mark: log draws only during buildProps for lot 1127's segment
globalThis.__RNGDRAW = [];
globalThis.__MARK = 'PROPS';
await buildProps(ctx, lots, B, out, { wetness: 1 });
writeFileSync('unity/Temp/js_draws_props.txt', globalThis.__RNGDRAW.join('\n'));
console.log('draws:', globalThis.__RNGDRAW.length);
