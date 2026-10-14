(function () {
  'use strict';
  const ARRAY_KEYS = ['plugins', 'results', 'items'];

  function resultValue(response) { return response && response.result; }

  function resultArray(response) {
    const value = resultValue(response);
    if (Array.isArray(value)) return value;
    for (const key of ARRAY_KEYS) if (value && Array.isArray(value[key])) return value[key];
    return null;
  }

  function diagnostics(response) {
    return response && response.diagnostics ? ' · 诊断：' + String(response.diagnostics) : '';
  }

  function displayName(plugin) {
    return String(plugin.name || plugin.title || plugin.displayName || plugin.id || '未命名插件');
  }

  function pluginId(plugin) {
    const value = plugin.id || plugin.pluginId || plugin.name;
    return value === undefined || value === null ? '' : String(value);
  }

  function enabled(plugin) {
    return plugin.enabled === true || String(plugin.status || '').toLowerCase() === 'enabled';
  }

  function textElement(tag, className, text) {
    const element = document.createElement(tag);
    element.className = className;
    element.textContent = text;
    return element;
  }

  function actionButton(label, action, id, disabled) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'button button-quiet plugin-action-button';
    button.dataset.pluginAction = action;
    button.dataset.pluginId = id;
    button.textContent = label;
    button.disabled = disabled;
    return button;
  }

  function renderRawList(controller, value, message) {
    const list = controller.$('#pluginList');
    list.replaceChildren(textElement('p', 'plugin-format-error', message));
    list.appendChild(textElement('pre', 'plugin-json plugin-raw-list', JSON.stringify(value, null, 2)));
    list.classList.remove('empty-state');
  }

  function renderPluginActions(plugin, id, busy) {
    const actions = document.createElement('div');
    actions.className = 'plugin-card-actions';
    if (!id) return actions;
    const isEnabled = enabled(plugin);
    actions.appendChild(actionButton('详情', 'inspect', id, busy));
    actions.appendChild(actionButton(isEnabled ? '停用' : '启用', isEnabled ? 'disable' : 'enable', id, busy));
    actions.appendChild(actionButton('重载', 'reload', id, busy));
    actions.appendChild(actionButton('更新', 'update', id, busy));
    actions.appendChild(actionButton('卸载', 'uninstall', id, busy));
    return actions;
  }

  function renderPluginCard(plugin, busy) {
    const id = pluginId(plugin);
    const card = document.createElement('article');
    card.className = 'plugin-card';
    const heading = document.createElement('div');
    heading.className = 'plugin-card-heading';
    heading.append(textElement('strong', '', displayName(plugin)), textElement('span', 'plugin-status-tag', String(plugin.status || (enabled(plugin) ? 'enabled' : 'disabled'))));
    card.append(heading, textElement('code', 'plugin-id', id || '缺少插件 ID'));
    const details = [plugin.version, plugin.source, plugin.dependencyStatus].filter(value => value !== undefined && value !== null && value !== '');
    if (details.length) card.appendChild(textElement('small', 'plugin-meta', details.map(String).join(' · ')));
    if (plugin.description) card.appendChild(textElement('p', 'plugin-description', String(plugin.description)));
    card.appendChild(renderPluginActions(plugin, id, busy));
    return card;
  }

  function renderInstalled(controller) {
    const state = controller.state;
    const list = controller.$('#pluginList');
    if (state.listFormatError) {
      controller.$('#pluginCount').textContent = '—';
      controller.$('#pluginListCount').textContent = '—';
      renderRawList(controller, state.installedRaw, '插件清单 JSON 中没有可识别的 plugins 数组。原始结果：');
      return;
    }
    const query = controller.$('#pluginFilter').value.trim().toLowerCase();
    const visible = state.installed.filter(plugin =>
      [displayName(plugin), pluginId(plugin), plugin.source, plugin.status, plugin.version]
        .some(value => String(value || '').toLowerCase().includes(query)));
    controller.$('#pluginCount').textContent = String(state.installed.length);
    controller.$('#pluginListCount').textContent = String(visible.length);
    list.replaceChildren();
    list.classList.toggle('empty-state', visible.length === 0);
    if (!visible.length) {
      list.textContent = state.loaded ? '没有匹配的已安装插件。' : '连接服务器后读取插件清单';
      return;
    }
    const busy = state.loading || state.searching || state.mutating || controller.connectionState.busy || !controller.isConnected();
    for (const plugin of visible) list.appendChild(renderPluginCard(plugin, busy));
  }

  function searchSpec(item) {
    const explicit = item.installSpec || item.packageSpec;
    if (typeof explicit === 'string' && explicit) return explicit;
    const id = item.id || item.slug;
    return typeof id === 'string' && id ? 'clawhub:' + id : '';
  }

  function renderInstallSpecButton(controller, spec) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'button button-quiet';
    button.dataset.pluginSpec = spec;
    button.textContent = '填入安装来源';
    button.disabled = controller.state.searching || controller.state.mutating || controller.connectionState.busy;
    return button;
  }

  function renderSearchCard(controller, item) {
    const card = document.createElement('article');
    card.className = 'plugin-search-card';
    const title = item.name || item.title || item.id || item.slug || '搜索结果';
    const id = item.id || item.slug || '';
    card.appendChild(textElement('strong', '', String(title)));
    if (id) card.appendChild(textElement('code', 'plugin-id', String(id)));
    if (item.description) card.appendChild(textElement('p', 'plugin-description', String(item.description)));
    const spec = searchSpec(item);
    if (spec) card.appendChild(renderInstallSpecButton(controller, spec));
    return card;
  }

  function renderRawSearchResult(container, result) {
    container.classList.remove('empty-state');
    container.append(textElement('p', 'plugin-format-error', '搜索返回了非列表 JSON，原始结果如下：'));
    container.appendChild(textElement('pre', 'plugin-json plugin-raw-list', JSON.stringify(result, null, 2)));
  }

  function renderSearchResults(controller) {
    const container = controller.$('#pluginSearchResults');
    const response = controller.state.searchResponse;
    const items = response ? resultArray(response) : null;
    container.replaceChildren();
    container.classList.toggle('empty-state', !items || items.length === 0);
    if (!response) { container.textContent = '搜索结果会显示在这里'; return; }
    if (!items) { renderRawSearchResult(container, resultValue(response)); return; }
    if (!items.length) { container.textContent = '没有找到匹配的插件。'; return; }
    for (const item of items) container.appendChild(renderSearchCard(controller, item));
  }

  window.OpenClawPluginView = { resultValue, resultArray, diagnostics, renderInstalled, renderSearchResults };
})();
