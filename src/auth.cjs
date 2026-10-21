const { setTimeout: delay } = require('node:timers/promises');
const LOGIN = 'https://login.microsoftonline.com/consumers/oauth2/v2.0';

class MicrosoftAuth {
  constructor(fetcher, emit = () => {}) { this.fetch = fetcher; this.emit = emit; }

  async request(url, payload, form = false, signal) {
    const response = await this.fetch(url, {
      method: 'POST', headers: { 'Content-Type': form ? 'application/x-www-form-urlencoded' : 'application/json', Accept: 'application/json' },
      body: form ? new URLSearchParams(payload).toString() : JSON.stringify(payload), signal: signal || AbortSignal.timeout(30000)
    });
    const result = await response.json();
    if (!response.ok) {
      const error = new Error(result.error_description || result.errorMessage || `账号服务请求失败 (${response.status}${result.XErr ? ` / ${result.XErr}` : ''})`);
      error.code = result.error;
      throw error;
    }
    return result;
  }

  async login(clientId, signal) {
    if (!/^[a-f0-9-]{36}$/i.test(clientId)) throw new Error('请先在设置中填写你注册并获得 Minecraft API 授权的 Microsoft 应用 Client ID');
    const code = await this.request(`${LOGIN}/devicecode`, { client_id: clientId, scope: 'XboxLive.signin offline_access' }, true, signal);
    this.emit('auth-code', { code: code.user_code, url: code.verification_uri, expiresIn: code.expires_in });
    const expires = Date.now() + code.expires_in * 1000;
    let interval = Math.max(code.interval || 5, 5);
    while (Date.now() < expires) {
      await delay(interval * 1000, undefined, { signal });
      try {
        const tokens = await this.request(`${LOGIN}/token`, { client_id: clientId, grant_type: 'urn:ietf:params:oauth:grant-type:device_code', device_code: code.device_code }, true, signal);
        return await this.exchange(tokens, signal);
      } catch (e) {
        if (e.code === 'authorization_pending') continue;
        if (e.code === 'slow_down') { interval += 5; continue; }
        throw e;
      }
    }
    throw new Error('登录代码已过期，请重新登录');
  }

  async refresh(clientId, refreshToken, signal) {
    const tokens = await this.request(`${LOGIN}/token`, { client_id: clientId, grant_type: 'refresh_token', refresh_token: refreshToken, scope: 'XboxLive.signin offline_access' }, true, signal);
    return this.exchange({ ...tokens, refresh_token: tokens.refresh_token || refreshToken }, signal);
  }

  async exchange(tokens, signal) {
    const xbox = await this.request('https://user.auth.xboxlive.com/user/authenticate', {
      Properties: { AuthMethod: 'RPS', SiteName: 'user.auth.xboxlive.com', RpsTicket: `d=${tokens.access_token}` }, RelyingParty: 'http://auth.xboxlive.com', TokenType: 'JWT'
    }, false, signal);
    const xsts = await this.request('https://xsts.auth.xboxlive.com/xsts/authorize', {
      Properties: { SandboxId: 'RETAIL', UserTokens: [xbox.Token] }, RelyingParty: 'rp://api.minecraftservices.com/', TokenType: 'JWT'
    }, false, signal);
    const uhs = xsts.DisplayClaims.xui[0].uhs;
    const minecraft = await this.request('https://api.minecraftservices.com/authentication/login_with_xbox', { identityToken: `XBL3.0 x=${uhs};${xsts.Token}` }, false, signal);
    const response = await this.fetch('https://api.minecraftservices.com/minecraft/profile', { headers: { Authorization: `Bearer ${minecraft.access_token}` }, signal: signal || AbortSignal.timeout(30000) });
    if (!response.ok) throw new Error('无法获取 Minecraft Java 版档案，请确认此账号已购买游戏、创建角色，且应用已获 Minecraft API 授权');
    const profile = await response.json();
    return { type: 'microsoft', uuid: profile.id, name: profile.name, xuid: xsts.DisplayClaims.xui[0].xid || '',
      accessToken: minecraft.access_token, refreshToken: tokens.refresh_token, expiresAt: Date.now() + minecraft.expires_in * 1000 };
  }
}

module.exports = { MicrosoftAuth };
