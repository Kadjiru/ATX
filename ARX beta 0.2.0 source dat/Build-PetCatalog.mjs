// node Build-PetCatalog.mjs <directory containing pets.json and spawns.json>
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const input = process.argv[2];
if (!input) throw new Error('Specify the directory with pets.json and spawns.json.');
const read = name => JSON.parse(readFileSync(join(input, name + '.json'), 'utf8'));
const { pets, monsters, souls } = read('pets');
const { maps } = read('spawns');
const soulPets = new Set(Object.values(souls).map(String));
const zones = new Map([
    ['World/World_L/World_L_Starter', 'Поэта'],
    ['World/World_D/World_D_Starter', 'Исхальген'],
    ['World/World_L/World_L_A', 'Бертрон'],
    ['World/World_D/World_D_A', 'Альтгард'],
    ['Intersever/Abyss/Abyss_Reshanta_A', 'Нижняя Решанта'],
    ['Intersever/Abyss/Abyss_Reshanta_C', 'Средняя Решанта'],
    ['Intersever/Abyss/Abyss_Reshanta_D', 'Верхняя Решанта'],
    ['Dungeon/Party/KraoCave/', 'Пещера Крао'],
    ['Dungeon/Party/Urugugu_Canyon/', 'Каньон Уругугу'],
    ['Dungeon/Party/Draupnir/', 'Пещера Драупнир'],
    ['Dungeon/Party/BakronIsland/', 'Небесный остров Вакрона'],
    ['Dungeon/Party/Fire_Temple/', 'Храм Огня'],
    ['Dungeon/Party/HornCave/', 'Логово Свирепого Рога'],
    ['Dungeon/Party/Arkanis/', 'Расколотый Арканис']
]);
const habitats = new Map();
for (const [path, spawns] of Object.entries(maps)) {
    const zone = zones.get(path) ?? zones.get(path.slice(0, path.lastIndexOf('/') + 1));
    if (!zone) continue;
    for (const [npc, points] of Object.entries(spawns)) {
        const id = String(monsters[npc]);
        if (!points.length || !soulPets.has(id)) continue;
        if (!habitats.has(id)) habitats.set(id, new Set());
        habitats.get(id).add(zone);
    }
}
const catalog = Object.entries(pets).map(([id, { category, names }]) => ({
    Id: Number(id), Name: names.ru || names.en, English: names.en, Category: category,
    HasSoul: soulPets.has(id), Habitats: [...(habitats.get(id) ?? [])].sort()
}));
if (!catalog.length || catalog.some(x => !x.Name || !x.English || !x.Category))
    throw new Error('Incomplete pet catalog.');
writeFileSync(new URL('./Data/Pets.json', import.meta.url), '[\n' +
    catalog.map(pet => '  ' + JSON.stringify(pet)).join(',\n') + '\n]\n');
