(function () {
  'use strict';
  const UNCATEGORIZED = '__uncategorized__';
  const CATEGORY_FIELDS = ['category', 'categories', 'capabilityCategories'];
  const NESTED_SOURCES = ['catalog', 'plugin', 'package', 'manifest', 'metadata', 'meta'];
  const LABEL_FIELDS = ['label', 'name', 'title', 'value', 'slug', 'id'];

  function labelOf(value) {
    if (typeof value === 'string' || typeof value === 'number') return String(value).trim();
    if (!value || typeof value !== 'object') return '';
    for (const key of LABEL_FIELDS) {
      const label = labelOf(value[key]);
      if (label) return label;
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
          const label = labelOf(entry);
          if (label) labels.set(label.toLowerCase(), label);
        }
      }
    }
    return Array.from(labels.values());
  }

  function appendOption(select, value, label) {
    const option = document.createElement('option');
    option.value = value;
    option.textContent = label;
    select.appendChild(option);
  }

  function updateFilter(select, records) {
    const selected = select.value;
    const selectedLabel = select.selectedOptions[0]?.textContent || selected;
    const categories = new Map();
    let hasUncategorized = false;
    for (const record of records) {
      const labels = values(record);
      if (!labels.length) hasUncategorized = true;
      for (const label of labels) categories.set(label.toLowerCase(), label);
    }
    select.replaceChildren();
    if (!categories.size) {
      appendOption(select, '', records.length ? '未提供分类' : '所有类别');
      select.disabled = true;
      return;
    }
    appendOption(select, '', '所有类别');
    Array.from(categories.entries()).sort((left, right) => left[1].localeCompare(right[1], 'zh-CN'))
      .forEach(([key, label]) => appendOption(select, key, label));
    if (hasUncategorized) appendOption(select, UNCATEGORIZED, '未分类');
    if (selected && !categories.has(selected) && selected !== UNCATEGORIZED) appendOption(select, selected, selectedLabel);
    if (selected === UNCATEGORIZED && !hasUncategorized) appendOption(select, UNCATEGORIZED, selectedLabel || '未分类');
    select.value = selected;
    select.disabled = false;
  }

  function matches(record, selected) {
    if (!selected) return true;
    const labels = values(record);
    if (selected === UNCATEGORIZED) return labels.length === 0;
    return labels.some(label => label.toLowerCase() === selected);
  }

  window.OpenClawPluginCategoryUtils = { UNCATEGORIZED, values, updateFilter, matches };
})();
