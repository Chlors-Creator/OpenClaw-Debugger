(function () {
  'use strict';

  function pluginId(plugin) {
    const value = plugin && (plugin.id || plugin.pluginId || plugin.name);
    return typeof value === 'string' ? value : '';
  }

  function normalizeEntry(plugin, entry, optional) {
    const ownerId = pluginId(plugin);
    if (!ownerId || !entry || typeof entry !== 'object' ||
        typeof entry.name !== 'string' || typeof entry.spec !== 'string' ||
        typeof entry.installed !== 'boolean') return null;
    return {
      ownerId,
      ownerName: String(plugin.name || plugin.displayName || ownerId),
      ownerVersion: typeof plugin.version === 'string' ? plugin.version : '',
      name: entry.name,
      spec: entry.spec,
      installed: entry.installed,
      optional: optional || entry.optional === true,
      resolvedPath: typeof entry.resolvedPath === 'string' ? entry.resolvedPath : ''
    };
  }

  function collect(plugins) {
    const items = [];
    const unreported = [];
    const invalid = [];
    let reported = 0;
    for (const plugin of plugins) {
      const ownerId = pluginId(plugin) || '未命名插件';
      const status = plugin && plugin.dependencyStatus;
      if (!status || typeof status !== 'object') {
        unreported.push(ownerId);
        continue;
      }
      if (status.hasDependencies === false && !status.dependencies && !status.optionalDependencies) {
        reported++;
        continue;
      }
      if (!Array.isArray(status.dependencies) || !Array.isArray(status.optionalDependencies)) {
        invalid.push(ownerId + '：依赖状态缺少 dependencies 或 optionalDependencies 数组');
        continue;
      }
      reported++;
      appendEntries(plugin, status.dependencies, false, items, invalid);
      appendEntries(plugin, status.optionalDependencies, true, items, invalid);
    }
    return { items, unreported, invalid, reported };
  }

  function appendEntries(plugin, entries, optional, items, invalid) {
    entries.forEach((entry, index) => {
      const normalized = normalizeEntry(plugin, entry, optional);
      if (normalized) items.push(normalized);
      else invalid.push(pluginId(plugin) + '：第 ' + (index + 1) + ' 条依赖记录格式无效');
    });
  }

  function textElement(tag, className, text) {
    const element = document.createElement(tag);
    element.className = className;
    element.textContent = text;
    return element;
  }

  function actionButton(label, action, index, disabled) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'button button-quiet plugin-action-button';
    button.dataset.dependencyAction = action;
    button.dataset.dependencyIndex = String(index);
    button.textContent = label;
    button.disabled = disabled;
    return button;
  }

  function renderCard(entry, index, disabled) {
    const card = document.createElement('article');
    card.className = 'plugin-card dependency-card' + (!entry.installed ? ' dependency-missing' : '');
    const heading = document.createElement('div');
    heading.className = 'plugin-card-heading';
    const status = entry.installed ? '已安装' : entry.optional ? '可选缺失' : '缺失';
    heading.append(textElement('strong', '', entry.name), textElement('span', 'plugin-status-tag', status));
    card.append(heading, textElement('code', 'plugin-id dependency-spec', entry.spec || '未指定版本范围'));
    const kind = entry.optional ? '可选依赖' : '必需依赖';
    const owner = '所属插件：' + entry.ownerName + ' (' + entry.ownerId + ')';
    card.appendChild(textElement('small', 'plugin-meta', kind + ' · ' + owner + (entry.ownerVersion ? ' · 插件版本 ' + entry.ownerVersion : '')));
    if (entry.resolvedPath) card.appendChild(textElement('small', 'dependency-path', '安装路径：' + entry.resolvedPath));
    const actions = document.createElement('div');
    actions.className = 'plugin-card-actions';
    actions.append(actionButton('详情', 'inspect', index, disabled), actionButton('更新所属插件', 'update', index, disabled));
    card.appendChild(actions);
    return card;
  }

  function render(controller, entries, emptyMessage) {
    const list = controller.$('#dependencyList');
    const query = controller.$('#dependencyFilter').value.trim().toLowerCase();
    const visible = entries.filter(entry => [entry.name, entry.spec, entry.ownerId, entry.ownerName]
      .some(value => value.toLowerCase().includes(query)));
    controller.visibleDependencies = visible;
    list.replaceChildren();
    list.classList.toggle('empty-state', visible.length === 0);
    controller.$('#dependencyVisibleCount').textContent = String(visible.length);
    if (!visible.length) {
      list.textContent = emptyMessage;
      return;
    }
    const disabled = controller.isBusy();
    visible.forEach((entry, index) => list.appendChild(renderCard(entry, index, disabled)));
  }

  function renderRaw(controller, value, message) {
    const list = controller.$('#dependencyList');
    list.replaceChildren(textElement('p', 'plugin-format-error', message));
    list.appendChild(textElement('pre', 'plugin-json plugin-raw-list', JSON.stringify(value, null, 2)));
    list.classList.remove('empty-state');
  }

  window.OpenClawDependencyView = { collect, render, renderRaw };
})();
