const api = window.craft;
const $ = selector => document.querySelector(selector);
const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
let state, page = 'home', selected, contentType = 'mod', versions = [], log = '', toastTimer, searchSequence = 0;
let managedType = 'resourcepack', managedWorld = '';
const themeQuery = matchMedia('(prefers-color-scheme: light)');
function applyTheme() { document.documentElement.dataset.theme = state.settings.theme === 'light' || state.settings.theme === 'system' && themeQuery.matches ? 'light' : 'dark'; }
themeQuery.addEventListener('change', () => { if (state) applyTheme(); });
const instance = () => state.instances.find(i => i.id === selected);
const button = (action, label, cls = 'secondary', attrs = '') => `<button class="${cls}" data-action="${action}" ${attrs}>${label}</button>`;
const heading = (title, subtitle, actions = '') => `<div class="page-heading"><div><div class="eyebrow">CRAFT / YOUR NEXT ADVENTURE</div><h1>${title}</h1><p class="heading-caption">${subtitle}</p></div><div class="actions">${actions}</div></div>`;
const options = () => state.instances.map(i => `<option value="${escape(i.id)}" ${selected === i.id ? 'selected' : ''}>${escape(i.name)} · ${escape(i.gameVersion)}</option>`).join('');
const empty = (title, text, actions = '') => `<div class="empty"><div class="empty-symbol">◇</div><h2>${title}</h2><p>${text}</p>${actions}</div>`;
function toast(message, error = false) {
  clearTimeout(toastTimer); $('#toast').textContent = message; $('#toast').className = `toast${error ? ' error' : ''}`;
  toastTimer = setTimeout(() => $('#toast').classList.add('hidden'), error ? 9000 : 4500);
}
async function perform(action, success) {
  try { const result = await action(); if (success && result !== null) toast(success); return result; }
  catch (e) { toast(e.message, true); return null; }
}
function syncState(next) {
  state = next; applyTheme();
  if (!state.instances.some(i => i.id === selected)) selected = state.instances[0]?.id;
  $('#instance-count').textContent = state.instances.length;
  const account = state.accounts.find(a => a.uuid === state.selectedAccount);
  $('#profile-name').textContent = account?.name || '还未添加账号';
  $('#profile-type').textContent = account ? account.type === 'microsoft' ? 'Microsoft · 正版账号' : '本地离线账号' : '点击设置你的玩家身份';
  $('.avatar').textContent = account?.name[0]?.toUpperCase() || '?';
  $('#status-text').textContent = state.busy ? '任务进行中，请稍候…' : state.running.length ? '游戏正在运行，祝你玩得开心。' : '就绪，准备出发。';
}
function navigate(next) {
  page = next; searchSequence++;
  document.querySelectorAll('[data-page]').forEach(el => el.classList.toggle('active', el.dataset.page === page));
  render();
}
function render() {
  const i = instance();
  if (page === 'home') {
    $('#main').innerHTML = heading('准备好，进入你的世界。', '每一次出发，都有新的可能。', '<span class="header-index">LET’S BUILD SOMETHING.</span>') +
      `<section class="hero"><div class="hero-copy"><span class="pill">A WORLD OF POSSIBILITIES</span><h2>下一段冒险，<br>从这里生长。</h2><p>熟悉的方块，无限的灵感。<br>选择你的世界，剩下的交给 Craft。</p></div><div class="hero-footer"><span>探索 / 建造 / 与朋友一起</span><b>JAVA EDITION</b></div></section>` +
      `<div class="section-head"><h2>继续你的旅程</h2>${button('instances', '管理所有实例 ↗', 'text-button')}</div><section class="launch-panel"><div class="block-icon">▧</div><div class="launch-info">${i ? `<select id="home-instance" aria-label="选择游戏实例">${options()}</select><p>${escape(i.loader.toUpperCase())} · 独立游戏环境${i.lastPlayed ? ' · 上次游玩 ' + new Date(i.lastPlayed).toLocaleDateString('zh-CN') : ' · 准备开始新的冒险'}</p>` : '<h3>还没有游戏实例</h3><p>从原版开始，或导入你喜欢的整合包。</p>'}</div>${button(i ? 'launch' : 'create', i ? '开始游戏 <span>▷</span>' : '创建实例 <span>＋</span>', 'primary launch', state.busy || state.running.length ? 'disabled' : '')}</section>` +
      `<div class="quick-grid"><button class="quick-card" data-action="create"><span class="quick-icon">＋</span><div><strong>创建新实例</strong><small>原版与 Fabric，自由选择</small></div><span class="arrow">↗</span></button><button class="quick-card" data-action="packs"><span class="quick-icon">▦</span><div><strong>发现整合包</strong><small>为下一场冒险寻找灵感</small></div><span class="arrow">↗</span></button><button class="quick-card" data-action="importPack"><span class="quick-icon">↓</span><div><strong>导入整合包</strong><small>安装本地 .mrpack 文件</small></div><span class="arrow">↗</span></button></div><div class="note"><div><strong>为属于你的世界而造。</strong><br>独立实例 · Modrinth 内容 · 开源代码</div><div>CRAFT LAUNCHER<br>与 Mojang / Microsoft 无隶属关系</div></div>`;
  } else if (page === 'instances') {
    $('#main').innerHTML = heading('我的实例', '让每个世界拥有自己的空间。', button('importLocal', '导入本地游戏') + button('create', '＋ 创建实例', 'primary')) +
      (state.instances.length ? `<div class="grid">${state.instances.map(item => `<article class="instance-card ${item.id === selected ? 'selected' : ''}" data-instance="${escape(item.id)}"><div class="instance-top"><div class="block-icon">▧</div><div><h3>${escape(item.name)}</h3><small>Minecraft ${escape(item.gameVersion)}</small></div></div><span class="tag">${escape(item.loader.toUpperCase())}</span> ${item.importRoot ? '<span class="tag amber">共享本地目录</span>' : ''}<div class="actions">${button('launch', '▷ 启动', 'primary', `data-id="${item.id}" ${state.busy || state.running.length ? 'disabled' : ''}`)}${button('folder', '文件夹', 'secondary', `data-id="${item.id}"`)}${button('instanceMods', 'Mods', 'secondary', `data-id="${item.id}"`)}</div></article>`).join('')}</div>` : empty('第一个世界，等你命名', '创建原版或 Fabric 实例，也可以导入已有的 .minecraft 目录。', button('create', '创建实例', 'primary')));
  } else if (page === 'discover') {
    $('#main').innerHTML = heading('探索内容', '从 Modrinth 寻找让世界更有趣的点子。') + `<div class="tabs"><button class="tab ${contentType === 'mod' ? 'active' : ''}" data-action="tabMod">Mods</button><button class="tab ${contentType === 'modpack' ? 'active' : ''}" data-action="tabPack">整合包</button></div><form id="search-form" class="searchbar"><input id="query" placeholder="${contentType === 'mod' ? '搜索 Mod，例如 Sodium…' : '搜索整合包…'}" aria-label="搜索内容">${contentType === 'mod' ? `<select id="search-instance" aria-label="目标实例"><option value="">选择目标实例</option>${options()}</select>` : ''}<button class="primary" type="submit">搜索</button></form><p class="hint">${contentType === 'mod' ? '按实例的游戏版本与加载器筛选，必要依赖会一同安装。' : '当前支持原版与 Fabric 整合包；Forge / NeoForge 整合包暂不支持。'}</p><div id="results"></div>`;
    if (contentType === 'modpack' || i) runSearch();
    else $('#results').innerHTML = empty('先选一个目标实例', 'Mod 需要与游戏版本、加载器匹配。请先创建 Fabric 实例。', button('create', '创建实例', 'primary'));
  } else if (page === 'mods') {
    $('#main').innerHTML = heading('Mod 管理', '为每个世界，搭配恰到好处的可能。', button('importMods', '导入本地 JAR') + button('discoverMods', '发现 Mods', 'primary')) + `<div class="searchbar"><select id="mods-instance" aria-label="管理的实例">${options() || '<option>尚无实例</option>'}</select></div><div id="mod-list"></div>`;
    loadMods();
  } else if (page === 'content') {
    $('#main').innerHTML = heading('资源与内容包', '为世界换一种风景。', button('importContent', '＋ 导入 ZIP', 'primary') + button('contentFolder', '打开文件夹')) + `<div class="tabs">${[['resourcepack','资源包'],['datapack','数据包'],['shader','光影包']].map(([type,label]) => `<button class="tab ${managedType === type ? 'active' : ''}" data-action="contentTab" data-type="${type}">${label}</button>`).join('')}</div><div class="searchbar"><select id="content-instance" aria-label="内容包实例">${options() || '<option value="">尚无实例</option>'}</select>${managedType === 'datapack' ? '<select id="content-world" aria-label="目标世界"><option value="">正在读取世界…</option></select>' : ''}</div><p class="hint">${managedType === 'resourcepack' ? '导入后在游戏的「选项 → 资源包」中选择。停用会移出游戏可读取的包；恢复后可重新选择。' : managedType === 'shader' ? '需要 Iris 或 OptiFine。导入后在游戏的视频设置中选择；恢复后需重新选择光影。' : '数据包属于具体世界。恢复或导入后，进入游戏使用 /datapack list、/datapack enable 和 /reload 确认启用状态；启动器不修改世界内部的启用列表。'}</p><div id="content-list"></div>`;
    loadContent();
  } else if (page === 'accounts') {
    $('#main').innerHTML = heading('账号管理', '选择你在方块世界里的身份。', button('login', '＋ Microsoft 登录', 'primary')) + `<section class="panel"><h2>你的账号</h2>${state.accounts.length ? state.accounts.map(a => `<div class="account-row"><div class="avatar">${escape(a.name[0].toUpperCase())}</div><div class="account-info"><strong>${escape(a.name)} ${a.uuid === state.selectedAccount ? '<span class="tag">当前账号</span>' : ''}</strong><small>${a.type === 'microsoft' ? 'Microsoft 正版账号' : '本地离线账号 · 无法进入正版验证服务器'}</small></div>${button('selectAccount', '选择', 'secondary', `data-id="${a.uuid}"`)}${button('removeAccount', '移除', 'secondary', `data-id="${a.uuid}"`)}</div>`).join('') : '<p>还没有账号。添加正版账号，或创建本地离线身份。</p>'}</section><section class="panel"><h2>本地离线身份</h2><p>用于本地单人游戏或允许离线身份的服务器，不提供正版服务器认证。</p><form id="offline-form"><label for="offline-name">玩家名</label><div class="field-row"><input id="offline-name" required pattern="[A-Za-z0-9_]{3,16}" maxlength="16" placeholder="3–16 位字母、数字或下划线"><button class="secondary">添加离线账号</button></div></form></section><p class="hint">Microsoft 登录使用设备代码流程，不收集密码。首次使用需要在设置中填写你自己的应用 Client ID。</p>`;
  } else if (page === 'settings') {
    const s = state.settings;
    $('#main').innerHTML = heading('启动器设置', '为你的电脑，找到合适的运行方式。') + `<form id="settings-form"><section class="panel"><h2>外观与游戏语言</h2><label for="theme">启动器配色</label><select id="theme">${[['dark','深色 · 森林'],['light','浅色 · 晨光'],['system','跟随系统']].map(([v,l]) => `<option value="${v}" ${s.theme === v ? 'selected' : ''}>${l}</option>`).join('')}</select><label for="game-language">自动配置游戏语言</label><select id="game-language">${[['auto','跟随系统（自动）'],['zh_cn','简体中文'],['zh_tw','繁體中文'],['en_us','English'],['ja_jp','日本語'],['preserve','保留游戏原设置']].map(([v,l]) => `<option value="${v}" ${s.gameLanguage === v ? 'selected' : ''}>${l}</option>`).join('')}</select><small class="hint">启动前写入 options.txt，仅更新语言项，保留其他游戏选项；对所有启动的实例生效。</small></section><section class="panel"><h2>游戏运行环境</h2><label for="java-path">Java 路径</label><div class="field-row"><input id="java-path" value="${escape(s.javaPath)}" placeholder="留空使用 PATH 中的 java">${button('pickJava', '浏览')}${button('checkJava', '检测')}</div><small id="java-result" class="hint">不会修改你的 Java 环境变量。</small><label for="memory">最大游戏内存（MB）</label><input id="memory" type="number" min="1024" max="65536" step="256" value="${s.memoryMb}"></section><section class="panel"><h2>下载与账号</h2><label for="proxy">启动器网络代理</label><input id="proxy" value="${escape(s.proxy)}" placeholder="留空使用系统代理，例如 http://127.0.0.1:7890"><small class="hint">下载和登录使用这里的代理。Java 的 JAVA_TOOL_OPTIONS 等环境变量会独立继承。</small><label for="client-id">Microsoft 应用 Client ID</label><input id="client-id" value="${escape(s.microsoftClientId)}" placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"><small class="hint">需要你注册的公共客户端应用，开启设备代码流，并获得 Minecraft Java API 的访问授权。未配置时可使用离线身份。</small></section><section class="panel"><h2>文件位置</h2><p>${escape(state.libraryRoot)}</p><small class="hint">新实例独立保存；导入已有游戏会使用原目录，其中 Mods 与存档可能由多个版本共享。</small></section><div class="settings-footer"><button class="primary" type="submit">保存设置</button><small>设置会保存在此电脑。</small></div></form>`;
  } else if (page === 'logs') {
    $('#main').innerHTML = heading('运行日志', '游戏进程输出，保留最近的日志。', button('refreshLogs', '刷新日志')) + '<pre class="log-box" id="log-output"></pre>';
    $('#log-output').textContent = log || '尚无游戏运行日志。';
  }
}
async function runSearch() {
  const sequence = ++searchSequence;
  const target = $('#search-instance')?.value;
  if (contentType === 'mod' && !target) { $('#results').innerHTML = empty('选择目标实例', '先选择一个安装了加载器的实例，再搜索兼容的 Mods。'); return; }
  const query = $('#query')?.value || '';
  $('#results').innerHTML = '<div class="loading">正在从 Modrinth 获取内容…</div>';
  try {
    const results = await api.search({ query, type: contentType, instanceId: target });
    if (sequence !== searchSequence || page !== 'discover') return;
    $('#results').innerHTML = results.length ? results.map(item => {
      const icon = typeof item.icon_url === 'string' && item.icon_url.startsWith('https://cdn.modrinth.com/') ? `<img class="result-icon" src="${escape(item.icon_url)}" alt="" loading="lazy">` : '<div class="result-icon">◇</div>';
      return `<article class="result">${icon}<div class="result-info"><h3>${escape(item.title)}</h3><p>${escape(item.description)}</p><small>${escape(item.author)} · ${Number(item.downloads).toLocaleString()} 次下载</small></div>${button(contentType === 'mod' ? 'installMod' : 'installProjectPack', '安装', 'secondary', `data-project="${escape(item.project_id)}" ${state.busy ? 'disabled' : ''}`)}</article>`;
    }).join('') : empty('没有找到匹配内容', '试试其他关键词，或选择不同的游戏版本。');
  } catch (e) { if (sequence === searchSequence && page === 'discover') $('#results').innerHTML = empty('暂时无法获取内容', escape(e.message), button('retrySearch', '重试')); }
}
async function loadMods() {
  const id = selected;
  if (!id) { $('#mod-list').innerHTML = empty('还没有游戏实例', '创建 Fabric 实例后，就可以安装和管理 Mods。', button('create', '创建实例', 'primary')); return; }
  $('#mod-list').innerHTML = '<div class="loading">读取 Mods…</div>';
  try {
    const mods = await api.listMods({ instanceId: id });
    if (page !== 'mods' || selected !== id) return;
    $('#mod-list').innerHTML = mods.length ? `<section class="panel">${mods.map(m => `<div class="mod-row ${m.enabled ? '' : 'disabled'}"><span class="tag">${m.enabled ? '已启用' : '已禁用'}</span><div class="mod-info"><strong>${escape(m.name || m.file)}</strong><small>${escape([m.version, m.loader, m.modId].filter(Boolean).join(" · "))}</small><small>${escape(m.description || m.error || (m.recognized ? "" : "未识别元数据，请确认这是有效 Mod"))}</small><small>${escape(m.file)}${m.loader && m.loader !== instance().loader ? " · ⚠ 与当前实例加载器不同" : ""}</small></div>${button('toggleMod', m.enabled ? '禁用' : '启用', 'secondary', `data-file="${escape(m.file)}"`)}${button('removeContent', '移到回收站', 'secondary', `data-file="${escape(m.file)}"`)}</div>`).join('')}</section>` : empty('给这个世界加点新意', '从 Modrinth 安装兼容的 Mods，或导入本地 JAR 文件。', button('discoverMods', '发现 Mods', 'primary'));
  } catch (e) { if (page === 'mods') $('#mod-list').innerHTML = empty('读取失败', escape(e.message)); }
}
function closeDialog() { if ($('#dialog').open) $('#dialog').close(); }
async function createDialog() {
  $('#dialog-body').innerHTML = '<div class="eyebrow">NEW ADVENTURE</div><h2>创建新实例</h2><p>每个实例拥有独立的 Mods、配置与存档。</p><form id="create-form"><label for="instance-name">实例名称</label><input id="instance-name" placeholder="我的新世界" required maxlength="80"><label for="game-version">Minecraft 版本</label><select id="game-version" required disabled><option>正在获取版本…</option></select><label for="loader">Mod 加载器</label><select id="loader"><option value="fabric">Fabric · 支持安装 Mods</option><option value="vanilla">原版 · Vanilla</option></select><div class="error-inline" id="create-error"></div><div class="actions"><button class="primary" id="create-submit" disabled>创建实例</button></div></form>';
  $('#dialog').showModal();
  try {
    if (!versions.length) versions = await api.versions();
    if (!$('#game-version')) return;
    $('#game-version').innerHTML = versions.map(v => `<option value="${escape(v.id)}">${escape(v.id)}</option>`).join('');
    $('#game-version').disabled = false; $('#create-submit').disabled = false;
  } catch (e) { if ($('#create-error')) $('#create-error').textContent = e.message; }
}
async function installPack(project) {
  const result = await perform(() => api.installPack(project ? { project } : {}), '整合包安装完成');
  if (result) { selected = result.id; syncState(await api.state()); navigate('instances'); }
}
document.addEventListener('click', async event => {
  const nav = event.target.closest('[data-page]');
  if (nav) return navigate(nav.dataset.page);
  const el = event.target.closest('[data-action]');
  if (!el) {
    const card = event.target.closest('[data-instance]');
    if (card) { selected = card.dataset.instance; render(); }
    return;
  }
  const action = el.dataset.action;
  if (action === 'create') return createDialog();
  if (action === 'instances') return navigate('instances');
  if (action === 'packs' || action === 'tabPack') { contentType = 'modpack'; return navigate('discover'); }
  if (action === 'tabMod' || action === 'discoverMods' || action === 'instanceMods') {
    if (el.dataset.id) selected = el.dataset.id;
    contentType = 'mod'; return navigate(action === 'instanceMods' ? 'mods' : 'discover');
  }
  if (action === 'removeContent') {
    await perform(() => api.removeContent({ instanceId: selected, type: page === 'mods' ? 'mod' : managedType, world: managedWorld, file: el.dataset.file }), '已移到回收站');
    return page === 'mods' ? loadMods() : loadContent();
  }
  if (action === 'contentTab') { managedType = el.dataset.type; managedWorld = ''; return render(); }
  if (action === 'contentFolder' || action === 'importContent' || action === 'toggleContent') {
    if (!selected) return toast('请先选择实例', true);
    const input = { instanceId: selected, type: managedType, world: managedWorld, file: el.dataset.file };
    if (managedType === 'datapack' && !managedWorld) return toast('请先选择一个已创建的世界', true);
    await perform(() => api[action === 'contentFolder' ? 'openContentFolder' : action](input), action === 'importContent' ? '内容包已导入，请在游戏中选择启用' : undefined);
    if (action !== 'contentFolder') await loadContent();
    return;
  }
  if (action === 'retrySearch') return runSearch();
  if (action === 'importPack') return installPack();
  if (action === 'installProjectPack') return installPack(el.dataset.project);
  if (action === 'importLocal') {
    const result = await perform(() => api.importLocal(), '本地版本已导入');
    if (result?.length) { selected = result[0].id; syncState(await api.state()); navigate('instances'); }
    return;
  }
  if (action === 'launch') {
    const id = el.dataset.id || selected;
    if (!id) return toast('请先创建或选择实例', true);
    el.disabled = true;
    await perform(() => api.launch({ instanceId: id }), '游戏进程已启动');
    syncState(await api.state()); render(); return;
  }
  if (action === 'folder') return perform(() => api.openFolder({ instanceId: el.dataset.id || selected }));
  if (action === 'installMod') {
    const id = $('#search-instance').value;
    el.disabled = true;
    await perform(() => api.installMod({ instanceId: id, project: el.dataset.project }), 'Mod 与必要依赖已安装');
    el.disabled = false; return;
  }
  if (action === 'importMods') { if (!selected) return toast('请先选择实例', true); await perform(() => api.importMods({ instanceId: selected }), '本地 Mods 已导入'); return loadMods(); }
  if (action === 'toggleMod') { await perform(() => api.toggleMod({ instanceId: selected, file: el.dataset.file })); return loadMods(); }
  if (action === 'selectAccount' || action === 'removeAccount') { await perform(() => api[action]({ uuid: el.dataset.id })); syncState(await api.state()); return render(); }
  if (action === 'login') {
    if (!state.settings.microsoftClientId) { navigate('settings'); return toast('请先填写已获授权的 Microsoft 应用 Client ID', true); }
    const account = await perform(() => api.login(), 'Microsoft 登录成功');
    closeDialog(); if (account) { syncState(await api.state()); navigate('accounts'); } return;
  }
  if (action === 'cancelLogin') { await api.cancelLogin(); return closeDialog(); }
  if (action === 'openLogin') return perform(() => api.openLogin({ url: el.dataset.url }));
  if (action === 'pickJava') { const file = await perform(() => api.pickJava()); if (file) $('#java-path').value = file; return; }
  if (action === 'checkJava') { const result = await perform(() => api.checkJava({ javaPath: $('#java-path').value })); if (result) $('#java-result').textContent = result.description; return; }
  if (action === 'refreshLogs') { log = await api.logs(); render(); }
});
document.addEventListener('change', event => {
  if (event.target.id === 'theme') { document.documentElement.dataset.theme = event.target.value === 'light' || event.target.value === 'system' && themeQuery.matches ? 'light' : 'dark'; }
  if (event.target.id === 'content-world') { managedWorld = event.target.value; loadContent(false); }
  if (['home-instance', 'mods-instance', 'search-instance', 'content-instance'].includes(event.target.id)) {
    selected = event.target.value;
    if (page === 'content') { managedWorld = ''; loadContent(); }
    if (page === 'mods') loadMods();
    if (page === 'discover') runSearch();
  }
});
document.addEventListener('submit', async event => {
  event.preventDefault();
  if (event.target.id === 'search-form') return runSearch();
  if (event.target.id === 'offline-form') { await perform(() => api.addOffline({ name: $('#offline-name').value }), '离线身份已添加'); syncState(await api.state()); return render(); }
  if (event.target.id === 'settings-form') {
    await perform(() => api.settings({ javaPath: $('#java-path').value, memoryMb: $('#memory').value, microsoftClientId: $('#client-id').value, proxy: $('#proxy').value, theme: $('#theme').value, gameLanguage: $('#game-language').value }), '设置已保存');
    syncState(await api.state()); return;
  }
  if (event.target.id === 'create-form') {
    const submit = $('#create-submit'); submit.disabled = true;
    try {
      const result = await api.createInstance({ name: $('#instance-name').value, gameVersion: $('#game-version').value, loader: $('#loader').value });
      selected = result.id; closeDialog(); syncState(await api.state()); navigate('instances'); toast('实例已创建，首次启动时下载游戏文件');
    } catch (e) { $('#create-error').textContent = e.message; }
    finally { if (submit.isConnected) submit.disabled = false; }
  }
});
$('#dialog-close').onclick = () => { api.cancelLogin(); closeDialog(); };
$('#dialog').addEventListener('cancel', () => api.cancelLogin());
$('#account-shortcut').onclick = () => navigate('accounts');
$('#folder-shortcut').onclick = () => perform(() => api.openFolder({}));
api.onEvent(({ type, data }) => {
  if (type === 'state') { syncState(data); if (['home', 'instances'].includes(page)) render(); }
  if (type === 'progress') {
    $('#task').classList.toggle('hidden', !data);
    if (data) { $('#task-title').textContent = data.stage; $('#task-detail').textContent = data.detail || ''; $('#task-value').textContent = data.total ? `${Math.round(data.current / data.total * 100)}%` : ''; $('#task-progress').max = data.total || 1; if (data.total) $('#task-progress').value = data.current; else $('#task-progress').removeAttribute('value'); }
  }
  if (type === 'log') { log = (log + data).slice(-150000); if ($('#log-output')) { $('#log-output').textContent = log; $('#log-output').scrollTop = $('#log-output').scrollHeight; } }
  if (type === 'auth-code') {
    if (!data) return closeDialog();
    closeDialog();
    $('#dialog-body').innerHTML = `<div class="eyebrow">MICROSOFT ACCOUNT</div><h2>在浏览器中完成登录</h2><p>打开微软登录页面，输入下面的设备代码。</p><div class="device-code">${escape(data.code)}</div><p>请在 ${Math.ceil(data.expiresIn / 60)} 分钟内完成授权。Craft 不会获取你的密码。</p><div class="actions">${button('cancelLogin', '取消')}${button('openLogin', '打开微软登录页面 ↗', 'primary', `data-url="${escape(data.url)}"`)}</div>`;
    $('#dialog').showModal();
  }
});
api.state().then(value => { syncState(value); render(); }).catch(e => { $('#main').textContent = `启动失败：${e.message}`; });

