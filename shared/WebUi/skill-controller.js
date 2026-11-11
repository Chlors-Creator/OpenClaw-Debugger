(function () {
  'use strict';

  function create(deps) {
    const controller = { ...deps, view: window.OpenClawSkillView };
    controller.load = force => load(controller, force);
    controller.render = () => controller.view.render(controller);
    controller.refresh = () => load(controller, true);
    controller.handleAction = (action, name) => handleAction(controller, action, name);
    controller.syncButtons = () => syncButtons(controller);
    controller.reset = () => reset(controller);
    controller.$('#skillFilter').addEventListener('input', controller.render);
    controller.$('#skillRefreshButton').addEventListener('click', controller.refresh);
    controller.$('#skillList').addEventListener('click', event => {
      const button = event.target.closest('[data-skill-action]');
      if (button) controller.handleAction(button.dataset.skillAction, button.dataset.skillName);
    });
    syncButtons(controller);
    return controller;
  }

  async function load(controller, force) {
    const state = controller.state;
    if (!controller.isConnected()) {
      controller.$('#skillStatus').textContent = '连接服务器后读取当前技能。';
      controller.render();
      syncButtons(controller);
      return false;
    }
    if (state.loading || (state.loaded && !force)) return false;
    const generation = ++state.loadGeneration;
    state.loadController?.abort();
    const operation = new AbortController();
    state.loadController = operation;
    state.loading = true;
    state.loadError = '';
    controller.$('#skillStatus').textContent = '正在读取远端技能清单…';
    controller.render();
    syncButtons(controller);
    try {
      const response = await controller.bridgeCall('getSkills', {}, { signal: operation.signal });
      if (generation !== state.loadGeneration) return false;
      storeInventory(controller, response);
      return !state.listFormatError;
    } catch (error) {
      state.loadError = String(error && error.message || error);
      reportError(controller, error, '读取技能清单失败：');
      return false;
    } finally {
      finishLoad(controller, generation);
    }
  }

  function storeInventory(controller, response) {
    const state = controller.state;
    const skills = controller.view.resultArray(response);
    state.listFormatError = skills === null || !skills.every(skill => skill && typeof skill === 'object' && !Array.isArray(skill));
    state.installedRaw = controller.view.resultValue(response);
    state.installed = skills || [];
    state.loaded = true;
    state.loadError = '';
    controller.render();
    const status = skills === null ? '技能清单格式无法识别' : '已读取 ' + skills.length + ' 项技能';
    controller.$('#skillStatus').textContent = status + controller.view.diagnostics(response);
  }

  function finishLoad(controller, generation) {
    const state = controller.state;
    if (generation !== state.loadGeneration) return;
    state.loading = false;
    state.loadController = null;
    controller.render();
    syncButtons(controller);
  }

  function reportError(controller, error, prefix) {
    if (error && error.name === 'AbortError') return;
    controller.$('#skillStatus').textContent = prefix + String(error && error.message || error);
    controller.reportError(error);
  }

  function handleAction(controller, action, name) {
    if (!name || !controller.isConnected() || controller.connectionState.busy) return;
    const handler = action === 'inspect' ? inspect : action === 'update' ? update : null;
    if (handler) handler(controller, name).catch(error => reportError(controller, error, '技能操作失败：'));
  }

  async function inspect(controller, name) {
    const state = controller.state;
    const generation = ++state.detailGeneration;
    state.detailController?.abort();
    const operation = new AbortController();
    state.detailController = operation;
    syncButtons(controller);
    try {
      const response = await controller.bridgeCall('inspectSkill', { skillName: name }, { signal: operation.signal });
      if (generation !== state.detailGeneration) return;
      controller.$('#skillDetailTitle').textContent = '技能详情 · ' + name;
      controller.$('#skillDetailBody').textContent = JSON.stringify(response, null, 2);
      controller.$('#skillDetailDialog').showModal();
    } finally {
      if (generation === state.detailGeneration) state.detailController = null;
      syncButtons(controller);
    }
  }

  function confirmUpdate(controller, name) {
    const dialog = controller.$('#skillUpdateDialog');
    controller.$('#skillUpdateTarget').textContent = name;
    dialog.showModal();
    return new Promise(resolve => dialog.addEventListener('close', () => {
      resolve(dialog.returnValue === 'confirm');
    }, { once: true }));
  }

  async function update(controller, name) {
    if (!await confirmUpdate(controller, name)) return;
    const state = controller.state;
    state.mutating = true;
    controller.$('#skillStatus').textContent = '正在远程更新技能：' + name;
    syncButtons(controller);
    try {
      await controller.bridgeCall('updateSkill', { skillName: name });
    } catch (error) {
      reportError(controller, error, '远程更新失败：');
      return;
    } finally {
      state.mutating = false;
      syncButtons(controller);
    }
    const refreshed = await load(controller, true);
    const message = refreshed ? '技能已远程更新：' + name : '远程更新命令已完成，但技能清单刷新失败。';
    controller.$('#skillStatus').textContent = message;
    controller.showToast(message, !refreshed);
  }

  function syncButtons(controller) {
    const state = controller.state;
    const busy = !controller.isConnected() || state.loading || state.mutating ||
      controller.connectionState.busy || Boolean(state.detailController);
    controller.$('#skillRefreshButton').disabled = busy;
    controller.$$('#skillList [data-skill-action]').forEach(button => { button.disabled = busy; });
  }

  function reset(controller) {
    const state = controller.state;
    state.loadController?.abort();
    state.detailController?.abort();
    state.loadGeneration++;
    state.detailGeneration++;
    state.installed = [];
    state.installedRaw = null;
    state.listFormatError = false;
    state.loaded = false;
    state.loading = false;
    state.mutating = false;
    state.loadError = '';
    state.loadController = null;
    state.detailController = null;
    controller.$('#skillCount').textContent = '0';
    controller.$('#skillVisibleCount').textContent = '0';
    controller.$('#skillStatus').textContent = '连接服务器后读取技能状态。';
    controller.render();
    syncButtons(controller);
  }

  window.OpenClawSkillController = { create };
})();
