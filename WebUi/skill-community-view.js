(function () {
  'use strict';
  const ARRAY_KEYS = ['items', 'results', 'skills'];

  function resultValue(response) {
    return response && response.result !== undefined ? response.result : response;
  }

  function records(response) {
    const value = resultValue(response);
    if (Array.isArray(value)) return value;
    for (const key of ARRAY_KEYS) if (value && Array.isArray(value[key])) return value[key];
    return null;
  }

  function nextCursor(response) {
    const value = resultValue(response);
    return String(value && (value.nextCursor || value.cursor) || '');
  }

  function readPath(source, path) {
    return path.split('.').reduce((value, key) => value && value[key], source);
  }

  function first(item, paths) {
    const sources = [item, item && item.skill, item && item.catalog].filter(Boolean);
    for (const path of paths) {
      for (const source of sources) {
        const value = readPath(source, path);
        if (value !== undefined && value !== null && value !== '') return value;
      }
    }
    return undefined;
  }

  function scalar(value) {
    if (typeof value === 'string' || typeof value === 'number') return String(value);
    if (value && typeof value === 'object') return String(value.handle || value.displayName || value.name || '');
    return '';
  }

  function slug(item) { return scalar(first(item, ['slug', 'skill.slug', 'id', 'name'])); }
  function title(item) { return scalar(first(item, ['displayName', 'name', 'title', 'skill.displayName'])) || slug(item) || '未命名技能'; }
  function version(item) { return scalar(first(item, ['latestVersion.version', 'latestVersion', 'version', 'skill.latestVersion.version'])); }
  function summary(item) { return scalar(first(item, ['summary', 'description', 'blurb', 'skill.summary'])); }
  function owner(item) { return scalar(first(item, ['owner.handle', 'owner.name', 'owner', 'author.handle', 'author'])); }
  function stat(item, key) {
    const value = first(item, ['stats.' + key, key, 'skill.stats.' + key]);
    return typeof value === 'number' && Number.isFinite(value) ? value.toLocaleString() : '—';
  }

  function element(tag, className, text) {
    const node = document.createElement(tag);
    node.className = className || '';
    node.textContent = text;
    return node;
  }

  function action(label, type, name, disabled) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = type === 'install' ? 'button button-primary' : 'button button-quiet';
    button.dataset.skillCommunityAction = type;
    button.dataset.skillSlug = name;
    button.textContent = label;
    button.disabled = disabled;
    return button;
  }

  function isInstalled(controller, item) {
    const name = slug(item).toLowerCase();
    return controller.skillController.state.installed.some(skill =>
      controller.skillController.view.skillName(skill).toLowerCase() === name);
  }

  function renderCard(controller, item, busy) {
    const name = slug(item);
    const card = element('article', 'plugin-search-card skill-community-card', '');
    const heading = element('div', 'plugin-card-heading', '');
    heading.append(element('strong', '', title(item)), element('span', 'plugin-status-tag', version(item) ? 'v' + version(item) : 'ClawHub'));
    card.append(heading, element('code', 'plugin-id', name || '缺少技能标识'));
    const publisher = owner(item);
    const details = [publisher && '发布者 ' + publisher, '★ ' + stat(item, 'stars'), '↓ ' + stat(item, 'downloads')].filter(Boolean);
    card.append(element('small', 'plugin-meta', details.join(' · ')));
    const description = summary(item);
    if (description) card.append(element('p', 'plugin-description', description));
    const actions = element('div', 'plugin-community-actions', '');
    if (name) actions.append(action('详情', 'detail', name, busy),
      action(isInstalled(controller, item) ? '已安装' : '安装到服务器', 'install', name, busy || isInstalled(controller, item)));
    card.append(actions);
    return card;
  }

  function renderResults(controller) {
    const state = controller.state;
    const list = controller.$('#skillCommunityResults');
    controller.$('#skillCommunityCount').textContent = String(state.items.length);
    list.replaceChildren();
    list.classList.toggle('empty-state', state.items.length === 0);
    if (!state.items.length) {
      list.textContent = state.loading ? '正在读取 ClawHub 技能目录…' : state.loaded ? '没有找到匹配的技能。' : '技能社区结果会显示在这里';
    } else {
      state.items.forEach(item => list.append(renderCard(controller, item, state.loading || Boolean(state.installingSlug) ||
        controller.connectionState.busy || !controller.isConnected())));
    }
    const more = controller.$('#skillCommunityLoadMoreButton');
    more.hidden = !state.nextCursor || Boolean(state.query);
    more.disabled = state.loading || !state.nextCursor;
  }

  function renderDetails(controller, response) {
    const selected = controller.state.selectedItem || {};
    const value = resultValue(response) || selected;
    const detail = value.skill && typeof value.skill === 'object'
      ? { ...value.skill, owner: value.owner || value.skill.owner, moderation: value.moderation || value.skill.moderation }
      : value;
    const titleText = scalar(first(detail, ['displayName', 'name', 'title'])) || title(selected);
    controller.$('#skillCommunityDetailTitle').textContent = titleText;
    controller.$('#skillCommunityDetailIntroduction').textContent = scalar(first(detail, ['summary', 'description'])) || summary(selected) || '此技能没有提供简介。';
    const rows = [
      ['技能标识', scalar(first(detail, ['slug', 'name'])) || slug(selected)],
      ['发布者', owner(detail) || owner(selected)],
      ['版本', scalar(first(detail, ['latestVersion.version', 'latestVersion', 'version'])) || version(selected)],
      ['安全状态', scalar(first(detail, ['moderation.status', 'moderation', 'scan.status', 'latestVersion.scan.status'])) || '未提供']
    ].filter(row => row[1]);
    const metadata = controller.$('#skillCommunityDetailMetadata');
    metadata.replaceChildren(...rows.map(([label, valueText]) => {
      const row = element('div', 'plugin-community-detail-row', '');
      row.append(element('small', '', label), element('span', '', valueText));
      return row;
    }));
    controller.$('#skillCommunityDetailStats').textContent = '★ ' + stat(detail, 'stars') + '　↓ ' + stat(detail, 'downloads');
    controller.$('#skillCommunityDetailBody').textContent = JSON.stringify(response || selected, null, 2);
    const installButton = controller.$('#skillCommunityDetailInstallButton');
    const installed = isInstalled(controller, selected);
    installButton.textContent = installed ? '已安装' : '安装到服务器';
    installButton.disabled = !slug(selected) || installed || !controller.isConnected() ||
      controller.connectionState.busy || Boolean(controller.state.installingSlug);
  }

  window.OpenClawSkillCommunityView = { resultValue, records, nextCursor, slug, title, summary, renderResults, renderDetails };
})();
