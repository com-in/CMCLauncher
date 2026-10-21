const { app, BrowserWindow, ipcMain, dialog, shell, net, session, safeStorage } = require('electron');
const fs = require('node:fs/promises');
const path = require('node:path');
const crypto = require('node:crypto');
const { LauncherCore, safePath, offlineAccount, writeJson, exists, inspectJava, redact } = require('./core.cjs');
const content = require('./content.cjs');
const { MicrosoftAuth } = require('./auth.cjs');

if (process.env.CRAFT_TEST_HOME) app.setPath('userData', path.resolve(process.env.CRAFT_TEST_HOME));
let window, config, core, auth;
let busy = false;
let authController;
const running = new Map();
const logLines = [];
const configFile = () => path.join(app.getPath('userData'), 'settings.json');
const publicAccount = ({ type, name, uuid }) => ({ type, name, uuid });
const state = () => ({ settings: config.settings, accounts: config.accounts.map(publicAccount), selectedAccount: config.selectedAccount, instances: config.instances, libraryRoot: core.root, running: [...running.keys()], busy });
const persist = () => writeJson(configFile(), config);
function emit(type, data) {
  if (type === 'log') {
    data = redact(String(data), config?.accounts.flatMap(a => {
      try { const secrets = decodeSecrets(a); return [secrets.accessToken, secrets.refreshToken]; } catch { return []; }
    }) || []);
    logLines.push(data);
    if (logLines.length > 600) logLines.shift();
  }
  if (window && !window.isDestroyed()) window.webContents.send('craft:event', { type, data });
}
function instanceById(id) {
  const instance = config.instances.find(i => i.id === id);
  if (!instance) throw new Error('实例不存在');
  return instance;
}
function instanceDirectory(instance) { return instance.directory || safePath(core.root, `instances/${instance.id}`); }
function requireStopped(instance) { if (running.has(instance.id)) throw new Error('请先关闭此实例的游戏，再修改文件'); }

function encodeAccount(account) {
  if (account.type === 'offline') return publicAccount(account);
  if (!safeStorage.isEncryptionAvailable()) throw new Error('系统凭据加密不可用，无法安全保存微软账号');
  const { accessToken, refreshToken, expiresAt, xuid, ...publicFields } = account;
  return { ...publicFields, secret: safeStorage.encryptString(JSON.stringify({ accessToken, refreshToken, expiresAt, xuid })).toString('base64') };
}
function decodeSecrets(account) {
  if (account.type === 'offline') return { accessToken: '0' };
  if (!account.secret || !safeStorage.isEncryptionAvailable()) throw new Error('无法解密账号，请移除此账号并重新登录');
  return JSON.parse(safeStorage.decryptString(Buffer.from(account.secret, 'base64')));
}

async function exclusive(action) {
  if (busy) throw new Error('已有任务正在进行，请稍候');
  busy = true;
  emit('state', state());
  try { return await action(); }
  finally { busy = false; emit('progress', null); emit('state', state()); }
}

