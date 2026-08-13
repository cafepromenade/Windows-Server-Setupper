(() => {
  'use strict';

  const api = window.exchangeInstaller;
  const state = {
    settings: null,
    regexTarget: null,
    regex: new Map(),
    conversionSource: null,
    conversionAdapter: null,
    docs: [],
    notifications: [],
    history: [],
    tickets: [],
    voices: []
  };

  const byId = (id) => document.getElementById(id);
  const all = (selector, root = document) => [...root.querySelectorAll(selector)];

  function notice(message, kind = 'info') {
    state.notifications.unshift({ id: crypto.randomUUID(), message, kind, at: new Date().toISOString(), selected: false });
    renderNotificationHistory();
    const stack = byId('notice-stack');
    const card = document.createElement('div');
    card.className = `notice ${kind}`;
    card.setAttribute('role', kind === 'error' ? 'alert' : 'status');
    card.textContent = message;
    const close = document.createElement('button');
    close.type = 'button';
    close.setAttribute('aria-label', 'Dismiss notification');
    close.textContent = '×';
    close.addEventListener('click', () => card.remove());
    card.append(close);
    stack.prepend(card);
  }

  function recordHistory(action, detail) {
    state.history.unshift({ id: crypto.randomUUID(), action, detail, at: new Date().toISOString() });
    renderHistory();
  }

  function renderNotificationHistory() {
    const list = byId('notification-history');
    if (!list) return;
    list.replaceChildren(...(state.notifications.length ? state.notifications.map((item) => {
      const row = document.createElement('li');
      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.checked = item.selected;
      checkbox.setAttribute('aria-label', `Select notification: ${item.message}`);
      checkbox.addEventListener('change', () => { item.selected = checkbox.checked; });
      row.append(checkbox, document.createTextNode(` ${item.at} · ${item.message}`));
      return row;
    }) : [document.createTextNode('No notifications recorded.')]));
  }

  function renderHistory() {
    const list = byId('settings-history');
    if (!list) return;
    list.replaceChildren(...(state.history.length ? state.history.map((item) => {
      const row = document.createElement('li');
      row.textContent = `${item.at} · ${item.action}: ${item.detail}`;
      return row;
    }) : [document.createTextNode('No settings changes in this session.')]));
  }

  function searchMatcher(input) {
    const config = state.regex.get(input.id);
    if (config?.enabled) {
      try { return new RegExp(config.pattern, config.flags); }
      catch { return null; }
    }
    return input.value.trim().toLocaleLowerCase();
  }

  function matches(matcher, text) {
    if (!matcher || matcher === '') return true;
    if (matcher instanceof RegExp) { matcher.lastIndex = 0; return matcher.test(text); }
    return text.toLocaleLowerCase().includes(matcher);
  }

  function bindTabs() {
    for (const tabList of all('[role="tablist"]')) {
      const tabs = all('[role="tab"]', tabList);
      const activate = (tab) => {
        tabs.forEach((candidate) => {
          const selected = candidate === tab;
          candidate.setAttribute('aria-selected', String(selected));
          candidate.tabIndex = selected ? 0 : -1;
          const panel = byId(candidate.getAttribute('aria-controls'));
          if (panel) panel.hidden = !selected;
        });
        tab.focus();
      };
      tabs.forEach((tab) => tab.addEventListener('click', () => activate(tab)));
      tabList.addEventListener('keydown', (event) => {
        const current = tabs.indexOf(document.activeElement);
        if (current < 0) return;
        const vertical = getComputedStyle(tabList).flexDirection === 'column';
        const backward = vertical ? 'ArrowUp' : 'ArrowLeft';
        const forward = vertical ? 'ArrowDown' : 'ArrowRight';
        let next = current;
        if (event.key === backward) next = (current - 1 + tabs.length) % tabs.length;
        else if (event.key === forward) next = (current + 1) % tabs.length;
        else if (event.key === 'Home') next = 0;
        else if (event.key === 'End') next = tabs.length - 1;
        else return;
        event.preventDefault();
        activate(tabs[next]);
      });
    }
  }

  function bindRegexBuilder() {
    const dialog = byId('regex-dialog');
    const pattern = byId('regex-pattern');
    const flags = byId('regex-flags');
    const sample = byId('regex-sample');
    const feedback = byId('regex-feedback');
    const evaluate = () => {
      if (pattern.value.length > 512 || sample.value.length > 4096) { feedback.textContent = 'Pattern or sample exceeds the local evaluation bound.'; return; }
      try {
        const expression = new RegExp(pattern.value, flags.value);
        const limited = sample.value.slice(0, 4096);
        const found = [];
        let match;
        const global = expression.global ? expression : new RegExp(expression.source, `${expression.flags}g`);
        while ((match = global.exec(limited)) && found.length < 100) {
          found.push({ match: match[0], index: match.index, groups: match.slice(1) });
          if (match[0] === '') global.lastIndex += 1;
        }
        feedback.textContent = found.length ? JSON.stringify(found, null, 2) : 'No match in the bounded sample.';
      } catch (error) { feedback.textContent = `Syntax error: ${error.message}`; }
    };
    all('.regex-builder-button').forEach((button) => button.addEventListener('click', () => {
      state.regexTarget = byId(button.dataset.regexFor);
      const prior = state.regex.get(state.regexTarget?.id) || {};
      pattern.value = prior.pattern || state.regexTarget?.value || '';
      flags.value = prior.flags || 'iu';
      sample.value = state.regexTarget?.value || '';
      evaluate();
      dialog.showModal();
      pattern.focus();
    }));
    all('.regex-token').forEach((button) => button.addEventListener('click', () => {
      const token = button.dataset.token;
      const start = pattern.selectionStart;
      pattern.setRangeText(token, start, pattern.selectionEnd, 'end');
      evaluate();
    }));
    byId('regex-literal').addEventListener('input', (event) => { pattern.value = event.target.value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); evaluate(); });
    byId('regex-class').addEventListener('input', (event) => { pattern.value = `[${event.target.value.replace(/[\]\\]/g, '\\$&')}]`; evaluate(); });
    [pattern, flags, sample].forEach((control) => control.addEventListener('input', evaluate));
    byId('regex-apply').addEventListener('click', () => {
      if (!state.regexTarget) return;
      try { new RegExp(pattern.value, flags.value); }
      catch { return; }
      state.regex.set(state.regexTarget.id, { enabled: true, pattern: pattern.value, flags: flags.value });
      state.regexTarget.value = pattern.value;
      state.regexTarget.dispatchEvent(new Event('input', { bubbles: true }));
    });
    byId('regex-copy').addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(`/${pattern.value}/${flags.value}`); notice('Pattern copied locally.', 'success'); }
      catch { notice('Clipboard write was refused. Select the raw pattern to copy it.', 'error'); }
    });
  }

  function collectSettings() {
    const schedules = state.settings?.schedules || [];
    return {
      ...state.settings,
      schemaVersion: 1,
      language: byId('language-mode').value,
      funnyEnglish: Number(byId('funny-en').value),
      funnyCantonese: Number(byId('funny-yue').value),
      showDialogEmojis: byId('dialog-emojis').checked,
      theme: byId('theme-mode').value,
      density: byId('density-mode').value,
      accent: byId('accent-color').value,
      fontFamily: byId('font-family').value.trim() || 'system-ui',
      fontScale: Number(byId('font-scale').value),
      fontWeight: Number(byId('font-weight').value),
      reducedMotion: byId('reduced-motion').checked,
      tabDock: byId('tab-dock').value,
      appDisplayName: byId('app-display-name').value.trim() || 'Exchange Auto Installer',
      schoolMode: byId('school-mode').checked,
      schoolModeName: byId('school-mode-name').value.trim() || 'School mode',
      narratorEnabled: byId('narrator-enabled').checked,
      narratorLanguage: byId('narrator-language').value,
      narratorVoiceEnglish: byId('narrator-voice-en').value,
      narratorVoiceCantonese: byId('narrator-voice-yue').value,
      narrationRate: Number(byId('narrator-rate').value),
      narrationPitch: Number(byId('narrator-pitch').value),
      updateEnabled: true,
      schedules
    };
  }

  function applySettings(settings) {
    state.settings = settings;
    const values = {
      'language-mode': settings.language, 'funny-en': settings.funnyEnglish, 'funny-yue': settings.funnyCantonese,
      'dialog-emojis': settings.showDialogEmojis, 'theme-mode': settings.theme, 'density-mode': settings.density,
      'accent-color': settings.accent, 'font-family': settings.fontFamily, 'font-scale': settings.fontScale,
      'font-weight': settings.fontWeight, 'reduced-motion': settings.reducedMotion, 'tab-dock': settings.tabDock,
      'app-display-name': settings.appDisplayName, 'school-mode': settings.schoolMode, 'school-mode-name': settings.schoolModeName,
      'narrator-enabled': settings.narratorEnabled, 'narrator-language': settings.narratorLanguage,
      'narrator-voice-en': settings.narratorVoiceEnglish, 'narrator-voice-yue': settings.narratorVoiceCantonese,
      'narrator-rate': settings.narrationRate, 'narrator-pitch': settings.narrationPitch
    };
    Object.entries(values).forEach(([id, value]) => { const control = byId(id); if (control) { if (control.type === 'checkbox') control.checked = Boolean(value); else control.value = String(value); } });
    byId('funny-en-output').value = String(settings.funnyEnglish);
    byId('funny-yue-output').value = String(settings.funnyCantonese);
    byId('school-mode-label').textContent = settings.schoolModeName;
    document.documentElement.dataset.theme = settings.theme;
    document.documentElement.dataset.density = settings.density;
    document.documentElement.dataset.reducedMotion = String(settings.reducedMotion);
    document.documentElement.style.setProperty('--primary', settings.accent);
    document.documentElement.style.fontFamily = `${settings.fontFamily}, "Noto Sans CJK HK", "Segoe UI", system-ui, sans-serif`;
    document.documentElement.style.fontSize = `${settings.fontScale * 16}px`;
    document.documentElement.style.fontWeight = String(settings.fontWeight);
    document.documentElement.lang = settings.language === 'yue' ? 'zh-HK' : settings.language === 'bilingual' ? 'en' : 'en';
    document.body.classList.toggle('school-mode', settings.schoolMode);
    renderSchedules();
  }

  async function saveSettings(event) {
    if (event?.target && ['range', 'color'].includes(event.target.type)) {
      if (event.target.id === 'funny-en') byId('funny-en-output').value = event.target.value;
      if (event.target.id === 'funny-yue') byId('funny-yue-output').value = event.target.value;
    }
    try {
      const previous = JSON.stringify(state.settings);
      const saved = await api.saveSettings(collectSettings());
      applySettings(saved);
      if (previous !== JSON.stringify(saved)) recordHistory('settings changed', event?.target?.id || 'settings surface');
    } catch (error) { notice(`Settings were not saved: ${error.message}`, 'error'); }
  }

  function bindSettings() {
    const ids = ['language-mode', 'funny-en', 'funny-yue', 'dialog-emojis', 'theme-mode', 'density-mode', 'accent-color', 'font-family', 'font-scale', 'font-weight', 'reduced-motion', 'tab-dock', 'app-display-name', 'school-mode', 'school-mode-name', 'narrator-enabled', 'narrator-language', 'narrator-voice-en', 'narrator-voice-yue', 'narrator-rate', 'narrator-pitch'];
    ids.forEach((id) => byId(id)?.addEventListener('change', saveSettings));
    byId('settings-search').addEventListener('input', () => {
      const input = byId('settings-search');
      const matcher = searchMatcher(input);
      let visible = 0;
      all('.setting-item').forEach((item) => { const show = matches(matcher, item.textContent); item.hidden = !show; if (show) visible += 1; });
      byId('settings-search-count').textContent = matcher === null ? 'Regex syntax is invalid.' : `${visible} setting items shown.`;
    });
    byId('vocabulary-import').addEventListener('click', async () => { try { const result = await api.importPersonalVocabulary(); if (result) { byId('vocabulary-status').textContent = `${result.entryCount} private replacements loaded locally.`; recordHistory('personal vocabulary loaded', `${result.entryCount} entries; content omitted`); } } catch (error) { notice(error.message, 'error'); } });
    byId('vocabulary-clear').addEventListener('click', async () => { await api.clearPersonalVocabulary(); byId('vocabulary-status').textContent = 'Private cache cleared. Original shipped wording is active.'; recordHistory('personal vocabulary cleared', 'private cache purged'); });
    byId('appearance-reset').addEventListener('click', async () => { applySettings(await api.saveSettings({ schemaVersion: 1, language: 'en', funnyEnglish: 2, funnyCantonese: 3, showDialogEmojis: true, theme: 'system', density: 'comfortable', accent: '#1f5f91', fontFamily: 'system-ui', fontScale: 1, fontWeight: 400, reducedMotion: false, tabDock: 'left', appDisplayName: 'Exchange Auto Installer', schoolMode: false, schoolModeName: 'School mode', narratorEnabled: false, narratorLanguage: 'en', narratorVoiceEnglish: 'auto', narratorVoiceCantonese: 'auto', narrationRate: 1, narrationPitch: 1, updateEnabled: true, schedules: state.settings?.schedules || [] })); recordHistory('appearance reset', 'shipped defaults restored'); });
    byId('schedule-add').addEventListener('click', async () => { state.settings.schedules = [...(state.settings.schedules || []), { id: crypto.randomUUID(), label: 'Every-day theme schedule', enabled: true, weekdays: [0,1,2,3,4,5,6], startTime: byId('schedule-start').value, endTime: byId('schedule-end').value, startDate: null, endDate: null, values: { theme: byId('theme-mode').value } }]; await saveSettings({ target: { id: 'schedule-add' } }); });
    byId('logo-upload').addEventListener('change', (event) => {
      const file = event.target.files?.[0];
      if (!file) return;
      if (file.size > 2 * 1024 * 1024 || !['image/png','image/jpeg','image/webp','image/svg+xml'].includes(file.type)) { byId('logo-status').textContent = 'Rejected: choose a PNG, JPEG, WebP, or SVG no larger than 2 MiB.'; return; }
      const reader = new FileReader();
      reader.onload = () => { byId('logo-preview').src = reader.result; byId('logo-status').textContent = 'Custom preview decoded locally for this session. Package identity is unchanged.'; };
      reader.onerror = () => { byId('logo-status').textContent = 'The image could not be decoded; the prior logo remains active.'; };
      reader.readAsDataURL(file);
    });
    byId('logo-reset').addEventListener('click', () => { byId('logo-preview').src = '../assets/logo.svg'; byId('logo-status').textContent = 'Using the shipped project logo.'; });
  }

  function renderSchedules() {
    const list = byId('schedule-list');
    const schedules = state.settings?.schedules || [];
    list.replaceChildren(...(schedules.length ? schedules.map((schedule) => { const row = document.createElement('li'); row.textContent = `${schedule.label}: ${schedule.startTime}–${schedule.endTime} local time, ${schedule.weekdays.length === 7 ? 'every day' : `${schedule.weekdays.length} weekdays`}`; return row; }) : [document.createTextNode('No schedules saved.')]));
  }

  function bindNarrator() {
    const refreshVoices = () => {
      state.voices = speechSynthesis.getVoices();
      const populate = (select, language, status) => {
        const selected = select.value;
        select.replaceChildren(new Option('Choose automatically', 'auto'), ...state.voices.filter((voice) => voice.lang.toLowerCase().startsWith(language)).map((voice) => new Option(`${voice.name} · ${voice.lang}${voice.localService ? '' : ' · network-backed'}`, voice.voiceURI)));
        select.value = [...select.options].some((option) => option.value === selected) ? selected : 'auto';
        status.textContent = select.options.length > 1 ? `${select.options.length - 1} installed matching voice(s).` : 'No matching voice is installed; the narrator will fall back or stay quiet.';
      };
      populate(byId('narrator-voice-en'), 'en', byId('narrator-status-en'));
      populate(byId('narrator-voice-yue'), 'zh', byId('narrator-status-yue'));
    };
    refreshVoices();
    speechSynthesis.addEventListener('voiceschanged', refreshVoices);
    byId('narrator-test').addEventListener('click', () => {
      speechSynthesis.cancel();
      const utterance = new SpeechSynthesisUtterance('Exchange Auto Installer narrator test. No installation action was started.');
      utterance.rate = Number(byId('narrator-rate').value);
      utterance.pitch = Number(byId('narrator-pitch').value);
      const selected = byId('narrator-voice-en').value;
      utterance.voice = state.voices.find((voice) => voice.voiceURI === selected) || null;
      speechSynthesis.speak(utterance);
    });
  }

  async function bindConverter() {
    const catalog = await api.getConversionCatalog();
    const root = byId('converter-catalog');
    root.replaceChildren(...catalog.categories.map((category) => {
      const section = document.createElement('section');
      section.className = 'adapter-category';
      const heading = document.createElement('h4');
      heading.textContent = category.label;
      const search = document.createElement('input');
      search.type = 'search';
      search.id = `converter-search-${category.id}`;
      search.placeholder = `Search ${category.label}`;
      search.setAttribute('aria-label', `Search ${category.label} adapters`);
      const regex = document.createElement('button');
      regex.type = 'button';
      regex.className = 'button secondary regex-builder-button';
      regex.dataset.regexFor = search.id;
      regex.textContent = 'Regex builder…';
      const rows = catalog.adapters.filter((adapter) => adapter.category === category.id).map((adapter) => {
        const label = document.createElement('label');
        label.className = 'adapter-card';
        const radio = document.createElement('input');
        radio.type = 'radio'; radio.name = 'conversion-adapter'; radio.value = adapter.id; radio.disabled = !adapter.enabled;
        radio.addEventListener('change', () => { state.conversionAdapter = adapter.id; byId('converter-run').disabled = !state.conversionSource; });
        const copy = document.createElement('span');
        copy.innerHTML = `<strong></strong><small></small>`;
        copy.querySelector('strong').textContent = adapter.label;
        copy.querySelector('small').textContent = adapter.enabled ? `Bundled offline · ${adapter.lossiness}` : `Unavailable · ${adapter.missing}`;
        label.append(radio, copy);
        section.append(label);
        return label;
      });
      search.addEventListener('input', () => { const matcher = searchMatcher(search); rows.forEach((row) => { row.hidden = !matches(matcher, row.textContent); }); });
      section.append(heading, search, regex, ...rows);
      return section;
    }));
    bindRegexBuilderForNewButtons();
    byId('converter-choose').addEventListener('click', async () => { try { state.conversionSource = await api.chooseConversionSource(); if (state.conversionSource) byId('converter-source').textContent = `${state.conversionSource.path} · ${state.conversionSource.size} bytes · ${state.conversionSource.detectedType}`; byId('converter-run').disabled = !(state.conversionSource && state.conversionAdapter); } catch (error) { notice(error.message, 'error'); } });
    byId('converter-run').addEventListener('click', async () => { try { byId('converter-status').textContent = 'Converting one bounded file…'; const result = await api.convertFile({ sourcePath: state.conversionSource.path, adapterId: state.conversionAdapter }); byId('converter-status').textContent = result ? `Converted ${result.bytes} bytes to ${result.path}.` : 'Conversion cancelled before writing output.'; } catch (error) { byId('converter-status').textContent = `Conversion failed: ${error.message}`; } });
    byId('converter-cancel').addEventListener('click', () => { state.conversionSource = null; state.conversionAdapter = null; byId('converter-source').textContent = 'No source selected.'; byId('converter-run').disabled = true; byId('converter-status').textContent = 'Queue is empty.'; });
  }

  function bindRegexBuilderForNewButtons() {
    all('.regex-builder-button:not([data-regex-bound])').forEach((button) => { button.dataset.regexBound = 'true'; button.addEventListener('click', () => { const target = byId(button.dataset.regexFor); state.regexTarget = target; byId('regex-pattern').value = state.regex.get(target.id)?.pattern || target.value; byId('regex-sample').value = target.value; byId('regex-dialog').showModal(); byId('regex-pattern').focus(); }); });
  }

  async function refreshOllama() {
    byId('ollama-status').textContent = 'Checking the local Ollama API…';
    const result = await api.getOllamaStatus();
    byId('ollama-status').className = `status-chip ${result.status === 'healthy' ? 'passed' : 'warning'}`;
    byId('ollama-status').textContent = result.status === 'healthy' ? `Healthy · version ${result.version} · ${result.installed.length} installed model(s)` : `Unavailable · ${result.reason}`;
    const running = new Set(result.running.map((model) => model.name));
    const rows = result.installed.map((model) => { const tr = document.createElement('tr'); const fit = model.size ? 'Unknown — RAM, VRAM, driver, context, and disk evidence are incomplete' : 'Unknown — model size metadata is missing'; tr.innerHTML = '<td></td><td></td><td></td><td></td>'; tr.children[0].textContent = model.name; tr.children[1].textContent = running.has(model.name) ? 'Running' : 'Installed'; tr.children[2].textContent = model.size ? `${model.size} bytes` : 'Unknown'; tr.children[3].textContent = fit; return tr; });
    byId('ollama-models').replaceChildren(...(rows.length ? rows : [Object.assign(document.createElement('tr'), { innerHTML: '<td colspan="4">No verified local models.</td>' })]));
  }

  async function bindDocs() {
    state.docs = await api.getOfflineDocs();
    const list = byId('docs-list');
    const article = byId('docs-article');
    const open = (doc) => { article.textContent = doc.content; article.focus(); };
    const render = () => {
      const matcher = searchMatcher(byId('docs-search'));
      const docs = state.docs.filter((doc) => matches(matcher, `${doc.title} ${doc.content}`));
      list.replaceChildren(...docs.map((doc) => { const button = document.createElement('button'); button.type = 'button'; button.textContent = doc.title; button.addEventListener('click', () => open(doc)); return button; }));
      if (docs[0]) open(docs[0]); else article.textContent = 'No bundled article matches the active search.';
    };
    byId('docs-search').addEventListener('input', render);
    render();
  }

  function renderUpdate(update) {
    byId('manual-update-status').textContent = update.error ? `${update.status}: ${update.error}` : `${update.status} · current ${update.currentVersion}${update.updateVersion ? ` · update ${update.updateVersion}` : ''}`;
    byId('manual-update-restart').disabled = update.status !== 'ready';
    const banner = byId('update-banner');
    banner.classList.toggle('hidden', update.status !== 'ready');
    if (update.status === 'ready') byId('update-message').textContent = `${update.updateVersion || 'A new version'} is downloaded. The unsigned package may trigger an unknown-publisher warning. Save work, then restart when ready.`;
  }

  function bindUpdates() {
    byId('manual-update-check').addEventListener('click', async () => { try { renderUpdate(await api.checkForUpdates()); } catch (error) { renderUpdate({ status: 'failed', currentVersion: 'unknown', error: error.message }); } });
    [byId('manual-update-restart'), byId('update-restart')].forEach((button) => button.addEventListener('click', () => api.restartToInstallUpdate()));
    byId('update-later').addEventListener('click', () => byId('update-banner').classList.add('hidden'));
    api.getUpdateStatus().then(renderUpdate);
    api.subscribeUpdates?.(renderUpdate);
  }

  function bindCommandPalette() {
    const dialog = byId('command-palette');
    const search = byId('command-search');
    const commands = [
      ['Configure installation', 'configuration'], ['Run preflight', 'preflight'], ['Review exact plan', 'review'], ['Installation progress', 'installation'], ['Redacted logs', 'logs'], ['OpenCode repair adviser', 'repair'], ['Settings and tools', 'workspace-tools'],
      ['Language mode', 'settings-panel-general'], ['Appearance', 'settings-panel-appearance'], ['File converter', 'settings-panel-converter'], ['Ollama manager', 'settings-panel-ollama'], ['Offline documentation', 'settings-panel-docs'], ['History and support', 'settings-panel-history']
    ];
    const render = () => {
      const matcher = searchMatcher(search);
      byId('command-results').replaceChildren(...commands.filter(([label]) => matches(matcher, label)).map(([label, id]) => {
        const li = document.createElement('li');
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'button secondary'; button.textContent = label;
        button.addEventListener('click', () => {
          const panel = byId(id);
          const owningTab = document.querySelector(`[aria-controls="${id}"]`);
          if (owningTab) owningTab.click();
          panel?.scrollIntoView({ behavior: state.settings?.reducedMotion ? 'auto' : 'smooth', block: 'start' });
          panel?.focus?.();
          dialog.close();
        });
        li.append(button); return li;
      }));
    };
    byId('open-command-palette').addEventListener('click', () => { dialog.showModal(); search.focus(); render(); });
    window.addEventListener('keydown', (event) => { if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === 'f') { event.preventDefault(); dialog.showModal(); search.focus(); render(); } });
    search.addEventListener('input', render);
  }

  function bindHistorySupport() {
    byId('notifications-select-all').addEventListener('click', () => { state.notifications.forEach((item) => { item.selected = true; }); renderNotificationHistory(); });
    byId('notifications-export').addEventListener('click', () => downloadJson('exchange-notifications.json', state.notifications.filter((item) => item.selected)));
    byId('notifications-delete').addEventListener('click', () => { const count = state.notifications.filter((item) => item.selected).length; if (!count) { notice('Select at least one notification first.', 'warning'); return; } if (window.confirm(`Delete ${count} selected notification record(s)? This local action cannot be undone in this release.`)) { state.notifications = state.notifications.filter((item) => !item.selected); renderNotificationHistory(); } });
    byId('support-ticket').addEventListener('click', () => { const ticket = { number: `LOCAL-${String(state.tickets.length + 1).padStart(4, '0')}`, status: 'Resolved locally', severity: 'Cosmic inconvenience', createdAt: new Date().toISOString() }; state.tickets.unshift(ticket); const row = document.createElement('li'); row.textContent = `${ticket.number} · ${ticket.severity} · ${ticket.status}. Open the application-data folder and delete it yourself to reset toy locks.`; byId('support-ticket-list').replaceChildren(...state.tickets.map((item) => { const li = document.createElement('li'); li.textContent = `${item.number} · ${item.severity} · ${item.status}`; return li; })); recordHistory('support ticket created', ticket.number); });
    byId('lock-wizard').addEventListener('click', () => notice('Toy-lock credential storage is unavailable in this release. No lock was created; delete the app local-data folder remains the documented reset route.', 'warning'));
    byId('settings-export').addEventListener('click', async () => downloadJson('exchange-settings-redacted.json', await api.exportSettings()));
  }

  function downloadJson(name, value) {
    const blob = new Blob([`${JSON.stringify(value, null, 2)}\n`], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a'); link.href = url; link.download = name; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  async function initialize() {
    if (!api) return;
    bindTabs();
    bindRegexBuilder();
    bindSettings();
    bindNarrator();
    bindUpdates();
    bindCommandPalette();
    bindHistorySupport();
    byId('ollama-refresh').addEventListener('click', refreshOllama);
    state.settings = await api.getSettings();
    applySettings(state.settings);
    await Promise.all([bindConverter(), bindDocs()]);
    bindRegexBuilderForNewButtons();
  }

  initialize().catch((error) => notice(`Settings and tools initialization failed: ${error.message}`, 'error'));
})();
