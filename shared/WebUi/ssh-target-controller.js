(function () {
  'use strict';

  // 概览页「SSH 目标」切换器：列表来自后端 settings.targets，选中后写入连接设置并重连。
  function create(deps) {
    const { $, state, applyTarget } = deps;
    const button = $('#sshTargetButton');
    const menu = $('#sshTargetMenu');
    if (!button || !menu) return { setTargets() {}, sync() {}, close() {} };

    let targets = [];
    let open = false;

    function connection() { return state.value || {}; }

    function matches(target) {
      const value = connection();
      return Boolean(target.host) &&
        target.host === value.host &&
        target.username === value.username &&
        Number(target.port || 22) === Number(value.port || 22);
    }

    function addressOf(target) {
      const port = Number(target.port || 22);
      return target.username + '@' + target.host + (port === 22 ? '' : ':' + port);
    }

    function render() {
      menu.textContent = '';
      if (!targets.length) {
        const empty = document.createElement('div');
        empty.className = 'ssh-target-empty';
        empty.textContent = '未读取到可用的 SSH 目标。';
        menu.append(empty);
        return;
      }
      targets.forEach(target => {
        const active = matches(target);
        const option = document.createElement('button');
        option.type = 'button';
        option.className = active ? 'ssh-target-option active' : 'ssh-target-option';
        option.setAttribute('role', 'option');
        option.setAttribute('aria-selected', active ? 'true' : 'false');

        const mark = document.createElement('span');
        mark.className = 'ssh-target-mark';
        mark.textContent = target.id === 'local' ? '⌂' : '☁';

        const copy = document.createElement('span');
        copy.className = 'ssh-target-copy';
        const label = document.createElement('strong');
        label.textContent = target.label || target.id;
        const address = document.createElement('code');
        address.textContent = addressOf(target);
        copy.append(label, address);

        const check = document.createElement('span');
        check.className = 'ssh-target-check';
        check.textContent = '✓';

        option.append(mark, copy, check);
        option.addEventListener('click', () => selectTarget(target));
        menu.append(option);
      });

      const hint = document.createElement('div');
      hint.className = 'ssh-target-hint';
      hint.textContent = '切换后会写入连接设置，并重新扫描该服务器的工作区。';
      menu.append(hint);
    }

    function sync() {
      const active = targets.find(matches) || null;
      const value = connection();
      const summary = value.username && value.host ? value.username + '@' + value.host : '未配置 SSH 目标';
      $('#targetSummary').textContent = summary;
      $('#targetLabel').textContent = active ? active.label : 'SSH 目标';
      button.title = active
        ? '当前目标：' + active.label + '（' + summary + '），点击切换'
        : '点击选择 SSH 目标';
      if (open) render();
    }

    function setOpen(next) {
      open = Boolean(next);
      button.setAttribute('aria-expanded', open ? 'true' : 'false');
      if (open) { render(); menu.hidden = false; }
      else menu.hidden = true;
    }

    function close() { if (open) setOpen(false); }

    async function selectTarget(target) {
      const alreadyActive = matches(target);
      close();
      if (alreadyActive) return;
      await applyTarget(target);
    }

    function setTargets(list) {
      targets = Array.isArray(list) ? list.filter(item => item && item.host && item.username) : [];
      if (open) render();
      sync();
    }

    button.addEventListener('click', event => {
      event.preventDefault();
      event.stopPropagation();
      setOpen(!open);
    });
    document.addEventListener('click', event => {
      if (!open) return;
      if (menu.contains(event.target) || button.contains(event.target)) return;
      close();
    });
    document.addEventListener('keydown', event => {
      if (!open || event.key !== 'Escape') return;
      close();
      button.focus();
    });

    return { setTargets, sync, close };
  }

  window.OpenClawSshTargetController = { create };
})();
