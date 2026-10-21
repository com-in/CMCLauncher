const fs = require('node:fs/promises');
const path = require('node:path');
const AdmZip = require('adm-zip');
const { parse } = require('smol-toml');
const { safePath, exists } = require('./core.cjs');

async function updateOptions(directory, changes) {
  await fs.mkdir(directory, { recursive: true });
  const file = path.join(directory, 'options.txt');
  const original = await exists(file) ? await fs.readFile(file, 'utf8') : '';
  const lines = original.split(/\r?\n/).filter(line => line && !Object.hasOwn(changes, line.split(':')[0]));
  await fs.writeFile(file, [...lines, ...Object.entries(changes).map(([k, v]) => `${k}:${v}`), ''].join('\n'));
}
function languageFor(locale) {
  const tag = String(locale).toLowerCase().replaceAll('_', '-');
  if (tag.startsWith('zh')) return /tw|hk|hant|mo/.test(tag) ? 'zh_tw' : 'zh_cn';
  const known = { en: 'en_us', ja: 'ja_jp', ko: 'ko_kr', de: 'de_de', fr: 'fr_fr', es: 'es_es', ru: 'ru_ru', pt: 'pt_br', it: 'it_it' };
  return known[tag.split('-')[0]] || 'en_us';
}
async function configureLanguage(directory, preference, locale) {
  if (preference === 'preserve') return;
  const language = preference === 'auto' || !preference ? languageFor(locale) : preference;
  if (!/^[a-z]{2}_[a-z]{2}$/.test(language)) throw new Error('无效的游戏语言');
  await updateOptions(directory, { lang: language });
}
function plain(value) {
  if (typeof value === 'string') return value;
  if (Array.isArray(value)) return value.map(plain).join('');
  if (value && typeof value === 'object') return `${value.text || value.translate || ''}${plain(value.extra || '')}`;
  return '';
}
async function inspectContent(file, type) {
  const result = { name: path.basename(file).replace(/\.(jar|zip)(\.disabled)?$/i, ''), description: '', version: '', loader: '', modId: '', recognized: false };
  try {
    const stat = await fs.lstat(file);
    if (stat.isSymbolicLink()) throw new Error('不支持符号链接');
    if (!stat.isDirectory() && stat.size > 512 * 1024 ** 2) throw new Error('文件过大，跳过识别');
    const zip = stat.isDirectory() ? null : new AdmZip(file);
    const read = async name => {
      if (zip) {
        const entry = zip.getEntry(name);
        if (!entry) return null;
        if (entry.header.size > 2 * 1024 ** 2) throw new Error('元数据过大');
        return entry.getData().toString('utf8');
      }
      const target = safePath(file, name);
      const stat = await fs.lstat(target).catch(() => null);
      if (!stat?.isFile() || stat.isSymbolicLink() || stat.size > 2 * 1024 ** 2) return null;
      return fs.readFile(target, 'utf8');
    };
    if (type === 'mod') {
      const fabric = await read('fabric.mod.json');
      const quilt = await read('quilt.mod.json');
      const neo = await read('META-INF/neoforge.mods.toml');
      const forge = await read('META-INF/mods.toml');
      if (fabric || quilt) {
        const data = JSON.parse(fabric || quilt);
        const mod = fabric ? data : data.quilt_loader;
        const metadata = fabric ? data : mod.metadata || {};
        Object.assign(result, { name: metadata.name || mod.id, modId: mod.id, version: String(mod.version || ''), description: plain(metadata.description), loader: fabric ? 'fabric' : 'quilt', recognized: true });
      } else if (neo || forge) {
        const data = parse(neo || forge);
        const mod = data.mods?.[0];
        if (mod) Object.assign(result, { name: mod.displayName || mod.modId, modId: mod.modId, version: String(mod.version || '').replace('${file.jarVersion}', 'JAR 清单版本'), description: plain(mod.description), loader: neo ? 'neoforge' : 'forge', recognized: true });
      } else {
        const legacy = await read('mcmod.info');
        if (legacy) {
          const data = JSON.parse(legacy); const mod = (Array.isArray(data) ? data : data.modList)?.[0];
          if (mod) Object.assign(result, { name: mod.name || mod.modid, modId: mod.modid, version: String(mod.version || ''), description: plain(mod.description), loader: 'forge', recognized: true });
        }
      }
    } else if (type === 'shader') {
      result.recognized = zip ? zip.getEntries().some(e => e.entryName.startsWith('shaders/')) : await exists(path.join(file, 'shaders'));
      result.description = result.recognized ? '需要 Iris 或 OptiFine；在游戏视频设置中选择光影包。' : '';
    } else {
      const text = await read('pack.mcmeta');
      if (text) { const pack = JSON.parse(text).pack; if (pack) Object.assign(result, { description: plain(pack.description), version: `格式 ${pack.pack_format ?? pack.min_format ?? '?'}`, recognized: true }); }
    }
  } catch (e) { result.error = `无法读取元数据：${e.message}`; }
  return result;
}
async function contentDirectory(directory, type, world) {
  const folders = { mod: 'mods', resourcepack: 'resourcepacks', shader: 'shaderpacks' };
  if (folders[type]) return path.join(directory, folders[type]);
  if (type !== 'datapack' || !world || path.basename(world) !== world) throw new Error('请选择数据包所属的世界');
  const worldDir = safePath(directory, `saves/${world}`);
  const stat = await fs.lstat(worldDir);
  if (!stat.isDirectory() || stat.isSymbolicLink() || !await exists(path.join(worldDir, 'level.dat'))) throw new Error('世界不存在');
  return path.join(worldDir, 'datapacks');
}
async function contentFile(directory, type, world, file) {
  if (path.basename(file) !== file) throw new Error('无效的内容文件名');
  const root = await contentDirectory(directory, type, world);
  if (!(await listContent(directory, type, world)).some(item => item.file === file)) throw new Error('内容不存在');
  if (type !== 'mod' && file.endsWith('.disabled')) {
    const disabled = safePath(root + '.craft-disabled', file.slice(0, -9));
    if (await exists(disabled)) return disabled;
  }
  return safePath(root, file);
}
async function worlds(directory) {
  const root = path.join(directory, 'saves');
  if (!await exists(root)) return [];
  const result = [];
  for (const entry of await fs.readdir(root, { withFileTypes: true })) if (entry.isDirectory() && await exists(path.join(root, entry.name, 'level.dat'))) result.push(entry.name);
  return result;
}
async function listContent(directory, type, world, includeDisabled = true) {
  const root = await contentDirectory(directory, type, world);
  const result = [];
  for (const entry of await fs.readdir(root, { withFileTypes: true }).catch(e => { if (e.code === 'ENOENT') return []; throw e; })) {
    if (entry.isSymbolicLink() || !entry.isFile() && !entry.isDirectory()) continue;
    if (type === 'mod' ? !entry.isFile() || !/\.jar(?:\.disabled)?$/i.test(entry.name) : entry.isFile() && !/\.zip(?:\.disabled)?$/i.test(entry.name)) continue;
    result.push({ file: entry.name, enabled: !entry.name.endsWith('.disabled'), ...(await inspectContent(path.join(root, entry.name), type)) });
  }
  if (type !== 'mod' && includeDisabled && await exists(root + '.craft-disabled')) {
    for (const entry of await fs.readdir(root + '.craft-disabled', { withFileTypes: true })) {
      if (entry.isSymbolicLink() || !entry.isDirectory() && !/\.zip$/i.test(entry.name)) continue;
      result.push({ ...(await inspectContent(path.join(root + '.craft-disabled', entry.name), type)), file: entry.name + '.disabled', enabled: false });
    }
  }
  return result;
}
async function importContent(directory, type, world, files) {
  const root = await contentDirectory(directory, type, world);
  await fs.mkdir(root, { recursive: true });
  for (const file of files) {
    const metadata = await inspectContent(file, type);
    if (!metadata.recognized) throw new Error(`${path.basename(file)} 未识别为有效${{ mod: ' Mod', resourcepack: '资源包', datapack: '数据包', shader: '光影包' }[type]}${metadata.error ? '：' + metadata.error : ''}`);
  }
  for (const file of files) await fs.copyFile(file, safePath(root, path.basename(file)), require('node:fs').constants.COPYFILE_EXCL);
  return files.length;
}
async function toggleContent(directory, type, world, file) {
  const root = await contentDirectory(directory, type, world);
  if (path.basename(file) !== file) throw new Error('无效的内容文件名');
  const items = await listContent(directory, type, world);
  if (!items.some(i => i.file === file)) throw new Error('文件不存在或不受支持');
  if (type === 'mod') throw new Error('请使用 Mod 管理');
  const disabled = file.endsWith('.disabled');
  const name = disabled ? file.slice(0, -9) : file;
  const holding = root + '.craft-disabled';
  const source = disabled && await exists(safePath(holding, name)) ? safePath(holding, name) : safePath(root, file);
  const target = safePath(disabled ? root : holding, name);
  if (await exists(target)) throw new Error('目标文件已存在');
  await fs.mkdir(path.dirname(target), { recursive: true });
  await fs.rename(source, target);
}
module.exports = { configureLanguage, languageFor, inspectContent, contentDirectory, contentFile, worlds, listContent, importContent, toggleContent };
