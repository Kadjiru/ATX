// node Build-AtlasData.mjs <directory containing gather.json, kibelisks.json, pets.json and spawns.json>
// Inputs and version are documented in SOURCES.md. No map images are used.
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { gzipSync } from 'node:zlib';

const input = process.argv[2];
if (!input) throw new Error('Specify the directory with the three input JSON files.');
const read = name => JSON.parse(readFileSync(join(input, name + '.json'), 'utf8'));
const spawns = read('spawns').maps;
const gather = read('gather');
const kibelisks = read('kibelisks').maps;
const pets = read('pets');
const zones = {
    verteron: 'World/World_L/World_L_A',
    altgard: 'World/World_D/World_D_A',
    poeta: 'World/World_L/World_L_Starter',
    ishalgen: 'World/World_D/World_D_Starter',
    lower: 'Intersever/Abyss/Abyss_Reshanta_A',
    middle: 'Intersever/Abyss/Abyss_Reshanta_C',
    upper: 'Intersever/Abyss/Abyss_Reshanta_D'
};
const columns = 180, rows = 108;
const data = Object.entries(zones).map(([zone, table]) => {
    const samples = Object.values(spawns[table]).flat();
    const points = [];
    for (const [id, coords] of Object.entries(gather.maps[table] ?? {})) {
        const { kind, names } = gather.objects[id];
        for (const [x, y, z] of coords) {
            samples.push([x, y, z]);
            points.push({ key: `${id}:${x}:${y}`, category: ({ Fragment: 'След', Cube: 'Куб' })[kind] ?? 'Сбор',
                name: names.ru || names.en, en: names.en, x, y });
        }
    }
    for (const { names, x, y } of kibelisks[zone === 'lower' ? 'abyss' : zone] ?? []) {
        points.push({ key: `tp:${x}:${y}`, category: 'Телепорт', name: names.ru || names.en, en: names.en, x, y });
    }
    if (!points.length) {
        for (const [npc, positions] of Object.entries(spawns[table])) {
            const names = pets.pets[pets.monsters[npc]]?.names;
            if (!names) continue;
            for (const [x, y] of positions)
                points.push({ key: `pet:${npc}:${x}:${y}`, category: 'Питомец',
                    name: names.ru || names.en, en: names.en, x, y });
        }
    }

    const extent = (axis, field) => {
        const values = samples.map(p => p[axis]).sort((a, b) => a - b);
        return [
            Math.min(values[Math.floor(values.length * .01)], ...points.map(p => p[field])),
            Math.max(values[Math.floor(values.length * .99)], ...points.map(p => p[field]))
        ];
    };
    const [minX, maxX] = extent(0, 'x');
    const [minY, maxY] = extent(1, 'y');
    const step = Math.max((maxX - minX) / columns, (maxY - minY) / rows) * 1.12;
    const west = (minX + maxX - columns * step) / 2;
    const north = (minY + maxY - rows * step) / 2;
    const heights = samples.map(p => p[2]).sort((a, b) => a - b);
    const low = heights[Math.floor(heights.length * .02)];
    const high = heights[Math.floor(heights.length * .98)];
    const relief = Buffer.alloc(rows * columns);
    const radius = Math.max(4 * step, 0.048 * Math.max(maxX - minX, maxY - minY));
    for (let row = 0; row < rows; row++) {
        for (let col = 0; col < columns; col++) {
            const x = west + (col + .5) * step, y = north + (row + .5) * step;
            let elevation = 0, weight = 0, nearest = Infinity;
            for (const [sx, sy, z] of samples) {
                const distance = (sx - x) ** 2 + (sy - y) ** 2;
                if (distance > radius ** 2) continue;
                const influence = 1 / (distance + (step * 1.3) ** 2) ** 1.7;
                elevation += influence * z;
                weight += influence;
                nearest = Math.min(nearest, distance);
            }
            if (nearest > (radius * .76) ** 2) continue;
            const normalized = (elevation / weight - low) / Math.max(1, high - low);
            relief[row * columns + col] = 1 + Math.round(Math.max(0, Math.min(1, normalized)) * 253);
        }
    }
    return { zone, west, north, span: step * columns, columns, rows, relief: relief.toString('base64'),
        points: [...new Map(points.map(point => [point.key, point])).values()] };
});
writeFileSync(new URL('./Data/Atlas.json.gz', import.meta.url), gzipSync(JSON.stringify(data), { level: 9 }));
