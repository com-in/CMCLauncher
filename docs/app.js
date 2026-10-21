const menuButton = document.querySelector('.menu-toggle');
const nav = document.querySelector('#main-nav');
const languageButton = document.querySelector('#language-toggle');

const translations = {
  en: {
    title: 'CMC Launcher — Start your next world',
    description: 'CMC Launcher is a Windows launcher for Minecraft Java Edition. Explore game versions, manage instances, and find community content.',
    brandAria: 'CMC Launcher home', menuOpen: 'Open menu', menuClose: 'Close menu', navAria: 'Main navigation',
    navFeatures: 'Features', navAbout: 'About', navDownload: 'Download', languageButton: '中文', languageAria: 'Switch to Chinese',
    heroTitleA: 'Your world,', heroTitleB: 'your next adventure.', heroDescription: 'A Windows launcher for Minecraft Java Edition.',
    heroCta: 'View releases', heroBrowse: 'Explore features', shotAlt: 'CMC Launcher 0.5.0 release home screen',
    shotCaption: 'Release build · English game language', scrollCue: 'Scroll to explore', featuresTitleA: 'More time for', featuresTitleB: 'your next adventure.',
    feature1Title: 'Manage your instances', feature1Copy: 'Organize Minecraft versions and modpacks, then switch between the worlds you play.',
    feature2Title: 'Game versions and community content', feature2Copy: 'Browse Minecraft versions and community projects, including mods, modpacks, and content packs.',
    feature3Title: 'Set up the way you play', feature3Copy: 'Choose a game version, adjust launch options, and keep your game content organized.',
    aboutTitleA: 'Made for the', aboutTitleB: 'Minecraft community.',
    about1Title: 'Built for Minecraft Java Edition', about1Copy: 'A focused place to explore game versions and community content.',
    about2Title: 'A familiar Windows experience', about2Copy: 'Built with WinUI 3, with light, dark, and custom themes.',
    about3Title: 'Open source', about3Copy: 'Explore the code, follow development, or contribute to the project.',
    downloadTitle: 'Ready for your next world?', downloadDescription: 'CMC Launcher 0.5.0 · Windows x64',
    releaseState: 'No official release published yet', releaseLink: 'View GitHub releases',
    footerNote: 'Made for Minecraft Java Edition. Minecraft is a trademark of Mojang Studios.', githubProject: 'GitHub project'
  },
  zh: {
    title: 'CMC Launcher — 你的世界，由此开始',
    description: 'CMC Launcher 是面向 Minecraft Java 版的 Windows 启动器。浏览游戏版本、管理实例和探索社区内容。',
    brandAria: 'CMC Launcher 首页', menuOpen: '打开菜单', menuClose: '关闭菜单', navAria: '主导航',
    navFeatures: '功能', navAbout: '关于', navDownload: '下载', languageButton: 'EN', languageAria: 'Switch to English',
    heroTitleA: '你的世界，', heroTitleB: '由此开始。', heroDescription: '一个为 Minecraft Java Edition 打造的 Windows 启动器。',
    heroCta: '查看发行版', heroBrowse: '看看它能做什么', shotAlt: 'CMC Launcher 0.5.0 发布版主页界面',
    shotCaption: '发布版截图 · 游戏语言为 English', scrollCue: '继续探索', featuresTitleA: '把时间留给', featuresTitleB: '下一次冒险。',
    feature1Title: '实例，按你的方式管理', feature1Copy: '整理 Minecraft 版本与整合包实例，按需切换游玩内容。',
    feature2Title: '游戏下载与社区内容', feature2Copy: '获取 Minecraft 游戏版本与社区资源，支持 Mod、整合包和常见内容包。',
    feature3Title: '贴合你的游玩习惯', feature3Copy: '选择适合的游戏版本、调整启动选项，并管理自己的游戏内容。',
    aboutTitleA: '为玩家打造', aboutTitleB: '属于你的方块世界。',
    about1Title: '为 Minecraft Java Edition 打造', about1Copy: '从游戏版本到社区内容，为日常游玩提供一个专注的入口。',
    about2Title: '熟悉的 Windows 体验', about2Copy: '以 WinUI 3 构建，支持浅色、深色与自定义主题。',
    about3Title: '开源协作', about3Copy: '查看源码、跟进项目进展，或参与后续开发。',
    downloadTitle: '准备好，进入你的世界。', downloadDescription: 'CMC Launcher 0.5.0 · Windows x64',
    releaseState: 'GitHub 暂无正式 Release', releaseLink: '查看发行版',
    footerNote: '为 Minecraft Java Edition 打造。Minecraft 是 Mojang Studios 的商标。', githubProject: 'GitHub 项目'
  }
};

function setLanguage(language, save = true) {
  const locale = language === 'zh' ? 'zh' : 'en';
  const strings = translations[locale];
  document.documentElement.lang = locale === 'zh' ? 'zh-CN' : 'en';
  document.title = strings.title;
  document.querySelector('meta[name="description"]').content = strings.description;
  document.querySelectorAll('[data-i18n]').forEach(element => {
    element.textContent = strings[element.dataset.i18n];
  });
  document.querySelectorAll('[data-i18n-attr]').forEach(element => {
    const [attribute, key] = element.dataset.i18nAttr.split(':');
    element.setAttribute(attribute, strings[key]);
  });
  languageButton.textContent = strings.languageButton;
  languageButton.setAttribute('aria-label', strings.languageAria);
  if (save) {
    try { localStorage.setItem('cmc-language', locale); } catch { /* Storage is optional for local previews. */ }
  }
}

menuButton.addEventListener('click', () => {
  const isOpen = menuButton.getAttribute('aria-expanded') === 'true';
  menuButton.setAttribute('aria-expanded', String(!isOpen));
  menuButton.setAttribute('aria-label', translations[document.documentElement.lang === 'zh-CN' ? 'zh' : 'en'][isOpen ? 'menuOpen' : 'menuClose']);
  nav.classList.toggle('is-open', !isOpen);
});

nav.addEventListener('click', event => {
  if (event.target.closest('a')) {
    menuButton.setAttribute('aria-expanded', 'false');
    menuButton.setAttribute('aria-label', translations[document.documentElement.lang === 'zh-CN' ? 'zh' : 'en'].menuOpen);
    nav.classList.remove('is-open');
  }
});

languageButton.addEventListener('click', () => {
  setLanguage(document.documentElement.lang === 'zh-CN' ? 'en' : 'zh');
  const strings = translations[document.documentElement.lang === 'zh-CN' ? 'zh' : 'en'];
  const menuIsOpen = menuButton.getAttribute('aria-expanded') === 'true';
  menuButton.setAttribute('aria-label', strings[menuIsOpen ? 'menuClose' : 'menuOpen']);
});

let savedLanguage = 'en';
try { savedLanguage = localStorage.getItem('cmc-language') || 'en'; } catch { /* First view stays English when storage is unavailable. */ }
setLanguage(savedLanguage, false);