const handlers = {
  state: async () => state(),
  versions: async () => core.versions(),
  logs: async () => logLines.join('').slice(-150000),
  createInstance: async ({ name, gameVersion, loader }) => exclusive(async () => {
    if (!name?.trim() || name.length > 80) throw new Error('实例名称应为 1–80 个字符');
    if (!/^[\w.\-]+$/.test(gameVersion)) throw new Error('请选择游戏版本');
    if (!['vanilla', 'fabric'].includes(loader)) throw new Error('不支持的加载器');
    const instance = { id: crypto.randomUUID(), name: name.trim(), gameVersion, loader, createdAt: new Date().toISOString() };
    await core.versionFor(instance);
    await fs.mkdir(instanceDirectory(instance), { recursive: true });
    config.instances.push(instance);
    await persist();
    return instance;
  }),
  importLocal: async () => exclusive(async () => {
    const result = await dialog.showOpenDialog(window, { title: '选择现有的 .minecraft 目录（应包含 versions 文件夹）', properties: ['openDirectory'] });
    if (result.canceled) return null;
    const directory = result.filePaths[0];
    const versionDir = path.join(directory, 'versions');
    if (!await exists(versionDir)) throw new Error('所选目录没有 versions 文件夹');
    const imported = [];
    for (const entry of await fs.readdir(versionDir, { withFileTypes: true })) {
      if (!entry.isDirectory() || !await exists(path.join(versionDir, entry.name, `${entry.name}.json`))) continue;
      if (config.instances.some(i => i.importRoot === directory && i.versionId === entry.name)) continue;
      const version = await core.resolveLocalVersion(entry.name, directory);
      const raw = JSON.parse(await fs.readFile(path.join(versionDir, entry.name, `${entry.name}.json`), 'utf8'));
      const ids = (version.libraries || []).map(l => l.name).join(' ');
      const loader = ids.includes('net.fabricmc:fabric-loader:') ? 'fabric' : ids.includes('net.neoforged:') ? 'neoforge' : ids.includes('net.minecraftforge:') ? 'forge' : ids.includes('org.quiltmc:') ? 'quilt' : 'vanilla';
      let base = raw;
      for (let n = 0; n < 10 && base.inheritsFrom; n++) base = JSON.parse(await fs.readFile(safePath(directory, `versions/${base.inheritsFrom}/${base.inheritsFrom}.json`), 'utf8'));
      imported.push({ id: crypto.randomUUID(), name: entry.name, gameVersion: base.id, versionId: entry.name, importRoot: directory, directory, loader, createdAt: new Date().toISOString() });
    }
    if (!imported.length) throw new Error('没有找到可导入的新版本');
    config.instances.push(...imported);
    await persist();
    return imported;
  }),
  installPack: async ({ project } = {}) => exclusive(async () => {
    let file;
    if (project) {
      const version = await core.projectVersion(project);
      const pack = version.files.find(f => f.primary && f.filename.endsWith('.mrpack')) || version.files.find(f => f.filename.endsWith('.mrpack'));
      if (!pack) throw new Error('该项目没有可下载的 .mrpack 整合包');
      file = safePath(core.root, `pack-cache/${version.id}.mrpack`);
      emit('progress', { stage: '下载整合包', current: 0, total: 0, detail: version.name });
      await core.download(pack.url, file, pack.hashes, pack.size);
    } else {
      const result = await dialog.showOpenDialog(window, { title: '导入 Modrinth 整合包', properties: ['openFile'], filters: [{ name: 'Modrinth 整合包', extensions: ['mrpack'] }] });
      if (result.canceled) return null;
      file = result.filePaths[0];
    }
    const instance = await core.installPack(file);
    config.instances.push(instance);
    await persist();
    return instance;
  }),
  search: async ({ query = '', type = 'mod', instanceId }) => core.search(query, type, instanceId ? instanceById(instanceId) : null),
  installMod: async ({ instanceId, project }) => exclusive(async () => {
    const instance = instanceById(instanceId);
    requireStopped(instance);
    return core.installMod(instance, project);
  }),
  listMods: async ({ instanceId }) => content.listContent(instanceDirectory(instanceById(instanceId)), 'mod'),
  removeContent: async ({ instanceId, type, world, file }) => exclusive(async () => {
    const instance = instanceById(instanceId); requireStopped(instance);
    await shell.trashItem(await content.contentFile(instanceDirectory(instance), type, world, file));
  }),
  worlds: async ({ instanceId }) => content.worlds(instanceDirectory(instanceById(instanceId))),
  listContent: async ({ instanceId, type, world }) => content.listContent(instanceDirectory(instanceById(instanceId)), type, world),
  importContent: async ({ instanceId, type, world }) => exclusive(async () => {
    const instance = instanceById(instanceId); requireStopped(instance);
    const directory = instanceDirectory(instance);
    await content.contentDirectory(directory, type, world);
    const result = await dialog.showOpenDialog(window, { title: '导入内容包', properties: ['openFile', 'multiSelections'], filters: [{ name: '内容包 ZIP', extensions: ['zip'] }] });
    if (result.canceled) return null;
    return content.importContent(directory, type, world, result.filePaths);
  }),
  toggleContent: async ({ instanceId, type, world, file }) => exclusive(async () => {
    const instance = instanceById(instanceId); requireStopped(instance);
    await content.toggleContent(instanceDirectory(instance), type, world, file);
  }),
  openContentFolder: async ({ instanceId, type, world }) => {
    const directory = await content.contentDirectory(instanceDirectory(instanceById(instanceId)), type, world);
    await fs.mkdir(directory, { recursive: true });
    const error = await shell.openPath(directory); if (error) throw new Error(error);
  },
  importMods: async ({ instanceId }) => exclusive(async () => {
    const instance = instanceById(instanceId);
    requireStopped(instance);
    if (instance.loader === 'vanilla') throw new Error('请先选择有 Mod 加载器的实例');
    const result = await dialog.showOpenDialog(window, { title: '导入 Mod（请自行确认版本与加载器兼容）', properties: ['openFile', 'multiSelections'], filters: [{ name: 'Java Mod', extensions: ['jar'] }] });
    if (result.canceled) return 0;
    const directory = path.join(instanceDirectory(instance), 'mods');
    await fs.mkdir(directory, { recursive: true });
    for (const file of result.filePaths) await fs.copyFile(file, safePath(directory, path.basename(file)));
    return result.filePaths.length;
  }),
  toggleMod: async ({ instanceId, file }) => exclusive(async () => {
    const instance = instanceById(instanceId);
    requireStopped(instance);
    if (!/\.jar(?:\.disabled)?$/i.test(file)) throw new Error('无效的 Mod 文件');
    const directory = path.join(instanceDirectory(instance), 'mods');
    const target = file.endsWith('.disabled') ? file.slice(0, -9) : `${file}.disabled`;
    if (await exists(safePath(directory, target))) throw new Error('目标文件已存在，请打开实例文件夹检查');
    await fs.rename(safePath(directory, file), safePath(directory, target));
  }),
  addOffline: async ({ name }) => {
    const account = offlineAccount(name);
    if (!config.accounts.some(a => a.uuid === account.uuid)) config.accounts.push(encodeAccount(account));
    config.selectedAccount = account.uuid;
    await persist(); emit('state', state());
    return publicAccount(account);
  },
  login: async () => {
    if (authController) throw new Error('登录正在进行');
    authController = new AbortController();
    try {
      const account = await auth.login(config.settings.microsoftClientId, authController.signal);
      config.accounts = config.accounts.filter(a => a.uuid !== account.uuid);
      config.accounts.push(encodeAccount(account)); config.selectedAccount = account.uuid;
      await persist(); emit('state', state());
      return publicAccount(account);
    } finally { authController = null; emit('auth-code', null); }
  },
  cancelLogin: async () => { authController?.abort(); },
  selectAccount: async ({ uuid }) => {
    if (!config.accounts.some(a => a.uuid === uuid)) throw new Error('账号不存在');
    config.selectedAccount = uuid; await persist(); emit('state', state());
  },
  removeAccount: async ({ uuid }) => {
    config.accounts = config.accounts.filter(a => a.uuid !== uuid);
    if (config.selectedAccount === uuid) config.selectedAccount = config.accounts[0]?.uuid || null;
    await persist(); emit('state', state());
  },
  settings: async input => exclusive(async () => {
    const settings = {
      javaPath: String(input.javaPath || '').trim(),
      memoryMb: Math.round(Math.min(65536, Math.max(1024, Number(input.memoryMb) || 4096))),
      microsoftClientId: String(input.microsoftClientId || '').trim(),
      proxy: String(input.proxy || '').trim(),
      theme: ['light', 'dark', 'system'].includes(input.theme) ? input.theme : 'dark',
      gameLanguage: ['auto', 'preserve', 'zh_cn', 'zh_tw', 'en_us', 'ja_jp'].includes(input.gameLanguage) ? input.gameLanguage : 'auto'
    };
    if (settings.proxy && !/^(https?|socks5):\/\/[^\s/@]+:\d{1,5}$/.test(settings.proxy)) throw new Error('代理格式应为 http://127.0.0.1:7890 或 socks5://127.0.0.1:1080');
    await session.defaultSession.setProxy(settings.proxy ? { proxyRules: settings.proxy } : { mode: 'system' });
    config.settings = settings; applyTitleTheme(); await persist();
    return settings;
  }),
  pickJava: async () => {
    const result = await dialog.showOpenDialog(window, { title: '选择 Java 可执行文件', properties: ['openFile'], filters: [{ name: 'Java', extensions: ['exe'] }] });
    return result.canceled ? null : result.filePaths[0];
  },
  checkJava: async ({ javaPath }) => inspectJava(javaPath || config.settings.javaPath || 'java'),
  launch: async ({ instanceId }) => exclusive(async () => {
    const instance = instanceById(instanceId);
    if (running.size) throw new Error('已有游戏正在运行，请先关闭后再启动');
    let stored = config.accounts.find(a => a.uuid === config.selectedAccount);
    if (!stored) throw new Error('请先在账号页面添加一个账号');
    let account = { ...stored, ...decodeSecrets(stored) };
    if (account.type === 'microsoft' && account.expiresAt < Date.now() + 60000) {
      account = await auth.refresh(config.settings.microsoftClientId, account.refreshToken);
      stored = encodeAccount(account);
      config.accounts = config.accounts.map(a => a.uuid === account.uuid ? stored : a);
      await persist();
    }
    const child = await core.launch(instance, { ...config.settings, systemLocale: app.getLocale() }, account);
    running.set(instanceId, child);
    child.once('close', code => { running.delete(instanceId); emit('log', `\n游戏已退出，退出码：${code}\n`); emit('state', state()); });
    instance.lastPlayed = new Date().toISOString();
    await persist();
    return { pid: child.pid };
  }),
  openFolder: async ({ instanceId } = {}) => {
    const directory = instanceId ? instanceDirectory(instanceById(instanceId)) : core.root;
    await fs.mkdir(directory, { recursive: true });
    const error = await shell.openPath(directory);
    if (error) throw new Error(error);
  },
  openLogin: async ({ url }) => {
    const parsed = new URL(url);
    if (parsed.protocol !== 'https:' || !['www.microsoft.com', 'microsoft.com', 'login.microsoftonline.com', 'login.live.com'].includes(parsed.hostname)) throw new Error('登录地址不受支持');
    await shell.openExternal(parsed.href);
  }
};

