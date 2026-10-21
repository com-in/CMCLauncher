const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const os = require('node:os');
const { spawn } = require('node:child_process');
const AdmZip = require('adm-zip');

const MANIFEST = 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json';
const MODRINTH = 'https://api.modrinth.com/v2';
const FABRIC = 'https://meta.fabricmc.net/v2';
const LAUNCHER_VERSION = require('../package.json').version;
const platform = () => ({ win32: 'windows', darwin: 'osx', linux: 'linux' })[process.platform];
const exists = async p => { try { await fs.access(p); return true; } catch { return false; } };

function safePath(root, relative) {
  if (typeof relative !== 'string' || !relative || /[\x00-\x1f:]/.test(relative) || /^[\\/]/.test(relative)) throw new Error('文件路径不合法');
  const parts = relative.replaceAll('\\', '/').split('/');
  if (parts.some(p => p === '..' || p === '.' || /[. ]$/.test(p) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(p))) throw new Error('文件路径越过实例目录或包含不支持的名称');
  const target = path.resolve(root, ...parts);
  if (!target.startsWith(path.resolve(root) + path.sep)) throw new Error('文件路径越过实例目录');
  return target;
}

function rulesAllow(rules, features = {}, system = { name: platform(), arch: os.arch() === 'x64' ? 'x86_64' : os.arch(), version: os.release() }) {
  if (!rules?.length) return true;
  let allowed = false;
  for (const rule of rules) {
    if (rule.os?.name && rule.os.name !== system.name) continue;
    if (rule.os?.arch && !new RegExp(`^(?:${rule.os.arch})$`).test(system.arch)) continue;
    if (rule.os?.version && !new RegExp(rule.os.version).test(system.version)) continue;
    if (rule.features && !Object.entries(rule.features).every(([k, v]) => Boolean(features[k]) === v)) continue;
    allowed = rule.action === 'allow';
  }
  return allowed;
}

function mavenPath(name) {
  const [coordinate, extension = 'jar'] = name.split('@');
  const [group, artifact, version, classifier] = coordinate.split(':');
  if (!group || !artifact || !version) throw new Error(`无效依赖坐标：${name}`);
  return `${group.replaceAll('.', '/')}/${artifact}/${version}/${artifact}-${version}${classifier ? '-' + classifier : ''}.${extension}`;
}

function mergeVersion(parent, child) {
  return { ...parent, ...child, downloads: { ...parent.downloads, ...child.downloads },
    libraries: [...(child.libraries || []), ...(parent.libraries || [])].filter((lib, i, all) => all.findIndex(x => x.name.split(':').slice(0, 2).join(':') === lib.name.split(':').slice(0, 2).join(':')) === i),
    arguments: { game: [...(parent.arguments?.game || []), ...(child.arguments?.game || [])], jvm: [...(parent.arguments?.jvm || []), ...(child.arguments?.jvm || [])] } };
}

function offlineAccount(name) {
  if (!/^[a-zA-Z0-9_]{3,16}$/.test(name)) throw new Error('玩家名应为 3–16 位英文字母、数字或下划线');
  const hash = crypto.createHash('md5').update(`OfflinePlayer:${name}`).digest();
  hash[6] = (hash[6] & 15) | 48;
  hash[8] = (hash[8] & 63) | 128;
  return { type: 'offline', name, uuid: hash.toString('hex'), accessToken: '0' };
}

async function writeJson(file, data) {
  await fs.mkdir(path.dirname(file), { recursive: true });
  const temp = `${file}.${crypto.randomUUID()}.tmp`;
  await fs.writeFile(temp, JSON.stringify(data, null, 2));
  await fs.rename(temp, file);
}

async function parallel(items, concurrency, action) {
  let next = 0;
  let failure;
  await Promise.all(Array.from({ length: Math.min(items.length, concurrency) }, async () => {
    while (!failure && next < items.length) {
      const index = next++;
      try { await action(items[index], index); } catch (e) { failure = e; }
    }
  }));
  if (failure) throw failure;
}

class LauncherCore {
  constructor(root, options = {}) {
    this.root = root;
    this.fetch = options.fetch || globalThis.fetch;
    this.emit = options.emit || (() => {});
  }

