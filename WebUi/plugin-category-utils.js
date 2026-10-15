(function () {
  'use strict';
  const UNCATEGORIZED = '__uncategorized__';
  const CATEGORY_FIELDS = ['category', 'categories', 'capabilityCategories', 'pluginCategory', 'categorySlug', 'clawHubCategories'];
  const NESTED_SOURCES = ['catalog', 'plugin', 'package', 'manifest', 'metadata', 'meta'];
  const VALUE_FIELDS = ['slug', 'id', 'value', 'name', 'label', 'title'];
  const CATEGORY_LABELS = Object.freeze({
    channels: '通信渠道', models: '模型', 'agent-runtimes': '智能体运行时', memory: '记忆', context: '上下文',
    voice: '语音', web: '网络', 'computer-use': '电脑控制', media: '媒体', security: '安全',
    integrations: '集成', 'developer-tools': '开发工具', infrastructure: '基础设施',
    'documents-files': '文档与文件', 'inbox-collaboration': '收件箱与协作', productivity: '效率工具',
    scheduling: '日程安排', 'finance-payments': '财务与支付', 'sales-marketing': '销售与营销',
    'data-analytics': '数据分析', 'agent-orchestration': '智能体编排', research: '研究', other: '其他',
    tools: '工具', runtime: '运行时', gateway: '网关'
  });
  const CATEGORY_ALIASES = Object.freeze({
    'agent runtimes': 'agent-runtimes', 'computer use': 'computer-use', 'developer tools': 'developer-tools',
    'documents and files': 'documents-files', 'documents & files': 'documents-files',
    'inbox and collaboration': 'inbox-collaboration', 'inbox & collaboration': 'inbox-collaboration',
    'finance and payments': 'finance-payments', 'finance & payments': 'finance-payments',
    'sales and marketing': 'sales-marketing', 'sales & marketing': 'sales-marketing',
    'data analytics': 'data-analytics', 'agent orchestration': 'agent-orchestration'
  });

  function normalize(value) {
    const text = String(value || '').trim().toLowerCase();
    return CATEGORY_ALIASES[text] || text.replace(/[\s_]+/g, '-');
  }

  function label(value) {
    const key = normalize(value);
    if (CATEGORY_LABELS[key]) return CATEGORY_LABELS[key];
    return /[\u3400-\u9fff]/.test(String(value || '')) ? String(value) : '其他类别';
  }

  function valueOf(value) {
    if (typeof value === 'string' || typeof value === 'number') return normalize(value);
    if (!value || typeof value !== 'object') return '';
    for (const key of VALUE_FIELDS) {
      const category = valueOf(value[key]);
      if (category) return category;
    }
    return '';
  }

  function sourcesOf(item) {
    if (!item || typeof item !== 'object') return [];
    const sources = [];
    const pending = [item];
    const seen = new Set();
    while (pending.length) {
      const source = pending.pop();
      if (!source || typeof source !== 'object' || seen.has(source)) continue;
      seen.add(source);
      sources.push(source);
      NESTED_SOURCES.forEach(key => {
        if (source[key] && typeof source[key] === 'object') pending.push(source[key]);
      });
    }
    return sources;
  }

  function values(item) {
    const labels = new Map();
    for (const source of sourcesOf(item)) {
      for (const field of CATEGORY_FIELDS) {
        const raw = source[field];
        const entries = Array.isArray(raw) ? raw : [raw];
        for (const entry of entries) {
          const category = valueOf(entry);
          if (category) labels.set(category, category);
        }
      }
    }
    return Array.from(labels.values());
  }

  function appendOption(select, value, text) {
    const option = document.createElement('option');
    option.value = value;
    option.textContent = text;
    select.appendChild(option);
  }

  function updateFilter(select, records) {
    const selected = select.value;
    const categories = new Set();
    let hasUncategorized = false;
    for (const record of records) {
      const categoriesForRecord = values(record);
      if (!categoriesForRecord.length) hasUncategorized = true;
      categoriesForRecord.forEach(category => categories.add(category));
    }
    select.replaceChildren();
    appendOption(select, '', '所有类别');
    Array.from(categories).sort((left, right) => label(left).localeCompare(label(right), 'zh-CN'))
      .forEach(category => appendOption(select, category, label(category)));
    if (hasUncategorized) appendOption(select, UNCATEGORIZED, '未分类');
    if (selected && !categories.has(selected) && selected !== UNCATEGORIZED)
      appendOption(select, selected, label(selected));
    if (selected === UNCATEGORIZED && !hasUncategorized) appendOption(select, UNCATEGORIZED, '未分类');
    select.value = selected;
    select.disabled = records.length === 0;
  }

  function matches(record, selected) {
    if (!selected) return true;
    const recordCategories = values(record);
    if (selected === UNCATEGORIZED) return recordCategories.length === 0;
    return recordCategories.includes(normalize(selected));
  }

  window.OpenClawPluginCategoryUtils = { UNCATEGORIZED, values, label, updateFilter, matches };
})();