let contentSequence = 0;
async function loadContent(refreshWorlds = true) {
  const seq = ++contentSequence;
  const id = selected, type = managedType;
  const current = () => page === 'content' && seq === contentSequence && id === selected && type === managedType;
  if (!id) { $('#content-list').innerHTML = empty('先创建一个游戏实例', '内容包会保存在所选实例中。', button('create', '创建实例', 'primary')); return; }
  $('#content-list').innerHTML = '<div class="loading">读取内容包…</div>';
  try {
    if (type === 'datapack' && refreshWorlds) {
      const worlds = await api.worlds({ instanceId: id });
      if (!current()) return;
      if (!worlds.includes(managedWorld)) managedWorld = worlds[0] || '';
      $('#content-world').innerHTML = worlds.length ? worlds.map(w => `<option value="${escape(w)}" ${w === managedWorld ? 'selected' : ''}>${escape(w)}</option>`).join('') : '<option value="">还没有世界</option>';
    }
    if (type === 'datapack' && !managedWorld) { $('#content-list').innerHTML = empty('还没有可以管理的世界', '请先在此实例中创建并保存世界，再回来导入数据包。'); return; }
    const items = await api.listContent({ instanceId: id, type, world: managedWorld });
    if (!current()) return;
    $('#content-list').innerHTML = items.length ? `<section class="panel">${items.map(item => `<div class="mod-row ${item.enabled ? '' : 'disabled'}"><span class="tag">${item.enabled ? '可用' : '已停用'}</span><div class="mod-info"><strong>${escape(item.name)}</strong><small>${escape(item.version)}</small><small>${escape(item.description || item.error || '未识别包元数据')}</small><small>${escape(item.file)}</small></div>${button('toggleContent', item.enabled ? '停用' : '恢复', 'secondary', `data-file="${escape(item.file)}"`)}${button('removeContent', '移到回收站', 'secondary', `data-file="${escape(item.file)}"`)}</div>`).join('')}</section>` : empty('还没有内容包', '导入 ZIP 文件，或通过文件夹放入已解压的内容包。', button('importContent', '导入 ZIP', 'primary'));
  } catch (e) { if (current()) $('#content-list').innerHTML = empty('读取失败', escape(e.message)); }
}