  async request(url, options = {}) {
    if (!/^https:\/\//.test(url)) throw new Error('下载地址必须使用 HTTPS');
    const response = await this.fetch(url, { ...options, headers: { 'User-Agent': `CraftLauncher/${LAUNCHER_VERSION}`, ...options.headers }, signal: options.signal || AbortSignal.timeout(90000) });
    if (!response.ok) throw new Error(`请求失败 (${response.status})：${new URL(url).hostname}`);
    return response;
  }

  async json(url) { return (await this.request(url)).json(); }

  async download(url, file, hashes = {}, size) {
    const algorithm = hashes.sha512 ? 'sha512' : hashes.sha1 ? 'sha1' : null;
    const expected = algorithm && hashes[algorithm];
    const valid = async () => {
      try {
        const data = await fs.readFile(file);
        return (!size || data.length === size) && (!expected || crypto.createHash(algorithm).update(data).digest('hex') === expected.toLowerCase());
      } catch { return false; }
    };
    if (await valid()) return;
    await fs.mkdir(path.dirname(file), { recursive: true });
    const temp = `${file}.${crypto.randomUUID()}.part`;
    try {
      const response = await this.request(url);
      const handle = await fs.open(temp, 'wx');
      const hash = algorithm && crypto.createHash(algorithm);
      let length = 0;
      try {
        for await (const chunk of response.body) {
          length += chunk.length;
          if (length > (size || 1024 * 1024 * 1024)) throw new Error('下载文件大小超出声明或上限');
          hash?.update(chunk);
          await handle.write(chunk);
        }
      } finally { await handle.close(); }
      if (size && length !== size) throw new Error('下载文件长度校验失败');
      if (expected && hash.digest('hex') !== expected.toLowerCase()) throw new Error('下载文件哈希校验失败');
      await fs.rename(temp, file);
    } finally { await fs.rm(temp, { force: true }); }
  }

  progress(stage, current, total, detail = '') { this.emit('progress', { stage, current, total, detail }); }

  async versions() {
    const data = await this.json(MANIFEST);
    this.manifest = data;
    // The first release supports modern Java Edition (1.13 and newer).
    return data.versions.filter(v => v.type === 'release' && new Date(v.releaseTime) >= new Date('2018-07-18')).map(({ id, type, releaseTime }) => ({ id, type, releaseTime }));
  }

  async vanillaVersion(id, storage = this.root) {
    const file = safePath(storage, `versions/${id}/${id}.json`);
    if (await exists(file)) return JSON.parse(await fs.readFile(file, 'utf8'));
    const manifest = this.manifest || await this.json(MANIFEST);
    const entry = manifest.versions.find(v => v.id === id);
    if (!entry) throw new Error(`找不到 Minecraft ${id}`);
    await this.download(entry.url, file, { sha1: entry.sha1 });
    return JSON.parse(await fs.readFile(file, 'utf8'));
  }

  async resolveLocalVersion(id, storage, seen = new Set()) {
    if (seen.has(id) || seen.size > 10) throw new Error('版本继承形成循环');
    seen.add(id);
    const file = safePath(storage, `versions/${id}/${id}.json`);
    const version = await exists(file) ? JSON.parse(await fs.readFile(file, 'utf8')) : await this.vanillaVersion(id, storage);
    return version.inheritsFrom ? mergeVersion(await this.resolveLocalVersion(version.inheritsFrom, storage, seen), version) : version;
  }

  async fabricLoaders(gameVersion) {
    const loaders = await this.json(`${FABRIC}/versions/loader/${encodeURIComponent(gameVersion)}`);
    return loaders.map(x => ({ version: x.loader.version, stable: x.loader.stable }));
  }

  async versionFor(instance) {
    if (instance.importRoot) return this.resolveLocalVersion(instance.versionId, instance.importRoot);
    const base = await this.vanillaVersion(instance.gameVersion);
    if (instance.loader !== 'fabric') return base;
    let loaderVersion = instance.loaderVersion;
    if (!loaderVersion) {
      const loaders = await this.fabricLoaders(instance.gameVersion);
      loaderVersion = (loaders.find(l => l.stable) || loaders[0])?.version;
      if (!loaderVersion) throw new Error('该 Minecraft 版本尚无 Fabric 支持');
      instance.loaderVersion = loaderVersion;
    }
    const profileFile = safePath(this.root, `profiles/fabric-${instance.gameVersion}-${loaderVersion}.json`);
    let child;
    if (await exists(profileFile)) child = JSON.parse(await fs.readFile(profileFile, 'utf8'));
    else {
      child = await this.json(`${FABRIC}/versions/loader/${encodeURIComponent(instance.gameVersion)}/${encodeURIComponent(loaderVersion)}/profile/json`);
      await writeJson(profileFile, child);
    }
    return mergeVersion(base, child);
  }

  async prepare(instance) {
    const version = await this.versionFor(instance);
    const storage = instance.importRoot || this.root;
    const gameDir = instance.directory || safePath(this.root, `instances/${instance.id}`);
    await fs.mkdir(gameDir, { recursive: true });
    const natives = path.join(gameDir, '.craft-natives');
    await fs.mkdir(natives, { recursive: true });
    const jar = safePath(storage, `versions/${version.jar || instance.gameVersion}/${version.jar || instance.gameVersion}.jar`);
    if (version.downloads?.client) await this.download(version.downloads.client.url, jar, { sha1: version.downloads.client.sha1 }, version.downloads.client.size);
    else if (!await exists(jar)) throw new Error('本地版本缺少客户端 JAR，请先使用原启动器补全该版本');
    const classpath = [];
    const jobs = [];
    for (const lib of version.libraries || []) {
      if (!rulesAllow(lib.rules)) continue;
      const artifact = lib.downloads?.artifact;
      if (artifact || !lib.downloads) {
        const relative = artifact?.path || mavenPath(lib.name);
        const destination = safePath(storage, `libraries/${relative}`);
        classpath.push(destination);
        jobs.push(async () => this.download(artifact?.url || `${lib.url || 'https://libraries.minecraft.net/'}${relative}`, destination, { sha1: artifact?.sha1 }, artifact?.size));
      }
      const classifier = lib.natives?.[platform()]?.replace('${arch}', os.arch() === 'ia32' ? '32' : '64');
      const native = lib.downloads?.classifiers?.[classifier];
      if (native) {
        const destination = safePath(storage, `libraries/${native.path}`);
        jobs.push(async () => {
          await this.download(native.url, destination, { sha1: native.sha1 }, native.size);
          await extractZip(new AdmZip(destination), natives, '', lib.extract?.exclude || ['META-INF/']);
        });
      }
    }
    let complete = 0;
    this.progress('下载游戏依赖', 0, jobs.length);
    await parallel(jobs, 6, async job => { await job(); this.progress('下载游戏依赖', ++complete, jobs.length); });
    if (version.logging?.client?.file) {
      const log = version.logging.client.file;
      await this.download(log.url, safePath(storage, `assets/log_configs/${log.id}`), { sha1: log.sha1 }, log.size);
    }
    let assetIndex;
    if (version.assetIndex) {
      const indexFile = safePath(storage, `assets/indexes/${version.assetIndex.id}.json`);
      await this.download(version.assetIndex.url, indexFile, { sha1: version.assetIndex.sha1 }, version.assetIndex.size);
      assetIndex = JSON.parse(await fs.readFile(indexFile, 'utf8'));
      const assets = Object.entries(assetIndex.objects);
      complete = 0;
      this.progress('下载游戏资源', 0, assets.length);
      await parallel(assets, 8, async ([name, asset]) => {
        if (!/^[a-f0-9]{40}$/.test(asset.hash)) throw new Error('无效的资源哈希');
        const location = `${asset.hash.slice(0, 2)}/${asset.hash}`;
        const target = safePath(storage, `assets/objects/${location}`);
        await this.download(`https://resources.download.minecraft.net/${location}`, target, { sha1: asset.hash }, asset.size);
        if (assetIndex.virtual || assetIndex.map_to_resources) {
          const mapped = safePath(assetIndex.map_to_resources ? path.join(gameDir, 'resources') : path.join(storage, 'assets/virtual', version.assetIndex.id), name);
          await fs.mkdir(path.dirname(mapped), { recursive: true });
          await fs.copyFile(target, mapped);
        }
        if (++complete % 20 === 0 || complete === assets.length) this.progress('下载游戏资源', complete, assets.length);
      });
    }
    return { version, storage, gameDir, natives, classpath: [...new Set([...classpath, jar])], assetIndex };
  }

  buildArguments(prepared, instance, settings, account) {
    const { version, storage, gameDir, natives, classpath, assetIndex } = prepared;
    const values = {
      natives_directory: natives, launcher_name: 'CraftLauncher', launcher_version: LAUNCHER_VERSION,
      classpath: classpath.join(path.delimiter), classpath_separator: path.delimiter, library_directory: path.join(storage, 'libraries'),
      auth_player_name: account.name, version_name: version.id, game_directory: gameDir, assets_root: path.join(storage, 'assets'),
      game_assets: assetIndex?.virtual ? path.join(storage, 'assets/virtual', version.assetIndex.id) : path.join(storage, 'assets'),
      assets_index_name: version.assetIndex?.id || version.assets, auth_uuid: account.uuid.replaceAll('-', ''),
      auth_access_token: account.accessToken, auth_session: account.accessToken, user_type: account.type === 'microsoft' ? 'msa' : 'legacy',
      version_type: version.type || 'release', user_properties: '{}', clientid: settings.microsoftClientId || '', auth_xuid: account.xuid || '',
      resolution_width: '1280', resolution_height: '720'
    };
    const substitute = str => str.replace(/\$\{([^}]+)\}/g, (_, key) => {
      if (!(key in values)) throw new Error(`不支持的启动参数：${key}`);
      return values[key];
    });
    const flatten = args => (args || []).flatMap(arg => typeof arg === 'string' ? [arg] : rulesAllow(arg.rules) ? (Array.isArray(arg.value) ? arg.value : [arg.value]) : []).map(substitute);
    const jvm = version.arguments?.jvm?.length ? flatten(version.arguments.jvm) : [`-Djava.library.path=${natives}`, '-cp', classpath.join(path.delimiter)];
    if (platform() === 'osx' && !jvm.includes('-XstartOnFirstThread')) jvm.unshift('-XstartOnFirstThread');
    const game = version.arguments?.game?.length ? flatten(version.arguments.game) : (version.minecraftArguments || '').split(/\s+/).filter(Boolean).map(substitute);
    const logging = version.logging?.client;
    if (logging?.argument) jvm.push(logging.argument.replace('${path}', safePath(storage, `assets/log_configs/${logging.file.id}`)));
    const memory = Math.min(65536, Math.max(1024, Number(settings.memoryMb) || 4096));
    return ['-Xms512M', `-Xmx${memory}M`, ...jvm, version.mainClass, ...game];
  }

