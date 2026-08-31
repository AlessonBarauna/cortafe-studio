(function () {
  const histories = new Map();
  const timers = new Map();
  let applying = false;

  const renderBase = renderProject;
  renderProject = function (project) {
    renderBase(project);
    if (project.status !== 'ready') return;
    installToolbar();
    document.querySelectorAll('.clip-card').forEach(card => ensureHistory(card));
  };

  function snapshot(card) {
    const values = {};
    card.querySelectorAll('[name]').forEach(field => values[field.name] = field.type === 'checkbox' ? field.checked : field.value);
    return values;
  }

  function ensureHistory(card) {
    if (!histories.has(card.dataset.clip)) histories.set(card.dataset.clip, { undo: [], redo: [], current: snapshot(card) });
  }

  function installToolbar() {
    const actions = document.querySelector('#projectView .section-head .d-flex');
    if (!actions || document.querySelector('#editorHistoryTools')) return;
    actions.insertAdjacentHTML('afterbegin', `<div id="editorHistoryTools" class="editor-history-tools"><button class="btn btn-outline-light" data-editor-undo title="Desfazer (Ctrl+Z)">↶</button><button class="btn btn-outline-light" data-editor-redo title="Refazer (Ctrl+Y)">↷</button><span id="editorSaveState"><i></i> Salvo</span></div>`);
    actions.querySelector('[data-editor-undo]').onclick = () => move('undo');
    actions.querySelector('[data-editor-redo]').onclick = () => move('redo');
    updateButtons();
  }

  function activeCard() { return document.querySelector('.clip-card.active') || document.querySelector('.clip-card'); }

  function move(direction) {
    const card = activeCard(); if (!card) return;
    ensureHistory(card); const history = histories.get(card.dataset.clip); const source = history[direction];
    if (!source.length) return;
    const destination = direction === 'undo' ? history.redo : history.undo;
    destination.push(snapshot(card));
    const target = source.pop(); applying = true;
    card.querySelectorAll('[name]').forEach(field => { if (!(field.name in target)) return; if (field.type === 'checkbox') field.checked = target[field.name]; else field.value = target[field.name]; field.dispatchEvent(new Event('change', { bubbles: true })); });
    applying = false; history.current = target; scheduleSave(card, 250); updateButtons();
  }

  function scheduleSave(card, delay = 1400) {
    clearTimeout(timers.get(card.dataset.clip)); setState('saving', 'Alterações pendentes');
    timers.set(card.dataset.clip, setTimeout(async () => {
      try { setState('saving', 'Salvando…'); await saveClip(current, card); histories.get(card.dataset.clip).current = snapshot(card); setState('saved', 'Salvo automaticamente'); }
      catch (error) { setState('error', 'Falha ao salvar'); toast(error.message); }
    }, delay));
  }

  function setState(kind, label) { const state = document.querySelector('#editorSaveState'); if (!state) return; state.className = kind; state.innerHTML = `<i></i> ${label}`; }
  function updateButtons() { const card = activeCard(), history = card ? histories.get(card.dataset.clip) : null; document.querySelector('[data-editor-undo]')?.toggleAttribute('disabled', !history?.undo.length); document.querySelector('[data-editor-redo]')?.toggleAttribute('disabled', !history?.redo.length); }

  document.addEventListener('focusin', event => { const card = event.target.closest?.('.clip-card'); if (card) { ensureHistory(card); updateButtons(); } });
  document.addEventListener('input', event => {
    const card = event.target.closest?.('.clip-card'); if (!card || applying || !event.target.name) return;
    ensureHistory(card); const history = histories.get(card.dataset.clip); const before = history.current; const after = snapshot(card);
    if (JSON.stringify(before) === JSON.stringify(after)) return;
    history.undo.push(before); if (history.undo.length > 50) history.undo.shift(); history.redo = []; history.current = after; scheduleSave(card); updateButtons();
  });
  document.addEventListener('change', event => { const card = event.target.closest?.('.clip-card'); if (card && !applying && event.target.name) scheduleSave(card); });
  document.addEventListener('keydown', event => {
    if (!(event.ctrlKey || event.metaKey) || event.altKey) return;
    if (event.key.toLowerCase() === 'z') { event.preventDefault(); move(event.shiftKey ? 'redo' : 'undo'); }
    if (event.key.toLowerCase() === 'y') { event.preventDefault(); move('redo'); }
    if (event.key.toLowerCase() === 's') { event.preventDefault(); const card = activeCard(); if (card) scheduleSave(card, 0); }
  });
})();
