(function () {
  'use strict';
  const BYTES_PER_KIB = 1024;
  const BYTES_PER_MIB = BYTES_PER_KIB * BYTES_PER_KIB;

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
    button.dataset.customArtifactAction = action;
    button.dataset.customArtifactId = id;
    button.textContent = label;
    button.disabled = disabled;
    return button;
  }

  function renderCard(artifact, disabled) {
    const card = document.createElement('article');
    card.className = 'plugin-card custom-artifact-card';
    const heading = document.createElement('div');
    heading.className = 'plugin-card-heading';
    heading.append(textElement('strong', '', artifact.name), textElement('span', 'plugin-status-tag', '自制'));
    card.append(heading, textElement('code', 'plugin-id', artifact.fileName));
    const metadata = ['ZIP 包', artifact.version, sizeLabel(artifact.size)].filter(Boolean);
    card.appendChild(textElement('small', 'plugin-meta', metadata.join(' · ')));
    if (artifact.description) card.appendChild(textElement('p', 'plugin-description', artifact.description));
    const actions = document.createElement('div');
    actions.className = 'plugin-card-actions';
    actions.append(actionButton('详情', 'inspect', artifact.id, disabled), actionButton('移除', 'delete', artifact.id, disabled));
    card.appendChild(actions);
    return card;
  }

  function sizeLabel(value) {
    const size = Number(value) || 0;
    if (size < BYTES_PER_KIB) return size + ' B';
    if (size < BYTES_PER_MIB) return (size / BYTES_PER_KIB).toFixed(1) + ' KiB';
    return (size / BYTES_PER_MIB).toFixed(1) + ' MiB';
  }

  function renderList(controller, type, items) {
    const isPlugin = type === 'plugin';
    const query = controller.$(isPlugin ? '#customPluginSearch' : '#customSkillSearch').value.trim().toLowerCase();
    const list = controller.$(isPlugin ? '#customPluginList' : '#customSkillList');
    const count = controller.$(isPlugin ? '#customPluginCount' : '#customSkillCount');
    const visible = items.filter(item => [item.name, item.description, item.version, item.fileName]
      .some(value => String(value || '').toLowerCase().includes(query)));
    count.textContent = String(items.length);
    list.replaceChildren();
    list.classList.toggle('empty-state', visible.length === 0);
    if (!visible.length) {
      list.textContent = controller.state.loaded
        ? (query ? '没有匹配的自制项目。' : '还没有上传自制项目。')
        : '正在读取自定义列表…';
      return;
    }
    visible.forEach(item => list.appendChild(renderCard(item, isBusy(controller))));
  }

  function isBusy(controller) {
    return controller.state.loading || controller.state.uploading || Boolean(controller.state.deletingId);
  }

  function render(controller) {
    renderList(controller, 'plugin', controller.state.plugins);
    renderList(controller, 'skill', controller.state.skills);
  }

  window.OpenClawCustomArtifactView = { render };
})();