  async launch(instance, settings, account) {
    if (!account) throw new Error('请先添加并选择一个账号');
    // Check Java before downloading hundreds of MB of resources.
    const version = await this.versionFor(instance);
    const java = settings.javaPath || 'java';
    const javaInfo = await inspectJava(java);
    const required = version.javaVersion?.majorVersion || 8;
    if (javaInfo.major < required) throw new Error(`此版本需要 Java ${required} 或更新版本；当前为 Java ${javaInfo.major}。请在设置中选择正确的 Java。`);
    const prepared = await this.prepare(instance);
    await require('./content.cjs').configureLanguage(prepared.gameDir, settings.gameLanguage, settings.systemLocale || Intl.DateTimeFormat().resolvedOptions().locale);
    const args = this.buildArguments(prepared, instance, settings, account);
    this.progress('正在启动游戏', 1, 1);
    return new Promise((resolve, reject) => {
      const child = spawn(java, args, { cwd: prepared.gameDir, windowsHide: true, shell: false });
      child.once('error', reject);
      child.once('spawn', () => resolve(child));
      child.stdout.on('data', data => this.emit('log', redact(data.toString(), [account.accessToken])));
      child.stderr.on('data', data => this.emit('log', redact(data.toString(), [account.accessToken])));
    });
  }

