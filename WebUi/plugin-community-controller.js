(function () {
  'use strict';

  function create(deps) {
    const controller = { ...deps, view: window.OpenClawPluginCommunityView };
    controller.browse = forceRefresh => loadCatalog(controller, { forceRefresh: Boolean(forceRefresh) });
    controller.search = () => search(controller);
    controller.refresh = () => refresh(controller);
    controller.loadMore = () => loadMore(controller);
    controller.setSort = value => setSort(controller, value);
    controller.render = () => controller.view.renderResults(controller);
    controller.handleAction = (action, index) => handleAction(controller, action, index);
    controller.checkSecurity = item => checkSecurity(controller, item);
    controller.syncButtons = () => syncButtons(controller);
    controller.canInstall = () => controller.pluginController.canInstall();
    controller.installSpec = item => controller.view.installSpec(item);
    controller.$('#pluginSearchInput').addEventListener('input', controller.syncButtons);
    controller.$('#pluginCommunitySort').addEventListener('change', event => controller.setSort(event.target.value));
    controller.view.renderResults(controller);
    syncButtons(controller);
    return controller;
  }

  function setSort(controller, sort) {
    controller.state.sort = sort;
    controller.$('#pluginSearchInput').value = '';
    controller.syncButtons();
    loadCatalog(controller, { forceRefresh: true });
  }

  function search(controller) {
    const query = controller.$('#pluginSearchInput').value.trim();
    if (!query) return controller.browse();
    return loadCatalog(controller, { query, forceRefresh: true });
  }

  function refresh(controller) {
    const query = controller.state.query;
    return query
      ? loadCatalog(controller, { query, forceRefresh: true })
      : controller.browse(true);
  }

  function loadMore(controller) {
    const state = controller.state;
    if (!state.nextCursor || state.query) return Promise.resolve();
    return loadCatalog(controller, { cursor: state.nextCursor, append: true });
  }

  async function loadCatalog(controller, options) {
    const state = controller.state;
    const generation = ++state.generation;
    state.requestController?.abort();
    const operation = new AbortController();
    state.requestController = operation;
    state.loading = true;
    state.catalogFormatError = false;
    state.catalogError = '';
    if (!options.append) {
      state.items = [];
      state.nextCursor = '';
      state.query = options.query || '';
    }
    const status = state.query ? '正在搜索 ClawHub…' : '正在读取 ClawHub 插件目录…';
    controller.$('#pluginSearchStatus').textContent = status;
    controller.view.renderResults(controller);
    syncButtons(controller);
    try {
      const response = await fetchCatalog(controller, options, operation.signal);
      if (generation !== state.generation) return;
      storeCatalog(controller, response, options.append);
    } catch (error) {
      showCatalogError(controller, error);
    } finally {
      finishCatalog(controller, generation);
    }
  }

  function fetchCatalog(controller, options, signal) {
    const state = controller.state;
    if (options.query) {
      return controller.bridgeCall('searchPluginCommunity', {
        query: options.query,
        forceRefresh: Boolean(options.forceRefresh)
      }, { signal });
    }
    return controller.bridgeCall('browsePluginCommunity', {
      sort: state.sort,
      cursor: options.cursor || '',
      forceRefresh: Boolean(options.forceRefresh)
    }, { signal });
  }

  function storeCatalog(controller, response, append) {
    const state = controller.state;
    const items = controller.view.records(response);
    state.catalogRaw = controller.view.resultValue(response);
    state.catalogFormatError = items === null;
    state.catalogError = '';
    if (items === null) {
      state.loaded = true;
      controller.$('#pluginSearchStatus').textContent = 'ClawHub 返回的插件列表格式无法识别。';
      return;
    }
    state.loaded = true;
    state.items = append ? state.items.concat(items) : items;
    state.nextCursor = controller.view.nextCursor(response);
    const label = state.query ? '搜索到 ' : '已加载 ';
    controller.$('#pluginSearchStatus').textContent = label + items.length + ' 个插件' + (response.diagnostics ? ' · ' + response.diagnostics : '');
    controller.view.renderResults(controller);
  }

  function showCatalogError(controller, error) {
    if (error && error.name === 'AbortError') return;
    controller.state.catalogError = String(error && error.message || error);
    controller.$('#pluginSearchStatus').textContent = 'ClawHub 目录请求失败：' + String(error && error.message || error);
    controller.reportError(error);
  }

  function finishCatalog(controller, generation) {
    const state = controller.state;
    if (generation !== state.generation) return;
    state.loading = false;
    state.requestController = null;
    controller.view.renderResults(controller);
    syncButtons(controller);
  }

  function handleAction(controller, action, index) {
    const item = controller.state.items[index];
    if (!item) return;
    if (action === 'detail') openDetails(controller, item);
    if (action === 'security') {
      openDetails(controller, item);
      controller.checkSecurity(item);
    }
    if (action === 'install') controller.pluginController.installSpec(controller.view.installSpec(item));
  }

  function openDetails(controller, item) {
    const state = controller.state;
    state.securityController?.abort();
    state.securityGeneration += 1;
    state.securityController = null;
    state.securityLoading = false;
    controller.state.selectedItem = item;
    controller.view.renderDetails(controller, item);
    controller.$('#pluginCommunityDetailDialog').showModal();
  }

  async function checkSecurity(controller, item) {
    const state = controller.state;
    const packageName = controller.view.packageName(item);
    const version = controller.view.version(item);
    if (!packageName || !version) {
      controller.view.setSecurity(controller, null, '插件包名或版本信息缺失，无法查询版本报告。');
      return;
    }
    const generation = ++state.securityGeneration;
    state.securityController?.abort();
    const operation = new AbortController();
    state.securityController = operation;
    state.securityLoading = true;
    controller.$('#pluginCommunitySecurityStatus').textContent = '正在读取 ClawHub 已发布的安全报告…';
    controller.view.renderResults(controller);
    syncButtons(controller);
    try {
      const result = await controller.bridgeCall('checkPluginCommunitySecurity', {
        packageName,
        version,
        forceRefresh: true
      }, { signal: operation.signal });
      if (generation !== state.securityGeneration) return;
      state.securityResult = result;
      state.securityError = '';
      controller.view.setSecurity(controller, result, '');
    } catch (error) {
      if (error && error.name === 'AbortError') return;
      state.securityError = String(error && error.message || error);
      controller.view.setSecurity(controller, null, state.securityError);
    } finally {
      finishSecurity(controller, generation);
    }
  }

  function finishSecurity(controller, generation) {
    const state = controller.state;
    if (generation !== state.securityGeneration) return;
    state.securityLoading = false;
    state.securityController = null;
    controller.view.renderResults(controller);
    syncButtons(controller);
  }

  function syncButtons(controller) {
    const state = controller.state;
    const busy = state.loading || controller.connectionState.busy;
    controller.$('#pluginCommunityRefreshButton').disabled = state.loading;
    controller.$('#pluginCommunitySort').disabled = state.loading;
    controller.$('#pluginSearchButton').disabled = state.loading || !controller.$('#pluginSearchInput').value.trim();
    controller.$$('#pluginSearchResults [data-community-action="install"]').forEach(button => {
      button.disabled = busy || !controller.canInstall();
    });
    controller.$$('#pluginSearchResults [data-community-action="security"]').forEach(button => {
      const item = state.items[Number(button.dataset.communityIndex)];
      button.disabled = state.securityLoading || !controller.view.packageName(item) || !controller.view.version(item);
    });
    const selected = state.selectedItem;
    controller.$('#pluginCommunityDetailInstallButton').disabled = busy || !selected ||
      !controller.canInstall() || !controller.installSpec(selected);
    controller.$('#pluginCommunitySecurityButton').disabled = state.securityLoading || !selected ||
      !controller.view.packageName(selected) || !controller.view.version(selected);
  }

  window.OpenClawPluginCommunityController = { create };
})();
