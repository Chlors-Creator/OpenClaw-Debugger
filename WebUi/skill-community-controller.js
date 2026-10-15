(function () {
  'use strict';
  const SORT_MODES = new Set(['recommended', 'downloads', 'updated', 'createdAt', 'stars', 'trending', 'name']);

  function create(deps) {
    const controller = { ...deps, view: window.OpenClawSkillCommunityView };
    controller.browse = forceRefresh => loadCatalog(controller, { forceRefresh: Boolean(forceRefresh) });
    controller.search = () => search(controller);
    controller.refresh = () => refresh(controller);
    controller.loadMore = () => loadMore(controller);
    controller.syncButtons = () => syncButtons(controller);
    controller.install = item => install(controller, item);
    controller.$('#skillCommunitySearchForm').addEventListener('submit', event => { event.preventDefault(); controller.search(); });
    controller.$('#skillCommunitySearchInput').addEventListener('input', controller.syncButtons);
    controller.$('#skillCommunitySort').addEventListener('change', event => setSort(controller, event.target.value));
    controller.$('#skillCommunityRefreshButton').addEventListener('click', controller.refresh);
    controller.$('#skillCommunityLoadMoreButton').addEventListener('click', controller.loadMore);
    controller.$('#skillCommunityResults').addEventListener('click', event => handleResultAction(controller, event));
    controller.$('#skillCommunityDetailInstallButton').addEventListener('click', () => install(controller, controller.state.selectedItem));
    controller.$('#skillCommunityDetailDialog').addEventListener('close', () => closeDetails(controller));
    controller.$('#skillCommunityInstallForm').addEventListener('submit', event => {
      event.preventDefault(); controller.$('#skillCommunityInstallDialog').close('confirm');
    });
    controller.$('#skillCommunityInstallCancelButton').addEventListener('click', () => controller.$('#skillCommunityInstallDialog').close('cancel'));
    controller.$('#skillCommunityInstallCloseButton').addEventListener('click', () => controller.$('#skillCommunityInstallDialog').close('cancel'));
    controller.view.renderResults(controller);
    syncButtons(controller);
    return controller;
  }

  function setSort(controller, sort) {
    if (!SORT_MODES.has(sort)) throw new Error('不支持的技能社区排序方式：' + sort);
    controller.state.sort = sort;
    controller.$('#skillCommunitySearchInput').value = '';
    loadCatalog(controller, { forceRefresh: true });
  }

  function search(controller) {
    const query = controller.$('#skillCommunitySearchInput').value.trim();
    return loadCatalog(controller, query ? { query, forceRefresh: true } : { forceRefresh: false });
  }

  function refresh(controller) {
    const query = controller.state.query;
    return loadCatalog(controller, query ? { query, forceRefresh: true } : { forceRefresh: true });
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
    state.error = '';
    if (!options.append) {
      state.items = [];
      state.nextCursor = '';
      state.query = options.query || '';
    }
    controller.$('#skillCommunitySearchStatus').textContent = state.query ? '正在搜索 ClawHub 技能…' : '正在读取 ClawHub 技能目录…';
    controller.view.renderResults(controller);
    syncButtons(controller);
    try {
      const response = await fetchCatalog(controller, options, operation.signal);
      if (generation !== state.generation) return;
      storeCatalog(controller, response, options.append);
    } catch (error) {
      if (generation === state.generation) showCatalogError(controller, error);
    } finally {
      finishCatalog(controller, generation);
    }
  }

  function fetchCatalog(controller, options, signal) {
    if (options.query) return controller.bridgeCall('searchSkillCommunity', {
      query: options.query, forceRefresh: Boolean(options.forceRefresh)
    }, { signal });
    return controller.bridgeCall('browseSkillCommunity', {
      sort: controller.state.sort, cursor: options.cursor || '', forceRefresh: Boolean(options.forceRefresh)
    }, { signal });
  }

  function storeCatalog(controller, response, append) {
    const state = controller.state;
    const records = controller.view.records(response);
    if (records === null) throw new TypeError('ClawHub 技能响应中没有可识别的技能列表。');
    state.items = append ? state.items.concat(records) : records;
    state.nextCursor = controller.view.nextCursor(response);
    state.loaded = true;
    const action = state.query ? '搜索到 ' : '已加载 ';
    controller.$('#skillCommunitySearchStatus').textContent = action + records.length + ' 项技能' + (response.diagnostics ? ' · ' + response.diagnostics : '');
    controller.view.renderResults(controller);
  }

  function showCatalogError(controller, error) {
    if (error && error.name === 'AbortError') return;
    controller.state.error = String(error && error.message || error);
    controller.$('#skillCommunitySearchStatus').textContent = 'ClawHub 技能目录读取失败：' + controller.state.error;
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

  function handleResultAction(controller, event) {
    const button = event.target.closest('[data-skill-community-action]');
    if (!button) return;
    const item = controller.state.items.find(candidate => controller.view.slug(candidate) === button.dataset.skillSlug);
    if (!item) return;
    if (button.dataset.skillCommunityAction === 'detail') openDetails(controller, item);
    if (button.dataset.skillCommunityAction === 'install') install(controller, item);
  }

  async function openDetails(controller, item) {
    const state = controller.state;
    state.selectedItem = item;
    state.detail = item;
    state.detailLoading = true;
    controller.view.renderDetails(controller, item);
    controller.$('#skillCommunityDetailStatus').textContent = '正在读取 ClawHub 技能详情…';
    controller.$('#skillCommunityDetailDialog').showModal();
    const generation = ++state.detailGeneration;
    state.detailController?.abort();
    const operation = new AbortController();
    state.detailController = operation;
    try {
      const response = await controller.bridgeCall('getSkillCommunityDetails', { slug: controller.view.slug(item), forceRefresh: false }, { signal: operation.signal });
      if (generation !== state.detailGeneration) return;
      state.detail = response;
      controller.view.renderDetails(controller, response);
      controller.$('#skillCommunityDetailStatus').textContent = '已读取 ClawHub 发布信息。';
    } catch (error) {
      if (error && error.name === 'AbortError') return;
      controller.$('#skillCommunityDetailStatus').textContent = '详情读取失败：' + String(error && error.message || error);
    } finally {
      if (generation === state.detailGeneration) {
        state.detailLoading = false;
        state.detailController = null;
        syncButtons(controller);
      }
    }
  }

  async function install(controller, item) {
    const slug = item && controller.view.slug(item);
    if (!slug || !controller.isConnected() || controller.connectionState.busy || controller.state.installingSlug) return;
    controller.$('#skillCommunityInstallTarget').textContent = slug;
    controller.$('#skillCommunityInstallDialog').showModal();
    const dialog = controller.$('#skillCommunityInstallDialog');
    const confirmed = await new Promise(resolve => dialog.addEventListener('close', () => resolve(dialog.returnValue === 'confirm'), { once: true }));
    if (!confirmed) return;
    await performInstall(controller, slug);
  }

  function closeDetails(controller) {
    const state = controller.state;
    state.detailGeneration++;
    state.detailController?.abort();
    state.detailController = null;
    state.detailLoading = false;
    syncButtons(controller);
  }

  async function performInstall(controller, slug) {
    const state = controller.state;
    state.installingSlug = slug;
    controller.$('#skillCommunitySearchStatus').textContent = '正在服务器安装技能：' + slug;
    syncButtons(controller);
    try {
      await controller.bridgeCall('installCommunitySkill', { skillName: slug });
      const refreshed = await controller.skillController.load(true);
      const message = refreshed ? '技能已安装并加入当前技能清单：' + slug : '安装命令已完成，但当前技能清单刷新失败。';
      controller.$('#skillCommunitySearchStatus').textContent = message;
      controller.showToast(message, !refreshed);
    } catch (error) {
      controller.$('#skillCommunitySearchStatus').textContent = '技能安装失败：' + String(error && error.message || error);
      controller.reportError(error);
    } finally {
      state.installingSlug = '';
      controller.view.renderResults(controller);
      syncButtons(controller);
    }
  }

  function syncButtons(controller) {
    const state = controller.state;
    const busy = state.loading || Boolean(state.installingSlug) || controller.connectionState.busy;
    controller.$('#skillCommunityRefreshButton').disabled = state.loading;
    controller.$('#skillCommunitySort').disabled = state.loading;
    controller.$('#skillCommunitySearchButton').disabled = state.loading || !controller.$('#skillCommunitySearchInput').value.trim();
    controller.$$('#skillCommunityResults [data-skill-community-action="install"]').forEach(button => {
      button.disabled = busy || !controller.isConnected();
    });
    controller.$('#skillCommunityDetailInstallButton').disabled = busy || !controller.isConnected() || !state.selectedItem;
  }

  window.OpenClawSkillCommunityController = { create };
})();
