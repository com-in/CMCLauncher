const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const AdmZip = require('adm-zip');
const { LauncherCore, safePath, rulesAllow, mergeVersion, offlineAccount } = require('../src/core.cjs');
test('reject archive traversal and Windows special paths', () => {
  for (const file of ['../escape', 'a/../../escape', 'C:/escape', '/absolute', 'mods/CON.jar', 'a./b', 'a:stream']) assert.throws(() => safePath(os.tmpdir(), file));
  assert.ok(safePath(os.tmpdir(), 'mods/example.jar').endsWith(path.join('mods', 'example.jar')));
});
test('platform rules respect ordering and feature gates', () => {
  const system = { name: 'windows', arch: 'x86_64', version: '10.0' };
  assert.equal(rulesAllow([{ action: 'allow' }, { action: 'disallow', os: { name: 'windows' } }], {}, system), false);
  assert.equal(rulesAllow([{ action: 'allow', features: { is_demo_user: true } }], {}, system), false);
});
test('child libraries override parent versions and keep arguments', () => {
  const merged = mergeVersion({ libraries: [{ name: 'a:b:1' }], arguments: { game: ['parent'] } }, { libraries: [{ name: 'a:b:2' }], arguments: { game: ['child'] } });
  assert.deepEqual(merged.libraries, [{ name: 'a:b:2' }]);
  assert.deepEqual(merged.arguments.game, ['parent', 'child']);
});
test('offline UUID is deterministic and version 3', () => {
  assert.equal(offlineAccount('Steve').uuid, '5627dd98e6be3c21b8a8e92344183641');
  assert.throws(() => offlineAccount('../invalid'));
});
test('corrupt download never replaces existing file', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'craft-test-'));
  try {
    const file = path.join(root, 'file');
    await fs.writeFile(file, 'original');
    const core = new LauncherCore(root, { fetch: async () => new Response('corrupt') });
    await assert.rejects(core.download('https://example.com/file', file, { sha1: crypto.createHash('sha1').update('correct').digest('hex') }), /哈希/);
    assert.equal(await fs.readFile(file, 'utf8'), 'original');
    assert.deepEqual(await fs.readdir(root), ['file']);
  } finally { await fs.rm(root, { recursive: true, force: true }); }
});
test('pack import commits only after all files validate; rejects unsupported loaders', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'craft-pack-'));
  try {
    const zip = new AdmZip();
    const index = { formatVersion: 1, game: 'minecraft', name: 'Test pack', dependencies: { minecraft: '1.21.1', 'fabric-loader': '0.16.0' }, files: [{ path: 'mods/test.jar', downloads: ['https://example.com/mod'], hashes: { sha1: crypto.createHash('sha1').update('correct').digest('hex') }, fileSize: 7 }] };
    zip.addFile('modrinth.index.json', Buffer.from(JSON.stringify(index)));
    const file = path.join(root, 'test.mrpack'); zip.writeZip(file);
    const core = new LauncherCore(root, { fetch: async () => new Response('corrupt') });
    await assert.rejects(core.installPack(file), /哈希/);
    assert.deepEqual(await fs.readdir(path.join(root, 'staging')), []);
    index.dependencies.forge = '1'; zip.updateFile('modrinth.index.json', Buffer.from(JSON.stringify(index))); zip.writeZip(file);
    await assert.rejects(core.installPack(file), /forge/);
    delete index.dependencies.forge; zip.updateFile('modrinth.index.json', Buffer.from(JSON.stringify(index))); zip.addFile('overrides/config/test.txt', Buffer.from('configuration')); zip.writeZip(file);
    core.fetch = async () => new Response('correct'); core.versionFor = async () => ({});
    const instance = await core.installPack(file);
    assert.equal(await fs.readFile(path.join(root, 'instances', instance.id, 'config/test.txt'), 'utf8'), 'configuration');
  } finally { await fs.rm(root, { recursive: true, force: true }); }
});
test('required Mod dependencies install before main file and version updates remove old files', async () => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'craft-mod-'));
  try {
    let revision = 1;
    const definition = project => ({ id: `${project}-${revision}`, project_id: project, game_versions: ['1.21.1'], loaders: ['fabric'], version_type: 'release', dependencies: project === 'main' ? [{ project_id: 'dependency', dependency_type: 'required' }] : [], files: [{ primary: true, filename: `${project}-${revision}.jar`, url: `https://files.example/${project}`, hashes: { sha1: crypto.createHash('sha1').update(project).digest('hex') }, size: project.length }] });
    const core = new LauncherCore(root, { fetch: async url => {
      if (url.includes('/project/')) return Response.json([definition(url.split('/project/')[1].split('/')[0])]);
      return new Response(url.split('/').pop());
    } });
    const instance = { id: 'instance', gameVersion: '1.21.1', loader: 'fabric' };
    const first = await core.installMod(instance, 'main');
    assert.deepEqual(first.map(x => x.project), ['dependency', 'main']);
    revision = 2; await core.installMod(instance, 'main');
    assert.deepEqual((await fs.readdir(path.join(root, 'instances/instance/mods'))).sort(), ['dependency-2.jar', 'main-2.jar']);
  } finally { await fs.rm(root, { recursive: true, force: true }); }
});
test('launch arguments keep paths and identities as individual arguments', () => {
  const root = path.join(os.tmpdir(), 'path with spaces');
  const core = new LauncherCore(root);
  const prepared = { version: { id: '1.21.1', type: 'release', mainClass: 'net.minecraft.client.main.Main', assetIndex: { id: '17' }, arguments: { jvm: ['-Djava.library.path=${natives_directory}', '-cp', '${classpath}'], game: ['--username', '${auth_player_name}', '--gameDir', '${game_directory}', '--accessToken', '${auth_access_token}', { rules: [{ action: 'allow', features: { is_demo_user: true } }], value: '--demo' }] } }, storage: root, gameDir: path.join(root, 'my world'), natives: path.join(root, 'natives'), classpath: [path.join(root, 'library.jar')] };
  const args = core.buildArguments(prepared, {}, { memoryMb: 4096 }, offlineAccount('Steve'));
  assert.equal(args[args.indexOf('--gameDir') + 1], path.join(root, 'my world'));
  assert.equal(args.includes('--demo'), false);
  assert.ok(args.includes('-Xmx4096M'));
  assert.equal(args[args.indexOf('--username') + 1], 'Steve');
});