  async search(query, type, instance) {
    if (!['mod', 'modpack'].includes(type)) throw new Error('不支持的内容类型');
    const facets = [[`project_type:${type}`]];
    if (instance && type === 'mod') {
      if (instance.loader !== 'fabric' && instance.loader !== 'forge' && instance.loader !== 'neoforge' && instance.loader !== 'quilt') throw new Error('请先选择安装了 Mod 加载器的实例');
      facets.push([`versions:${instance.gameVersion}`], [`categories:${instance.loader}`]);
    }
    const params = new URLSearchParams({ query: String(query).slice(0, 200), facets: JSON.stringify(facets), limit: '16', index: query ? 'relevance' : 'downloads' });
    return (await this.json(`${MODRINTH}/search?${params}`)).hits;
  }

  async projectVersion(project, gameVersion, loader) {
    const params = new URLSearchParams();
    if (gameVersion) params.set('game_versions', JSON.stringify([gameVersion]));
    if (loader) params.set('loaders', JSON.stringify([loader]));
    const versions = await this.json(`${MODRINTH}/project/${encodeURIComponent(project)}/version?${params}`);
    if (!versions.length) throw new Error('没有与当前游戏版本和加载器兼容的文件');
    return versions.find(v => v.version_type === 'release') || versions[0];
  }

