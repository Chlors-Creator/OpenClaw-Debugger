(function () {
  'use strict';
  const BYTES_PER_KIB = 1024;
  const BYTES_PER_MIB = BYTES_PER_KIB * BYTES_PER_KIB;
  const MAXIMUM_ARCHIVE_BYTES = 100 * BYTES_PER_MIB;

  function create(deps) {
    const controller = { ...deps, view: window.OpenClawCustomArtifactView };
    controller.render = () => controller.view.render(controller);
    controller.load = force => load(controller, force);
    controller.upload = () => upload(controller);
    controller.handleAction = (action, id) => handleAction(controller, action, id);
    controller.syncButtons = () => syncButtons(controller);
    controller.$('#customArtifactRefreshButton').addEventListener('click', () => controller.load(true));
    controller.$('#customArtifactUploadButton').addEventListener('click', controller.upload);
    controller.$('#customArtifactFile').addEventListener('change', () => syncButtons(controller));
    controller.$('#customPluginSearch').addEventListener('input', controller.render);
    controller.$('#customSkillSearch').addEventListener('input', controller.render);
    ['#customPluginList', '#customSkillList'].forEach(selector => {
      controller.$(selector).addEventListener('click', event => {
        const button = event.target.closest('[data-custom-artifact-action]');
        if (button) controller.handleAction(button.dataset.customArtifactAction, button.dataset.customArtifactId);
      });
    });
    syncButtons(controller);
    return controller;
  }

  async function load(controller, force) {
    const state = controller.state;
    if (state.loading || (state.loaded && !force)) return false;
    state.loading = true;
    controller.$('#customArtifactStatus').textContent = '正在读取本机自定义列表…';
    controller.render();
    syncButtons(controller);
    try {
      const result = await controller.bridgeCall('getCustomArtifacts', {});
      if (!Array.isArray(result.plugins) || !Array.isArray(result.skills))
        throw new Error('自定义列表数据格式无效。');
      state.plugins = result.plugins;
      state.skills = result.skills;
      state.loaded = true;
      controller.$('#customArtifactStatus').textContent = '已读取 ' + state.plugins.length + ' 个自制插件和 ' + state.skills.length + ' 项自制技能。';
      return true;
    } catch (error) {
      controller.$('#customArtifactStatus').textContent = '读取自定义列表失败：' + error.message;
      controller.reportError(error);
      return false;
    } finally {
      state.loading = false;
      controller.render();
      syncButtons(controller);
    }
  }

  async function upload(controller) {
    const state = controller.state;
    const fileInput = controller.$('#customArtifactFile');
    const file = fileInput.files && fileInput.files[0];
    if (!file || state.uploading || !isValidArchive(file)) return;
    if (typeof window.chrome?.webview?.postMessageWithAdditionalObjects !== 'function') {
      controller.$('#customArtifactStatus').textContent = '当前 WebView 不支持安全文件传输。';
      return;
    }
    state.uploading = true;
    controller.$('#customArtifactStatus').textContent = '正在上传并读取 ZIP 元数据…';
    controller.$('#customArtifactSelectedFile').textContent = file.name + ' · ' + fileSize(file.size);
    syncButtons(controller);
    try {
      const type = controller.$('#customArtifactType').value;
      const artifact = await controller.bridgeCall('uploadCustomArtifact', {
        type, fileName: file.name, size: file.size
      }, { additionalObjects: [file] });
      const list = type === 'plugin' ? state.plugins : state.skills;
      list.unshift(artifact);
      fileInput.value = '';
      controller.$('#customArtifactSelectedFile').textContent = '尚未选择文件';
      controller.$('#customArtifactStatus').textContent = '已加入自定义列表：' + artifact.name;
      controller.showToast('自制项目已保存到本机自定义库。');
    } catch (error) {
      controller.$('#customArtifactStatus').textContent = '上传失败：' + error.message;
      controller.reportError(error);
    } finally {
      state.uploading = false;
      controller.render();
      syncButtons(controller);
    }
  }

  function handleAction(controller, action, id) {
    const artifact = [...controller.state.plugins, ...controller.state.skills].find(item => item.id === id);
    if (!artifact || isBusy(controller)) return;
    if (action === 'inspect') inspect(controller, artifact);
    if (action === 'delete') remove(controller, artifact).catch(error => {
      controller.$('#customArtifactStatus').textContent = '移除失败：' + error.message;
      controller.reportError(error);
    });
  }

  function inspect(controller, artifact) {
    controller.$('#customArtifactDetailTitle').textContent = (artifact.type === 'plugin' ? '自制插件 · ' : '自制技能 · ') + artifact.name;
    controller.$('#customArtifactDetailBody').textContent = JSON.stringify(artifact, null, 2);
    controller.$('#customArtifactDetailDialog').showModal();
  }

  async function remove(controller, artifact) {
    if (!window.confirm('从自定义列表移除“' + artifact.name + '”？对应的本机 ZIP 包也会删除。')) return;
    const state = controller.state;
    state.deletingId = artifact.id;
    controller.$('#customArtifactStatus').textContent = '正在移除：' + artifact.name;
    syncButtons(controller);
    try {
      await controller.bridgeCall('deleteCustomArtifact', { id: artifact.id });
      state.plugins = state.plugins.filter(item => item.id !== artifact.id);
      state.skills = state.skills.filter(item => item.id !== artifact.id);
      controller.$('#customArtifactStatus').textContent = '已从本机自定义列表移除：' + artifact.name;
      controller.showToast('自定义项目已移除。');
    } finally {
      state.deletingId = '';
      controller.render();
      syncButtons(controller);
    }
  }

  function isBusy(controller) {
    const state = controller.state;
    return state.loading || state.uploading || Boolean(state.deletingId);
  }

  function syncButtons(controller) {
    const fileInput = controller.$('#customArtifactFile');
    const file = fileInput.files && fileInput.files[0];
    const busy = isBusy(controller);
    controller.$('#customArtifactRefreshButton').disabled = busy;
    controller.$('#customArtifactType').disabled = busy;
    fileInput.disabled = busy;
    controller.$('#customArtifactUploadButton').disabled = busy || !file || !isValidArchive(file);
    if (file && !busy && !isValidArchive(file))
      controller.$('#customArtifactStatus').textContent = archiveError(file);
    controller.$('#customArtifactSelectedFile').textContent = file ? file.name + ' · ' + fileSize(file.size) : '尚未选择文件';
    controller.$$('#customPluginList [data-custom-artifact-action], #customSkillList [data-custom-artifact-action]')
      .forEach(button => { button.disabled = busy; });
  }

  function fileSize(size) {
    return size < BYTES_PER_MIB ? (size / BYTES_PER_KIB).toFixed(1) + ' KiB' : (size / BYTES_PER_MIB).toFixed(1) + ' MiB';
  }

  function isValidArchive(file) {
    return file.name.toLowerCase().endsWith('.zip') && file.size > 0 && file.size <= MAXIMUM_ARCHIVE_BYTES;
  }

  function archiveError(file) {
    if (!file.name.toLowerCase().endsWith('.zip')) return '仅支持 ZIP 格式的插件包或技能包。';
    if (file.size === 0) return '所选 ZIP 包为空。';
    return 'ZIP 包超过 100 MiB。';
  }

  window.OpenClawCustomArtifactController = { create };
})();
