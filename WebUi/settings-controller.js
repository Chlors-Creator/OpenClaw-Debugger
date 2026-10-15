(function () {
  'use strict';
  function create(deps) {
    const { $, bridgeCall, state, setStatus, showToast, reportError, connectServer, setBusy } = deps;
    function getConnectionSettings() {
      return {
        host: $('#hostInput').value.trim(),
        username: $('#usernameInput').value.trim(),
        port: Number($('#portInput').value),
        workspacePath: $('#workspaceInput').value.trim(),
        stickersPath: $('#stickersInput').value.trim(),
        backupRetentionCount: Math.max(1, Math.min(30, Number($('#backupRetentionInput').value) || 5))
      };
    }
    function fillSettings(value, retentionOverride) {
      state.value = value || state.value;
      $('#hostInput').value = state.value.host || '';
      $('#usernameInput').value = state.value.username || '';
      $('#portInput').value = state.value.port || 22;
      $('#backupRetentionInput').value = retentionOverride || state.value.backupRetentionCount || 5;
      $('#workspaceInput').value = state.value.workspacePath || '/home/user/.openclaw/workspace';
      $('#stickersInput').value = state.value.stickersPath || '/home/user/.openclaw/workspace/stickers';
      $('#targetSummary').textContent = state.value.username && state.value.host
        ? state.value.username + '@' + state.value.host
        : '未配置 SSH 目标';
    }
    async function saveConnectionSettings(andConnect) {
      try {
        const result = await bridgeCall('saveSettings', getConnectionSettings());
        fillSettings(result.settings.connection, result.settings.backupRetentionCount);
        $('#privatePath').textContent = result.privateDirectory;
        $('#backupPath').textContent = result.backupDirectory;
        $('#targetSummary').textContent = state.value.username + '@' + state.value.host;
        setStatus('设置已保存到仓库外的私密目录。');
        if (andConnect) await connectServer(); else showToast('连接设置已保存。');
      } catch (error) { reportError(error); }
    }
    function getFirewallSettings() {
      return {
        regionId: $('#firewallRegionInput').value.trim(),
        instanceId: $('#firewallInstanceInput').value.trim(),
        autoSyncOnStartup: $('#firewallAutoSyncInput').checked,
        accessKeyId: $('#firewallAccessKeyIdInput').value.trim(),
        accessKeySecret: $('#firewallAccessKeySecretInput').value
      };
    }
    function fillFirewallSettings(value, credentialsConfigured) {
      state.firewall = value || state.firewall;
      state.firewallCredentialsConfigured = Boolean(credentialsConfigured);
      $('#firewallRegionInput').value = state.firewall.regionId || '';
      $('#firewallInstanceInput').value = state.firewall.instanceId || '';
      $('#firewallAutoSyncInput').checked = Boolean(state.firewall.autoSyncOnStartup);
      $('#firewallAccessKeyIdInput').value = '';
      $('#firewallAccessKeySecretInput').value = '';
      $('#firewallCredentialStatus').textContent = state.firewallCredentialsConfigured
        ? '阿里云密钥对已保存在 Windows 凭据管理器。'
        : '尚未配置阿里云凭据。';
    }
    async function saveFirewallSettings() {
      setBusy(true);
      try {
        const result = await bridgeCall('saveFirewallSettings', getFirewallSettings());
        fillFirewallSettings(result.settings, result.aliyunCredentialsConfigured);
        setStatus('阿里云地域、实例和密钥配置已保存到本机。');
        showToast('阿里云配置已保存。');
      } catch (error) { reportError(error); }
      finally { setBusy(false); }
    }
    async function syncSshWhitelist() {
      setBusy(true);
      setStatus('正在检测本机公网 IPv4 并同步服务器 SSH 白名单…');
      try {
        const result = await bridgeCall('syncSshWhitelist', {}, { timeoutMs: 300000 });
        const summary = `已同步：出口 IPv4 ${result.egressIpv4}/32；固定 IPv4 ${result.permanentIpv4}/32；规则新增 ${result.rulesCreated}、更新 ${result.rulesUpdated}、清理 ${result.rulesRemoved}。`;
        $('#firewallSyncResult').textContent = summary;
        setStatus(summary);
        showToast('SSH 白名单已同步。');
        return true;
      } catch (error) {
        reportError(error);
        $('#firewallSyncResult').textContent = '同步失败：' + (error && error.message ? error.message : String(error));
        return false;
      } finally { setBusy(false); }
    }
    return {
      getConnectionSettings, fillSettings, saveConnectionSettings,
      fillFirewallSettings, saveFirewallSettings, syncSshWhitelist
    };
  }
  window.OpenClawSettingsController = { create };
})();