  async installMod(instance, project) {
    if (instance.loader === 'vanilla') throw new Error('原版实例不能加载 Mod，请创建 Fabric 实例');
    const directory = instance.directory || safePath(this.root, `instances/${instance.id}`);
    const installed = new Set();
    const pending = [];
    const stage = path.join(directory, `.craft-mods-${crypto.randomUUID()}`);
    await fs.mkdir(stage, { recursive: true });
    const visit = async (projectId, exactVersion) => {
      const version = exactVersion ? await this.json(`${MODRINTH}/version/${encodeURIComponent(exactVersion)}`) : await this.projectVersion(projectId, instance.gameVersion, instance.loader);
      if (installed.has(version.project_id)) return;
      if (installed.size >= 100) throw new Error('依赖数量超过上限');
      installed.add(version.project_id);
      if (!version.game_versions.includes(instance.gameVersion) || !version.loaders.includes(instance.loader)) throw new Error(`依赖 ${version.name} 与当前实例不兼容`);
      for (const dep of version.dependencies || []) if (dep.dependency_type === 'required') {
        if (!dep.project_id && !dep.version_id) throw new Error('无法自动解析此 Mod 的必要依赖');
        await visit(dep.project_id, dep.version_id);
      }
      const file = version.files.find(f => f.primary) || version.files[0];
      if (!file || !file.filename.endsWith('.jar')) throw new Error('Mod 文件不是 JAR');
      this.progress('安装 Mod 与依赖', pending.length, installed.size, file.filename);
      await this.download(file.url, safePath(stage, file.filename), file.hashes, file.size);
      pending.push({ project: version.project_id, version: version.id, file: file.filename });
    };
    try {
      await visit(project);
      const modsDir = path.join(directory, 'mods');
      await fs.mkdir(modsDir, { recursive: true });
      const recordsFile = path.join(directory, '.craft-mods.json');
      const records = await exists(recordsFile) ? JSON.parse(await fs.readFile(recordsFile, 'utf8')) : [];
      for (const item of pending) {
        await fs.copyFile(safePath(stage, item.file), safePath(modsDir, item.file));
        for (const previous of records.filter(r => r.project === item.project && r.file !== item.file)) {
          await fs.rm(safePath(modsDir, previous.file), { force: true });
          await fs.rm(safePath(modsDir, previous.file + '.disabled'), { force: true });
        }
      }
      await writeJson(recordsFile, [...records.filter(r => !installed.has(r.project)), ...pending]);
      return pending;
    } finally { await fs.rm(stage, { recursive: true, force: true }); }
  }

