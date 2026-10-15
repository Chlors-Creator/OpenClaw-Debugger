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

  function actionButton(options) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'button button-quiet plugin-action-button';
    button.dataset.pluginAction = options.action;
    button.dataset.pluginId = options.id;
    button.textContent = options.label;
    button.disabled = options.disabled;
    return button;
  }

  function renderRawList(controller, value, message) {
    const list = controller.$('#pluginList');
    list.replaceChildren(textElement('p', 'plugin-format-error', message));
    list.appendChild(textElement('pre', 'plugin-json plugin-raw-list', JSON.stringify(value, null, 2)));
    list.classList.remove('empty-state');
  }

  function renderActions(plugin, id, busy) {
    const actions = document.createElement('div');
    actions.className = 'plugin-card-actions';
    if (!id) return actions;
    const isEnabled = enabled(plugin);
    actions.appendChild(actionButton({ label: '详情', action: 'inspect', id, disabled: busy }));
    actions.appendChild(actionButton({ label: isEnabled ? '停用' : '启用', action: isEnabled ? 'disable' : 'enable', id, disabled: busy }));
    actions.appendChild(actionButton({ label: '重载', action: 'reload', id, disabled: busy }));
    actions.appendChild(actionButton({ label: '更新', action: 'update', id, disabled: busy }));
    actions.appendChild(actionButton({ label: '卸载', action: 'uninstall', id, disabled: busy }));
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
    card.appendChild(renderActions(plugin, id, busy));
    return card;
  }

  function renderInstalled(controller) {
    const state = controller.state;
    const list = controller.$('#pluginList');
    if (state.listFormatError) {
      window.OpenClawPluginCategoryUtils.updateFilter(controller.$('#pluginCategoryFilter'), []);
      controller.$('#pluginCount').textContent = '—';
      controller.$('#pluginListCount').textContent = '—';
      renderRawList(controller, state.installedRaw, '插件清单 JSON 中没有可识别的 plugins 数组。原始结果：');
      return;
    }
    const categoryFilter = controller.$('#pluginCategoryFilter');
    window.OpenClawPluginCategoryUtils.updateFilter(categoryFilter, state.installed);
    const selectedCategory = categoryFilter.value;
    const query = controller.$('#pluginFilter').value.trim().toLowerCase();
    const visible = state.installed.filter(plugin => window.OpenClawPluginCategoryUtils.matches(plugin, selectedCategory) &&
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
    const busy = state.loading || state.mutating || controller.connectionState.busy || !controller.isConnected();
    for (const plugin of visible) list.appendChild(renderPluginCard(plugin, busy));
  }

  window.OpenClawPluginView = { resultValue, resultArray, diagnostics, renderInstalled };
})();
