(function () {
  'use strict';

  function create(deps) {
    const controller = { ...deps, view: window.OpenClawPluginView };
    controller.load = force => load(controller, force);
    controller.renderInstalled = () => controller.view.renderInstalled(controller);
    controller.installFromInput = () => installFromInputSafely(controller);
    controller.installSpec = spec => installSpecSafely(controller, spec);
    controller.canInstall = () => canInstall(controller);
    controller.handleAction = (action, id) => handleAction(controller, action, id);
    controller.syncButtons = () => syncButtons(controller);
    controller.reset = () => reset(controller);
    controller.$('#pluginPackageInput').addEventListener('input', controller.syncButtons);
    return controller;
  }

  async function load(controller, force) {
    const state = controller.state;
    if (!controller.isConnected() || state.loading || (state.loaded && !force)) return false;
    const generation = ++state.loadGeneration;
    state.loadController?.abort();
    const operation = new AbortController();
    state.loadController = operation;
    state.loading = true;
    controller.$('#pluginStatus').textContent = '正在读取远端插件清单…';
    syncButtons(controller);
    try {
      const response = await controller.bridgeCall('getPlugins', {}, { signal: operation.signal });
      if (generation !== state.loadGeneration) return false;
      storeInstalled(controller, response);
      return !state.listFormatError;
    } catch (error) {
      reportOperationError(controller, error, '读取插件清单失败：', '#pluginStatus');
      return false;
    } finally {
      finishLoad(controller, generation);
    }
  }

  function storeInstalled(controller, response) {
    const state = controller.state;
    const plugins = controller.view.resultArray(response);
    state.listFormatError = plugins === null;
    state.installedRaw = controller.view.resultValue(response);
    state.installed = plugins || [];
    state.loaded = true;
    controller.view.renderInstalled(controller);
    const status = plugins === null ? '插件清单格式无法识别' : '已读取 ' + plugins.length + ' 个插件';
    controller.$('#pluginStatus').textContent = status + controller.view.diagnostics(response);
    controller.setStatus(status + controller.view.diagnostics(response), state.listFormatError || Boolean(response.diagnostics));
  }

  function finishLoad(controller, generation) {
    const state = controller.state;
    if (generation !== state.loadGeneration) return;
    state.loading = false;
    state.loadController = null;
    controller.view.renderInstalled(controller);
    syncButtons(controller);
  }

  function reportOperationError(controller, error, prefix, target) {
    if (error && error.name === 'AbortError') return;
    controller.$(target).textContent = prefix + String(error && error.message || error);
    controller.reportError(error);
  }

  function askConfirmation(controller, title, prompt, target, consentLabel) {
    const $ = controller.$;
    $('#pluginActionTitle').textContent = title;
    $('#pluginActionPrompt').textContent = prompt;
    $('#pluginActionTarget').textContent = target;
    $('#pluginConsentLabel').textContent = consentLabel || '';
    $('#pluginConsentCheck').checked = false;
    $('#pluginConsentRow').hidden = !consentLabel;
    $('#pluginActionDialog').showModal();
    return new Promise(resolve => $('#pluginActionDialog').addEventListener('close', () => {
      resolve($('#pluginActionDialog').returnValue === 'confirm' ? $('#pluginConsentCheck').checked : null);
    }, { once: true }));
  }

  async function inspect(controller, id) {
    await runBusy(controller, async () => {
      const response = await controller.bridgeCall('inspectPlugin', { pluginId: id });
      controller.$('#pluginDetailTitle').textContent = '插件详情 · ' + id;
      controller.$('#pluginDetailBody').textContent = JSON.stringify(response, null, 2);
      controller.$('#pluginDetailDialog').showModal();
      controller.setStatus('已读取插件静态检查结果：' + id + controller.view.diagnostics(response));
    });
  }

  async function enable(controller, id) {
    const accepted = await askConfirmation(controller, '启用插件', '启用会让 OpenClaw 加载插件并注册其声明的功能。', id,
      '我接受插件声明的 capability；只有勾选后才会发送 OpenClaw 的能力授权参数。');
    if (accepted === null) return;
    await mutate(controller, 'setPluginEnabled', { pluginId: id, enabled: true, acceptCapabilities: accepted }, '已启用插件：' + id);
  }

  async function disable(controller, id) {
    const confirmed = await askConfirmation(controller, '停用插件', '停用会修改服务器上的插件配置。', id, null);
    if (confirmed === null) return;
    await mutate(controller, 'setPluginEnabled', { pluginId: id, enabled: false, acceptCapabilities: false }, '已停用插件：' + id);
  }

  async function reload(controller, id) {
    const accepted = await askConfirmation(controller, '重载插件', '重载可能让 Gateway 立即重新加载此插件。', id,
      '我接受该插件声明的新 capability；只有勾选后才会发送授权参数。');
    if (accepted === null) return;
    await mutate(controller, 'reloadPlugin', { pluginId: id, acceptCapabilities: accepted }, '已请求重载插件：' + id);
  }

  async function update(controller, id) {
    const accepted = await askConfirmation(controller, '更新插件', '更新会替换服务器上的插件代码；OpenClaw 要求先停止 Gateway。', id,
      '我接受更新版本声明的新 capability；只有勾选后才会发送授权参数。');
    if (accepted === null) return;
    await mutate(controller, 'updatePlugin', { pluginId: id, acceptCapabilities: accepted }, '已更新插件：' + id);
  }

  async function uninstall(controller, id) {
    const confirmed = await askConfirmation(controller, '卸载插件', '此操作会从服务器移除插件安装记录和受管理的文件。', id, null);
    if (confirmed === null) return;
    await mutate(controller, 'uninstallPlugin', { pluginId: id }, '已卸载插件：' + id);
  }

  function actionMap(controller) {
    return {
      inspect: id => inspect(controller, id), enable: id => enable(controller, id),
      disable: id => disable(controller, id), reload: id => reload(controller, id),
      update: id => update(controller, id), uninstall: id => uninstall(controller, id)
    };
  }

  function handleAction(controller, action, id) {
    const handler = actionMap(controller)[action];
    if (handler && id) handler(id).catch(error => reportOperationError(controller, error, '插件操作失败：', '#pluginStatus'));
  }

  async function installFromInputSafely(controller) {
    try { await installFromInput(controller); }
    catch (error) { reportOperationError(controller, error, '插件操作失败：', '#pluginStatus'); }
  }

  async function installFromInput(controller) {
    const packageSpec = controller.$('#pluginPackageInput').value.trim();
    await requestInstall(controller, packageSpec);
  }

  async function installSpecSafely(controller, packageSpec) {
    try { await requestInstall(controller, packageSpec); }
    catch (error) { reportOperationError(controller, error, '插件安装失败：', '#pluginStatus'); }
  }

  async function requestInstall(controller, packageSpec) {
    if (!packageSpec || !canInstall(controller)) return;
    const accepted = await askConfirmation(controller, '安装插件（保持禁用）',
      '将从 ClawHub 下载插件并部署到服务器。本次默认使用 --no-enable，安装后仍需检查详情并单独启用。', packageSpec,
      '我信任此来源，并接受该插件声明的 capability；只有勾选后才会发送授权参数。');
    if (accepted === null) return;
    await mutate(controller, 'installPlugin', { packageSpec, acceptCapabilities: accepted }, '已安装插件并保持禁用：' + packageSpec);
  }

  async function mutate(controller, command, payload, message) {
    const refreshed = await runBusy(controller, async () => {
      await controller.bridgeCall(command, payload);
      return await load(controller, true);
    });
    if (refreshed) {
      controller.setStatus(message);
      controller.showToast(message);
      return;
    }
    controller.setStatus(message + '；但清单刷新失败，请查看错误并手动刷新。', true);
  }

  async function runBusy(controller, operation) {
    controller.state.mutating = true;
    syncButtons(controller);
    try { return await operation(); }
    finally { controller.state.mutating = false; syncButtons(controller); }
  }

  function canInstall(controller) {
    return controller.isConnected() && !controller.state.mutating && !controller.connectionState.busy;
  }

  function syncButtons(controller) {
    const state = controller.state;
    const connected = controller.isConnected();
    const busy = state.loading || state.mutating || controller.connectionState.busy || !connected;
    controller.$('#pluginRefreshButton').disabled = busy;
    controller.$('#pluginInstallButton').disabled = busy || !controller.$('#pluginPackageInput').value.trim();
    controller.$$('#pluginList [data-plugin-action]').forEach(button => { button.disabled = busy; });
  }

  function reset(controller) {
    const state = controller.state;
    state.loadController?.abort();
    state.loadGeneration += 1;
    state.installed = [];
    state.installedRaw = null;
    state.listFormatError = false;
    state.loaded = false;
    state.loading = false;
    state.mutating = false;
    controller.$('#pluginCount').textContent = '0';
    controller.$('#pluginListCount').textContent = '0';
    controller.$('#pluginStatus').textContent = '连接服务器后读取插件状态。';
    controller.view.renderInstalled(controller);
    syncButtons(controller);
  }

  window.OpenClawPluginController = { create };
})();