  async installPack(file, name) {
    if ((await fs.stat(file)).size > 1024 * 1024 * 1024) throw new Error('整合包文件超过 1 GB 上限');
    const zip = new AdmZip(file);
    const manifestEntry = zip.getEntry('modrinth.index.json');
    if (!manifestEntry) throw new Error('请选择 Modrinth .mrpack 整合包；当前不支持 CurseForge manifest.zip');
    if (manifestEntry.header.size > 8 * 1024 * 1024) throw new Error('整合包清单过大');
    const index = JSON.parse(manifestEntry.getData().toString('utf8'));
    if (index.formatVersion !== 1 || index.game !== 'minecraft' || !index.dependencies?.minecraft || !Array.isArray(index.files)) throw new Error('整合包清单不受支持');
    const unsupported = Object.keys(index.dependencies).filter(k => !['minecraft', 'fabric-loader'].includes(k));
    if (unsupported.length) throw new Error(`此整合包需要 ${unsupported.join('、')}；第一版整合包支持原版与 Fabric，暂不支持该加载器`);
    const instance = { id: crypto.randomUUID(), name: String(name || index.name || '新整合包').slice(0, 80), gameVersion: index.dependencies.minecraft, loader: index.dependencies['fabric-loader'] ? 'fabric' : 'vanilla', loaderVersion: index.dependencies['fabric-loader'], createdAt: new Date().toISOString(), source: index.name };
    const stage = safePath(this.root, `staging/${instance.id}`);
    const final = safePath(this.root, `instances/${instance.id}`);
    await fs.mkdir(stage, { recursive: true });
    try {
      const files = index.files.filter(f => f.env?.client !== 'unsupported');
      if (files.length > 20000 || files.reduce((n, f) => n + (Number(f.fileSize) || 0), 0) > 20 * 1024 ** 3) throw new Error('整合包内容超过支持上限');
      const destinations = new Set();
      for (const f of files) {
        const key = safePath(stage, f.path).toLowerCase();
        if (destinations.has(key)) throw new Error('整合包包含重复文件路径');
        destinations.add(key);
      }
      let done = 0;
      await parallel(files, 5, async f => {
        if (!f.hashes?.sha512 && !f.hashes?.sha1) throw new Error('整合包文件缺少校验哈希');
        if (!f.downloads?.length) throw new Error(`整合包文件缺少下载地址：${f.path}`);
        let lastError;
        for (const url of f.downloads) {
          try { await this.download(url, safePath(stage, f.path), f.hashes, f.fileSize); lastError = null; break; } catch (e) { lastError = e; }
        }
        if (lastError) throw lastError;
        this.progress('安装整合包', ++done, files.length, f.path);
      });
      await extractZip(zip, stage, 'overrides/');
      await extractZip(zip, stage, 'client-overrides/');
      await this.versionFor(instance);
      await fs.mkdir(path.dirname(final), { recursive: true });
      await fs.rename(stage, final);
      return instance;
    } finally { await fs.rm(stage, { recursive: true, force: true }); }
  }
}

async function extractZip(zip, root, prefix = '', exclusions = []) {
  let expanded = 0;
  for (const entry of zip.getEntries()) {
    const name = entry.entryName.replaceAll('\\', '/');
    if (!name.startsWith(prefix) || entry.isDirectory || exclusions.some(x => name.startsWith(x))) continue;
    const relative = name.slice(prefix.length);
    const destination = safePath(root, relative);
    // Reject Unix symbolic links and oversized archive contents.
    if (((entry.attr >>> 16) & 0xf000) === 0xa000) throw new Error('压缩包包含符号链接');
    expanded += entry.header.size;
    if (entry.header.size > 512 * 1024 ** 2 || expanded > 4 * 1024 ** 3) throw new Error('压缩包展开大小超过上限');
    await fs.mkdir(path.dirname(destination), { recursive: true });
    await fs.writeFile(destination, entry.getData());
  }
}

function redact(text, secrets = []) {
  for (const secret of secrets) if (secret && secret.length > 8) text = text.replaceAll(secret, '[REDACTED]');
  return text.replace(/(Bearer\s+)[A-Za-z0-9._~+\/-]+/gi, '$1[REDACTED]');
}

function inspectJava(executable) {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, ['-version'], { windowsHide: true, shell: false });
    let output = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error('检测 Java 超时')); }, 10000);
    child.stdout.on('data', b => output += b);
    child.stderr.on('data', b => output += b);
    child.once('error', e => { clearTimeout(timer); reject(new Error(`无法运行 Java：${e.message}`)); });
    child.once('close', code => {
      clearTimeout(timer);
      const match = output.match(/version\s+"(\d+)(?:\.(\d+))?/);
      if (code !== 0 || !match) reject(new Error('无法识别 Java 版本，请选择 java.exe'));
      else resolve({ major: Number(match[1] === '1' ? match[2] : match[1]), description: output.split('\n').find(line => /version\s+"/.test(line))?.trim() || output.split('\n')[0].trim() });
    });
  });
}

module.exports = { LauncherCore, safePath, rulesAllow, mavenPath, mergeVersion, offlineAccount, writeJson, extractZip, inspectJava, redact, exists, parallel };