async function boot() {
  const defaults = { settings: { javaPath: '', memoryMb: 4096, microsoftClientId: '', proxy: '', theme: 'dark', gameLanguage: 'auto' }, accounts: [], instances: [], selectedAccount: null };
  if (await exists(configFile())) {
    try { const saved = JSON.parse(await fs.readFile(configFile(), 'utf8')); config = { ...defaults, ...saved, settings: { ...defaults.settings, ...saved.settings } }; }
    catch { throw new Error(`配置文件损坏，请先备份并检查：${configFile()}`); }
  } else config = defaults;
  const root = path.join(app.getPath('userData'), 'library');
  await fs.mkdir(root, { recursive: true });
  await session.defaultSession.setProxy(config.settings.proxy ? { proxyRules: config.settings.proxy } : { mode: 'system' });
  const fetcher = (...args) => net.fetch(...args);
  core = new LauncherCore(root, { fetch: fetcher, emit });
  auth = new MicrosoftAuth(fetcher, emit);
  require('electron').nativeTheme.on('updated', () => applyTitleTheme());
  for (const [name, handler] of Object.entries(handlers)) ipcMain.handle(`craft:${name}`, async (event, input) => {
    if (event.sender !== window?.webContents || !event.senderFrame?.url.startsWith('file://')) return { ok: false, error: '请求来源不受支持' };
    try { return { ok: true, data: await handler(input) }; }
    catch (e) { return { ok: false, error: e.name === 'AbortError' ? '登录已取消' : redact(e.message) }; }
  });
  createWindow();
}
function applyTitleTheme() {
  const light = config.settings.theme === 'light' || config.settings.theme === 'system' && !require('electron').nativeTheme.shouldUseDarkColors;
  window?.setTitleBarOverlay({ color: light ? '#f2f5ee' : '#111815', symbolColor: light ? '#334b36' : '#b7c6bd', height: 42 });
}
function createWindow() {
  window = new BrowserWindow({ width: 1300, height: 860, minWidth: 1050, minHeight: 720, backgroundColor: '#111815', title: 'Craft Launcher',
    titleBarStyle: 'hidden', titleBarOverlay: { color: '#111815', symbolColor: '#b7c6bd', height: 42 },
    webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false, sandbox: true } });
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', event => event.preventDefault());
  applyTitleTheme();
  window.loadFile(path.join(__dirname, 'renderer/index.html'));
}
app.whenReady().then(boot).catch(e => { dialog.showErrorBox('Craft Launcher 无法启动', e.message); app.quit(); });
app.on('window-all-closed', () => { authController?.abort(); if (process.platform !== 'darwin') app.quit(); });
app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
