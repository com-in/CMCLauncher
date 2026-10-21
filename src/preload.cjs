const { contextBridge, ipcRenderer } = require('electron');
const methods = ['removeContent', 'worlds', 'listContent', 'importContent', 'toggleContent', 'openContentFolder', 'state', 'versions', 'logs', 'createInstance', 'importLocal', 'installPack', 'search', 'installMod', 'listMods', 'importMods', 'toggleMod', 'addOffline', 'login', 'cancelLogin', 'selectAccount', 'removeAccount', 'settings', 'pickJava', 'checkJava', 'launch', 'openFolder', 'openLogin'];
contextBridge.exposeInMainWorld('craft', {
  ...Object.fromEntries(methods.map(method => [method, async input => {
    const result = await ipcRenderer.invoke(`craft:${method}`, input);
    if (!result.ok) throw new Error(result.error);
    return result.data;
  }])),
  onEvent: callback => {
    const listener = (_event, payload) => callback(payload);
    ipcRenderer.on('craft:event', listener);
    return () => ipcRenderer.removeListener('craft:event', listener);
  }
});
