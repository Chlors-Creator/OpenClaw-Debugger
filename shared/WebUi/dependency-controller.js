(function () {
  'use strict';

  function create(deps) {
    const controller = { ...deps, view: window.OpenClawDependencyView, visibleDependencies: [], detailBusy: false };
    controller.render = () => render(controller);
    controller.load = () => load(controller, false);
    controller.refresh = () => load(controller, true);
    controller.syncButtons = () => syncButtons(controller);
    controller.isBusy = () => isBusy(controller);
    controller.handleAction = (action, index) => handleAction(controller, action, index);
    controller.$('#dependencyFilter').addEventListener('input', controller.render);
    controller.$('#dependencyRefreshButton').addEventListener('click', controller.refresh);
    controller.$('#dependencyList').addEventListener('click', event => {
      const button = event.target.closest('[data-dependency-action]');
      if (button) controller.handleAction(button.dataset.dependencyAction, Number(button.dataset.dependencyIndex));
    });
    syncButtons(controller);
    return controller;
  }

  async function load(controller, force) {
    if (!controller.isConnected()) {
      controller.$('#dependencyStatus').textContent = '连接服务器后读取当前依赖。';
      render(controller);
      return false;
    }
    const loaded = await controller.pluginController.load(force);
    render(controller);
    return loaded;
  }

  function render(controller) {
    const state = controller.pluginController.state;
    const status = controller.$('#dependencyStatus');
    const count = controller.$('#dependencyCount');
    if (!state.loaded) {
      count.textContent = '0';
      status.textContent = state.loadError ? '读取远端依赖清单失败：' + state.loadError :
        state.loading ? '正在读取远端插件依赖清单…' : '连接服务器后读取当前依赖。';
      controller.view.render(controller, [], '连接服务器后读取插件依赖清单');
      syncButtons(controller);
      return;
    }
    if (state.listFormatError) {
      count.textContent = '—';
      controller.$('#dependencyVisibleCount').textContent = '—';
      controller.visibleDependencies = [];
      status.textContent = '插件清单格式无法识别，不能枚举依赖。';
      controller.view.renderRaw(controller, state.installedRaw, '远端插件清单中没有可识别的 plugins 数组。');
      syncButtons(controller);
      return;
    }
    renderCollected(controller, controller.view.collect(state.installed), state.loadError);
  }

  function renderCollected(controller, result, loadError) {
    const missing = result.items.filter(item => !item.installed && !item.optional).length;
    const optionalMissing = result.items.filter(item => !item.installed && item.optional).length;
    const statusParts = [result.items.length + ' 项依赖', missing + ' 项必需依赖缺失', optionalMissing + ' 项可选依赖缺失'];
    if (result.unreported.length) statusParts.push('未返回依赖状态：' + result.unreported.join(', '));
    if (result.invalid.length) statusParts.push('依赖记录格式异常：' + result.invalid.join('；'));
    if (loadError) statusParts.push('清单刷新失败，显示上次结果：' + loadError);
    controller.$('#dependencyCount').textContent = String(result.items.length);
    controller.$('#dependencyStatus').textContent = statusParts.join(' · ');
    controller.view.render(controller, result.items, emptyMessage(controller, result));
    syncButtons(controller);
  }

  function emptyMessage(controller, result) {
    const query = controller.$('#dependencyFilter').value.trim();
    if (query) return '没有匹配的当前依赖。';
    if (result.invalid.length) return '依赖状态格式异常，详情见上方状态。';
    if (result.items.length) return '没有匹配的当前依赖。';
    if (result.unreported.length) return '远端未返回依赖状态，无法确认所有插件的依赖。';
    if (controller.pluginController.state.installed.length === 0) return '当前没有已安装插件，也没有插件依赖。';
    return '当前插件没有声明需要单独管理的运行依赖。';
  }

  function isBusy(controller) {
    const pluginState = controller.pluginController.state;
    return !controller.isConnected() || controller.connectionState.busy || pluginState.loading ||
      pluginState.mutating || controller.detailBusy;
  }

  function syncButtons(controller) {
    const busy = isBusy(controller);
    controller.$('#dependencyRefreshButton').disabled = busy;
    controller.$$('#dependencyList [data-dependency-action]').forEach(button => { button.disabled = busy; });
  }

  function handleAction(controller, action, index) {
    const dependency = controller.visibleDependencies[index];
    if (!dependency || isBusy(controller)) return;
    if (action === 'inspect') inspect(controller, dependency).catch(error => reportDetailError(controller, error));
    if (action === 'update') updateOwner(controller, dependency).catch(error => reportDetailError(controller, error));
  }

  async function inspect(controller, dependency) {
    controller.detailBusy = true;
    syncButtons(controller);
    try {
      const inspection = await controller.bridgeCall('inspectPlugin', { pluginId: dependency.ownerId });
      showDependencyDetails(controller, dependency, inspection);
    } finally {
      controller.detailBusy = false;
      syncButtons(controller);
    }
  }

  function showDependencyDetails(controller, dependency, inspection) {
    const details = {
      dependency: {
        name: dependency.name,
        spec: dependency.spec,
        installed: dependency.installed,
        optional: dependency.optional,
        resolvedPath: dependency.resolvedPath || null
      },
      owningPlugin: { id: dependency.ownerId, name: dependency.ownerName, version: dependency.ownerVersion || null },
      owningPluginInspection: inspection
    };
    controller.$('#dependencyDetailTitle').textContent = '依赖详情 · ' + dependency.name;
    controller.$('#dependencyDetailBody').textContent = JSON.stringify(details, null, 2);
    controller.$('#dependencyDetailDialog').showModal();
  }

  async function updateOwner(controller, dependency) {
    controller.$('#dependencyStatus').textContent = '等待确认更新所属插件及依赖：' + dependency.ownerId;
    const updated = await controller.pluginController.updatePlugin(dependency.ownerId);
    if (!updated) render(controller);
  }

  function reportDetailError(controller, error) {
    if (error && error.name === 'AbortError') return;
    controller.$('#dependencyStatus').textContent = '依赖操作失败：' + String(error && error.message || error);
    controller.reportError(error);
  }

  window.OpenClawDependencyController = { create };
})();
