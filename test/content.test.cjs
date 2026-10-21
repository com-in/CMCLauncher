const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const os = require('node:os');
const AdmZip = require('adm-zip');
const { configureLanguage, languageFor, inspectContent, listContent, importContent, toggleContent, worlds, contentDirectory } = require('../src/content.cjs');
const temp = async fn => { const dir = await fs.mkdtemp(path.join(os.tmpdir(), 'craft-content-')); try { await fn(dir); } finally { await fs.rm(dir, { recursive: true, force: true }); } };
function archive(file, entries) { const zip = new AdmZip(); for (const [name, value] of Object.entries(entries)) zip.addFile(name, Buffer.from(typeof value === 'string' ? value : JSON.stringify(value))); zip.writeZip(file); }
test('automatic language maps Chinese locales and preserves unrelated options', () => temp(async dir => {
  assert.equal(languageFor('zh-CN'), 'zh_cn'); assert.equal(languageFor('zh-Hant-HK'), 'zh_tw'); assert.equal(languageFor('en-GB'), 'en_us');
  await fs.writeFile(path.join(dir, 'options.txt'), 'music:0.5\r\nlang:en_us\r\nresourcePacks:["vanilla"]\r\n');
  await configureLanguage(dir, 'auto', 'zh-CN');
  assert.equal(await fs.readFile(path.join(dir, 'options.txt'), 'utf8'), 'music:0.5\nresourcePacks:["vanilla"]\nlang:zh_cn\n');
  await configureLanguage(dir, 'preserve', 'en-US');
  assert.match(await fs.readFile(path.join(dir, 'options.txt'), 'utf8'), /lang:zh_cn/);
}));
test('recognizes Fabric, Quilt, Forge and NeoForge metadata; corrupt files remain visible', () => temp(async dir => {
  const samples = [
    ['fabric', { 'fabric.mod.json': { id: 'sodium', name: 'Sodium', version: '1.0', description: 'Fast renderer' } }, 'Sodium'],
    ['quilt', { 'quilt.mod.json': { quilt_loader: { id: 'qmod', version: '2', metadata: { name: 'Quilt Mod' } } } }, 'Quilt Mod'],
    ['forge', { 'META-INF/mods.toml': 'modLoader="javafml"\n[[mods]]\nmodId="sample"\ndisplayName="Forge Mod"\nversion="3"' }, 'Forge Mod'],
    ['neoforge', { 'META-INF/neoforge.mods.toml': '[[mods]]\nmodId="neo"\ndisplayName="Neo Mod"\nversion="4"' }, 'Neo Mod']
  ];
  for (const [loader, entries, name] of samples) {
    const file = path.join(dir, loader + '.jar'); archive(file, entries);
    const info = await inspectContent(file, 'mod'); assert.equal(info.loader, loader); assert.equal(info.name, name); assert.equal(info.recognized, true);
  }
  await fs.writeFile(path.join(dir, 'broken.jar'), 'not a zip');
  assert.match((await inspectContent(path.join(dir, 'broken.jar'), 'mod')).error, /元数据/);
}));
test('resource pack directories are moved outside scan directory when disabled; restoration is reversible', () => temp(async dir => {
  const resource = path.join(dir, 'resourcepacks/My Pack'); await fs.mkdir(resource, { recursive: true });
  await fs.writeFile(path.join(resource, 'pack.mcmeta'), JSON.stringify({ pack: { pack_format: 34, description: { text: 'Beautiful world' } } }));
  assert.equal((await listContent(dir, 'resourcepack'))[0].description, 'Beautiful world');
  await toggleContent(dir, 'resourcepack', '', 'My Pack');
  assert.deepEqual(await fs.readdir(path.join(dir, 'resourcepacks')), []);
  const disabled = (await listContent(dir, 'resourcepack'))[0]; assert.equal(disabled.enabled, false);
  await toggleContent(dir, 'resourcepack', '', disabled.file);
  assert.equal((await listContent(dir, 'resourcepack'))[0].enabled, true);
}));
test('datapacks target real worlds only; import validates packages and refuses overwrite', () => temp(async dir => {
  await fs.mkdir(path.join(dir, 'saves/World'), { recursive: true }); await fs.writeFile(path.join(dir, 'saves/World/level.dat'), 'fixture');
  assert.deepEqual(await worlds(dir), ['World']);
  await assert.rejects(contentDirectory(dir, 'datapack', '../escape'));
  await assert.rejects(contentDirectory(dir, 'datapack', 'missing'));
  const file = path.join(dir, 'data.zip'); archive(file, { 'pack.mcmeta': { pack: { pack_format: 48, description: 'Test data' } }, 'data/test/function/test.mcfunction': 'say hello' });
  await importContent(dir, 'datapack', 'World', [file]);
  assert.equal((await listContent(dir, 'datapack', 'World'))[0].recognized, true);
  await assert.rejects(importContent(dir, 'datapack', 'World', [file]), /exist/i);
  const shader = path.join(dir, 'shader.zip'); archive(shader, { 'shaders/shaders.properties': 'profile=HIGH' });
  await importContent(dir, 'shader', '', [shader]); assert.equal((await listContent(dir, 'shader'))[0].recognized, true);
  await assert.rejects(importContent(dir, 'shader', '', [file]), /未识别/);
}));
