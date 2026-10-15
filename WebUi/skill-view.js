(function () {
  'use strict';
  const ARRAY_KEYS = ['skills', 'items', 'results'];

  function resultValue(response) { return response && response.result; }

  function resultArray(response) {
    const value = resultValue(response);
    if (Array.isArray(value)) return value;
    for (const key of ARRAY_KEYS) if (value && Array.isArray(value[key])) return value[key];
    return null;
  }

  function skillName(skill) {
    const value = skill.name || skill.skillName || skill.id || skill.slug;
    return value === undefined || value === null ? '' : String(value);
  }

  function textElement(tag, className, text) {
    const element = document.createElement(tag);
    element.className = className;
    element.textContent = text;
    return element;
  }

  function actionButton(label, action, name, disabled) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'button button-quiet plugin-action-button';
    button.dataset.skillAction = action;
    button.dataset.skillName = name;
    button.textContent = label;
    button.disabled = disabled;
    return button;
  }

  function renderRaw(controller, value) {
    const list = controller.$('#skillList');
    list.replaceChildren(textElement('p', 'plugin-format-error', '技能清单 JSON 中没有可识别的技能数组。原始结果：'));
    list.appendChild(textElement('pre', 'plugin-json plugin-raw-list', JSON.stringify(value, null, 2)));
    list.classList.remove('empty-state');
  }

  function renderCard(skill, busy) {
    const name = skillName(skill);
    const card = document.createElement('article');
    card.className = 'plugin-card skill-card';
    const heading = document.createElement('div');
    heading.className = 'plugin-card-heading';
    const status = skill.eligible === false ? '条件不足' : skill.status || (skill.eligible === true ? '可用' : '已发现');
    heading.append(textElement('strong', '', name || '未命名技能'), textElement('span', 'plugin-status-tag', String(status)));
    card.append(heading, textElement('code', 'plugin-id', name || '缺少技能名称'));
    const metadata = [skill.source, skill.version, skill.eligible === false ? '当前不可用' : ''].filter(Boolean);
    if (metadata.length) card.appendChild(textElement('small', 'plugin-meta', metadata.map(String).join(' · ')));
    const description = skill.description || skill.summary;
    if (description) card.appendChild(textElement('p', 'plugin-description', String(description)));
    const actions = document.createElement('div');
    actions.className = 'plugin-card-actions';
    if (name) actions.append(actionButton('详情', 'inspect', name, busy), actionButton('远程更新', 'update', name, busy));
    card.appendChild(actions);
    return card;
  }

  function render(controller) {
    const state = controller.state;
    const list = controller.$('#skillList');
    if (state.listFormatError) {
      controller.$('#skillCount').textContent = '—';
      controller.$('#skillVisibleCount').textContent = '—';
      renderRaw(controller, state.installedRaw);
      return;
    }
    const query = controller.$('#skillFilter').value.trim().toLowerCase();
    const visible = state.installed.filter(skill => [skillName(skill), skill.source, skill.version, skill.status, skill.description]
      .some(value => String(value || '').toLowerCase().includes(query)));
    controller.$('#skillCount').textContent = String(state.installed.length);
    controller.$('#skillVisibleCount').textContent = String(visible.length);
    list.replaceChildren();
    list.classList.toggle('empty-state', visible.length === 0);
    if (!visible.length) {
      list.textContent = state.loaded ? '没有匹配的当前技能。' : '连接服务器后读取技能清单';
      return;
    }
    const busy = state.loading || state.mutating || controller.connectionState.busy || !controller.isConnected();
    visible.forEach(skill => list.appendChild(renderCard(skill, busy)));
  }

  function diagnostics(response) {
    return response && response.diagnostics ? ' · 诊断：' + String(response.diagnostics) : '';
  }

  window.OpenClawSkillView = { resultValue, resultArray, skillName, render, diagnostics };
})();
