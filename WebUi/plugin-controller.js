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
    controller.updatePlugin = id => update(controller, id);
    controller.syncButtons = () => syncButtons(controller);
    controller.reset = () => reset(controller);
    controller.dependencies = window.OpenClawDependencyController.create({ ...deps, pluginController: controller });
    controller.$('#pluginPackageInput').addEventListener('input', controller.syncButtons);
    return controller;
  }

  async function load(controller, force) {
    const state = controller.state;
    if (!controller.isConnected() || state.loading || (state.loaded && !force)) return false;
    const generation = ++state.loadGeneration;
    state.loadController?.abort();
    state.categoryLookupController?.abort();
    state.categoryLookupController = null;
    const operation = new AbortController();
    state.loadController = operation;
    state.loading = true;
    state.loadError = '';
    controller.dependencies.render();
    syncButtons(controller);
    try {
      const response = await controller.bridgeCall('getPlugins', {}, { signal: operation.signal });
      if (generation !== state.loadGeneration) return false;
      const plugins = storeInstalled(controller, response);
      if (plugins) void lookupInstalledCategories(controller, plugins, generation);
      return !state.listFormatError;
    } catch (error) {
      state.loadError = String(error && error.message || error);
      reportOperationError(controller, error, '读取插件清单失败：');
      controller.dependencies.render();
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
    state.installed = (plugins || []).map(plugin => ({ ...plugin, clawHubCategoryStatus: 'pending' }));
    state.loaded = true;
    state.loadError = '';
    controller.view.renderInstalled(controller);
    controller.dependencies.render();
    if (state.listFormatError) {
      controller.setStatus('插件清单格式无法识别' + controller.view.diagnostics(response), true);
    } else if (response.diagnostics) {
      controller.setStatus('插件清单诊断：' + String(response.diagnostics), true);
    }
    return plugins;
  }

  async function lookupInstalledCategories(controller, plugins, generation) {
    const state = controller.state;
    if (!plugins.length) return;
    const operation = new AbortController();
    state.categoryLookupController = operation;
    controller.$('#pluginStatus').textContent = `已读取 ${plugins.length} 个插件，正在逐个查询 ClawHub 分类…`;
    controller.view.renderInstalled(controller);
    try {
      const payload = { plugins: plugins.map(plugin => ({
        id: pluginRecordId(plugin),
        name: plugin.name || plugin.displayName || plugin.title || '',
        packageName: plugin.packageName || plugin.installRef || '',
        source: plugin.source || ''
      })) };
      const response = await controller.bridgeCall('lookupInstalledPluginCategories', payload, { signal: operation.signal });
      if (generation !== state.loadGeneration) return;
      const lookups = controller.view.resultArray(response);
      if (!lookups) throw new Error('ClawHub 分类查询返回了无法识别的 JSON 结构。');
      applyCategoryLookups(state, lookups);
      const counts = categoryLookupCounts(lookups, plugins.length);
      controller.$('#pluginStatus').textContent = `已读取 ${plugins.length} 个插件；ClawHub 分类匹配 ${counts.matched} 个，未收录 ${counts.notFound} 个，查询失败 ${counts.error} 个。`;
    } catch (error) {
      if (error && error.name === 'AbortError') return;
      if (generation !== state.loadGeneration) return;
      applyCategoryLookupError(state, error);
      controller.$('#pluginStatus').textContent = '插件清单已读取，但 ClawHub 分类查询失败：' + String(error && error.message || error);
      controller.reportError(error);
    } finally {
      if (generation === state.loadGeneration) {
        state.categoryLookupController = null;
        controller.view.renderInstalled(controller);
      }
    }
  }

  function applyCategoryLookups(state, lookups) {
    const byId = new Map(lookups.map(item => [String(item.id || '').toLowerCase(), item]));
    state.installed = state.installed.map(plugin => {
      const result = byId.get(pluginRecordId(plugin).toLowerCase());
      if (!result) return { ...plugin, clawHubCategoryStatus: 'error', clawHubCategoryError: 'ClawHub 没有返回此插件的查询结果。' };
      const categories = result.categories || [];
      const hasCategories = window.OpenClawPluginCategoryUtils.values({ clawHubCategories: categories }).length > 0;
      const status = result.status === 'matched' ? (hasCategories ? 'matched' : 'missing') : result.status;
      return { ...plugin, clawHubCategories: categories, clawHubCategoryStatus: status, clawHubCategoryError: result.error || '' };
    });
  }

  function applyCategoryLookupError(state, error) {
    const message = String(error && error.message || error);
    state.installed = state.installed.map(plugin => ({
      ...plugin, clawHubCategoryStatus: 'error', clawHubCategoryError: message
    }));
  }

  function categoryLookupCounts(lookups, requestedCount) {
    const counts = lookups.reduce((summary, item) => {
      const key = item.status === 'matched' ? 'matched' : item.status === 'notFound' ? 'notFound' : 'error';
      summary[key] += 1;
      return summary;
    }, { matched: 0, notFound: 0, error: 0 });
    counts.error += Math.max(0, requestedCount - lookups.length);
    return counts;
  }

  function finishLoad(controller, generation) {
    const state = controller.state;
    if (generation !== state.loadGeneration) return;
    state.loading = false;
    state.loadController = null;
    controller.view.renderInstalled(controller);
    syncButtons(controller);
  }

  function pluginRecordId(plugin) {
    return String(plugin.id || plugin.pluginId || plugin.name || '');
  }

  function reportOperationError(controller, error, prefix) {
    if (error && error.name === 'AbortError') return;
    const message = prefix + String(error && error.message || error);
    controller.setStatus(message, true);
    controller.showToast(message, true);
  }

  function askConfirmation(controller, options) {
    const $ = controller.$;
    $('#pluginActionTitle').textContent = options.title;
    $('#pluginActionPrompt').textContent = options.prompt;
    $('#pluginActionTarget').textContent = options.target;
    $('#pluginConsentLabel').textContent = options.consentLabel || '';
    $('#pluginConsentCheck').checked = false;
    $('#pluginConsentRow').hidden = !options.consentLabel;
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
      const inspectionStatus = controller.$('#pluginInspectionStatus');
      inspectionStatus.textContent = '已读取插件静态检查结果：' + id + controller.view.diagnostics(response);
      inspectionStatus.hidden = false;
    });
  }

  async function enable(controller, id) {
    const accepted = await askConfirmation(controller, {
      title: '启用插件', prompt: '启用会让 OpenClaw 加载插件并注册其声明的功能。', target: id,
      consentLabel: '我接受插件声明的 capability；只有勾选后才会发送 OpenClaw 的能力授权参数。'
    });
    if (accepted === null) return;
    await mutate(controller, { command: 'setPluginEnabled', payload: { pluginId: id, enabled: true, acceptCapabilities: accepted }, message: '已启用插件：' + id });
  }

  async function disable(controller, id) {
    const confirmed = await askConfirmation(controller, { title: '停用插件', prompt: '停用会修改服务器上的插件配置。', target: id });
    if (confirmed === null) return;
    await mutate(controller, { command: 'setPluginEnabled', payload: { pluginId: id, enabled: false, acceptCapabilities: false }, message: '已停用插件：' + id });
  }

  async function reload(controller, id) {
    const accepted = await askConfirmation(controller, {
      title: '重载插件', prompt: '重载可能让 Gateway 立即重新加载此插件。', target: id,
      consentLabel: '我接受该插件声明的新 capability；只有勾选后才会发送授权参数。'
    });
    if (accepted === null) return;
    await mutate(controller, { command: 'reloadPlugin', payload: { pluginId: id, acceptCapabilities: accepted }, message: '已请求重载插件：' + id });
  }

  async function update(controller, id) {
    const accepted = await askConfirmation(controller, {
      title: '更新插件', prompt: '更新会替换服务器上的插件代码，并按包清单更新其运行依赖；OpenClaw 要求先停止 Gateway。', target: id,
      consentLabel: '我接受更新版本声明的新 capability；只有勾选后才会发送授权参数。'
    });
    if (accepted === null) return false;
    await mutate(controller, { command: 'updatePlugin', payload: { pluginId: id, acceptCapabilities: accepted }, message: '已更新插件：' + id });
    return true;
  }

  async function uninstall(controller, id) {
    const confirmed = await askConfirmation(controller, { title: '卸载插件', prompt: '此操作会从服务器移除插件安装记录和受管理的文件。', target: id });
    if (confirmed === null) return;
    await mutate(controller, { command: 'uninstallPlugin', payload: { pluginId: id }, message: '已卸载插件：' + id });
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
    if (handler && id) handler(id).catch(error => reportOperationError(controller, error, '插件操作失败：'));
  }

  async function installFromInputSafely(controller) {
    try { await installFromInput(controller); }
    catch (error) { reportOperationError(controller, error, '插件操作失败：'); }
  }

  async function installFromInput(controller) {
    const packageSpec = controller.$('#pluginPackageInput').value.trim();
    await requestInstall(controller, packageSpec);
  }

  async function installSpecSafely(controller, packageSpec) {
    try { await requestInstall(controller, packageSpec); }
    catch (error) { reportOperationError(controller, error, '插件安装失败：'); }
  }

  async function requestInstall(controller, packageSpec) {
    if (!packageSpec || !canInstall(controller)) return;
    const accepted = await askConfirmation(controller, {
      title: '安装插件（保持禁用）',
      prompt: '将从 ClawHub 下载插件并部署到服务器。本次默认使用 --no-enable，安装后仍需检查详情并单独启用。',
      target: packageSpec,
      consentLabel: '我信任此来源，并接受该插件声明的 capability；只有勾选后才会发送授权参数。'
    });
    if (accepted === null) return;
    await mutate(controller, { command: 'installPlugin', payload: { packageSpec, acceptCapabilities: accepted }, message: '已安装插件并保持禁用：' + packageSpec });
  }

  async function mutate(controller, options) {
    const refreshed = await runBusy(controller, async () => {
      await controller.bridgeCall(options.command, options.payload);
      return await load(controller, true);
    });
    if (refreshed) {
      controller.setStatus(options.message);
      controller.showToast(options.message);
      return true;
    }
    controller.setStatus(options.message + '；但清单刷新失败，请查看错误并手动刷新。', true);
    return true;
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
    controller.dependencies.syncButtons();
  }

  function reset(controller) {
    const state = controller.state;
    state.loadController?.abort();
    state.categoryLookupController?.abort();
    state.loadGeneration += 1;
    state.installed = [];
    state.installedRaw = null;
    state.listFormatError = false;
    state.loaded = false;
    state.loading = false;
    state.categoryLookupController = null;
    state.mutating = false;
    state.loadError = '';
    controller.$('#pluginCount').textContent = '0';
    controller.$('#pluginListCount').textContent = '0';
    controller.view.renderInstalled(controller);
    controller.dependencies.render();
    syncButtons(controller);
  }

  window.OpenClawPluginController = { create };
})();
