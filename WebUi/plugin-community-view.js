(function () {
  'use strict';
  const ARRAY_KEYS = ['plugins', 'items', 'results', 'packages'];

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

  function sources(item) {
    return [item, item && item.catalog, item && item.plugin, item && item.package]
      .filter(value => value && typeof value === 'object');
  }

  function readPath(source, path) {
    return path.split('.').reduce((value, key) => value && value[key], source);
  }

  function firstValue(item, paths) {
    const candidates = sources(item);
    for (const path of paths) {
      for (const source of candidates) {
        const value = readPath(source, path);
        if (value !== undefined && value !== null && value !== '') return value;
      }
    }
    return undefined;
  }

  function scalar(value) {
    if (typeof value === 'string' || typeof value === 'number') return String(value);
    if (value && typeof value === 'object') return String(value.handle || value.name || value.label || '');
    return '';
  }

  function packageName(item) {
    const explicit = scalar(firstValue(item, ['packageName', 'installRef', 'package.name', 'name', 'slug']));
    if (explicit) return explicit;
    const id = scalar(firstValue(item, ['id']));
    return id.startsWith('ch_') ? '' : id;
  }

  function displayName(item) {
    return scalar(firstValue(item, ['displayName', 'title', 'name'])) || packageName(item) || '未命名插件';
  }

  function version(item) {
    return scalar(firstValue(item, ['latestVersion.version', 'latestVersion', 'version', 'selectedRelease.version', 'latest.version']));
  }

  function description(item) {
    return scalar(firstValue(item, ['description', 'summary', 'blurb', 'catalog.summary']));
  }

  function count(item, keys) {
    const value = firstValue(item, keys);
    return typeof value === 'number' && Number.isFinite(value) ? value.toLocaleString() : '—';
  }

  function textElement(tag, className, text) {
    const element = document.createElement(tag);
    element.className = className || '';
    element.textContent = text;
    return element;
  }

  function actionButton(label, action, index, disabled) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'button button-quiet';
    button.dataset.communityAction = action;
    button.dataset.communityIndex = String(index);
    button.textContent = label;
    button.disabled = disabled;
    return button;
  }

  function appendActions(controller, card, item, index) {
    const actions = document.createElement('div');
    actions.className = 'plugin-community-actions';
    actions.appendChild(actionButton('详情', 'detail', index, false));
    actions.appendChild(actionButton('安全报告', 'security', index, controller.state.securityLoading));
    actions.appendChild(actionButton('下载并部署', 'install', index, !controller.canInstall()));
    card.appendChild(actions);
  }

  function renderCard(controller, item, index) {
    const name = packageName(item);
    const card = document.createElement('article');
    card.className = 'plugin-search-card plugin-community-card';
    const header = document.createElement('div');
    header.className = 'plugin-card-heading';
    header.appendChild(textElement('strong', '', displayName(item)));
    const badge = textElement('span', 'plugin-status-tag', firstValue(item, ['isOfficial', 'official']) === true ? '官方标记' : '社区发布');
    header.appendChild(badge);
    card.appendChild(header);
    card.appendChild(textElement('code', 'plugin-id', name || '未提供安装包名'));
    card.appendChild(textElement('small', 'plugin-meta', metaLine(item)));
    card.appendChild(textElement('p', 'plugin-description', description(item) || 'ClawHub 暂未提供插件简介。'));
    card.appendChild(textElement('div', 'plugin-community-metrics', '★ ' + count(item, ['statsStars', 'stats.stars', 'stars']) + '　↓ ' + count(item, ['statsDownloads', 'stats.downloads', 'downloads'])));
    appendActions(controller, card, item, index);
    return card;
  }

  function metaLine(item) {
    const values = [
      firstValue(item, ['owner.handle', 'publisher.handle', 'ownerHandle', 'publisherName']),
      version(item),
      firstValue(item, ['category', 'categories.0']),
      firstValue(item, ['family'])
    ].map(scalar).filter(Boolean);
    return values.length ? values.join(' · ') : 'ClawHub 插件';
  }

  function renderResults(controller) {
    const container = controller.$('#pluginSearchResults');
    container.replaceChildren();
    const items = controller.state.items;
    controller.$('#pluginCommunityCount').textContent = String(items.length);
    container.classList.toggle('empty-state', items.length === 0 && !controller.state.catalogFormatError);
    if (controller.state.catalogFormatError) {
      container.replaceChildren(textElement('p', 'plugin-format-error', 'ClawHub 返回的 JSON 中没有可识别的插件列表。原始结果：'));
      container.appendChild(textElement('pre', 'plugin-json plugin-raw-list', JSON.stringify(controller.state.catalogRaw, null, 2)));
      return;
    }
    if (!items.length) {
      container.textContent = controller.state.loading ? '正在读取 ClawHub 插件…' : '没有插件结果。';
    } else {
      items.forEach((item, index) => container.appendChild(renderCard(controller, item, index)));
    }
    const more = controller.$('#pluginCommunityLoadMoreButton');
    more.hidden = !controller.state.nextCursor || Boolean(controller.state.query);
    more.disabled = controller.state.loading || !controller.state.nextCursor;
  }

  function metadataRows(item) {
    const official = firstValue(item, ['isOfficial', 'official']);
    const rows = [
      ['包名', packageName(item)],
      ['发布者', scalar(firstValue(item, ['owner.handle', 'publisher.handle', 'ownerHandle', 'publisherName']))],
      ['版本', version(item)],
      ['类型', scalar(firstValue(item, ['family']))],
      ['分类', scalar(firstValue(item, ['category', 'categories.0']))],
      ['许可证', scalar(firstValue(item, ['license', 'licenseId']))],
      ['来源仓库', scalar(firstValue(item, ['sourceRepo', 'repository', 'source.repository']))],
      ['ClawHub 星标', count(item, ['statsStars', 'stats.stars', 'stars'])],
      ['ClawHub 下载', count(item, ['statsDownloads', 'stats.downloads', 'downloads'])],
      ['官方标记', official === true ? '是' : official === false ? '否' : '未提供']
    ];
    return rows.filter(([, value]) => value !== '');
  }

  function renderDetails(controller, item) {
    controller.$('#pluginCommunityDetailTitle').textContent = displayName(item);
    controller.$('#pluginCommunityDetailIntroduction').textContent = description(item) || 'ClawHub 暂未提供插件简介。';
    const metadata = controller.$('#pluginCommunityDetailMetadata');
    metadata.replaceChildren();
    for (const [label, value] of metadataRows(item)) {
      const row = document.createElement('div');
      row.className = 'plugin-community-detail-row';
      row.append(textElement('small', '', label), textElement('span', '', value));
      metadata.appendChild(row);
    }
    const readme = scalar(firstValue(item, ['readme', 'readmeMarkdown', 'introduction']));
    controller.$('#pluginCommunityDetailReadme').textContent = readme;
    controller.$('#pluginCommunityDetailReadme').hidden = !readme;
    controller.$('#pluginCommunitySecurityStatus').textContent = '尚未读取此版本的 ClawHub 安全报告。';
    controller.$('#pluginCommunitySecurityBody').textContent = '';
    controller.$('#pluginCommunitySecurityButton').disabled = !packageName(item) || !version(item);
    controller.$('#pluginCommunityDetailInstallButton').disabled = !controller.canInstall() || !controller.installSpec(item);
  }

  function setSecurity(controller, result, error) {
    const status = controller.$('#pluginCommunitySecurityStatus');
    const body = controller.$('#pluginCommunitySecurityBody');
    if (error) {
      status.textContent = '读取安全报告失败：' + error;
      body.textContent = '';
      return;
    }
    const value = resultValue(result);
    const summary = scalar(value && (value.status || value.verdict || value.state || value.summary));
    status.textContent = summary ? 'ClawHub 报告状态：' + summary : 'ClawHub 已返回此版本的安全报告；页面不推断未提供的结论。';
    body.textContent = JSON.stringify(value, null, 2);
  }

  function installSpec(item) {
    const name = packageName(item);
    return name ? 'clawhub:' + name : '';
  }

  window.OpenClawPluginCommunityView = {
    records,
    nextCursor,
    packageName,
    version,
    installSpec,
    renderResults,
    renderDetails,
    setSecurity
  };
})();
