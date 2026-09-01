(function () {
  let editorProject = null;
  let activeClipId = null;
  let timelineZoom = 1;

  const renderBase = renderProject;
  renderProject = function (project) {
    renderBase(project);
    if (project.status !== 'ready') return leaveEditorMode();
    editorProject = project;
    activeClipId = professionalClipId || project.clips[0]?.id || null;
    mountWorkspace(project);
  };

  const selectBase = selectClip;
  selectClip = function (project, id) {
    selectBase(project, id);
    activeClipId = id;
    syncWorkspace(project, id);
  };

  const homeBase = home;
  home = async function () { leaveEditorMode(); return homeBase(); };

  function leaveEditorMode() { document.body.classList.remove('cc-editor-active'); }

  function mountWorkspace(project) {
    const root = document.querySelector('#projectView');
    const layout = root?.querySelector('.clip-layout');
    const preview = layout?.querySelector('.preview-column');
    const inspector = layout?.querySelector('.clip-list');
    if (!root || !layout || !preview || !inspector || layout.querySelector('.cc-media-bin')) return;
    document.body.classList.add('cc-editor-active');
    window.scrollTo(0, 0);
    root.classList.add('cc-workspace-v2');
    layout.classList.add('cc-editor-grid');
    preview.classList.add('cc-canvas-stage');
    inspector.classList.add('cc-properties-panel');
    inspector.insertAdjacentHTML('afterbegin', `<header class="cc-panel-head"><div><span>CORTE ATIVO</span><strong id="ccInspectorTitle">Corte selecionado</strong></div><i id="ccInspectorScore">0</i></header><label class="cc-clip-picker"><span>Selecionar corte</span><select id="ccClipPicker">${project.clips.map((clip,index)=>`<option value="${clip.id}">${String(index+1).padStart(2,'0')} · ${escapeHtml(clip.title)}</option>`).join('')}</select></label>`);
    const actions = root.querySelector('.section-head .d-flex');
    actions?.insertAdjacentHTML('afterbegin', `<span class="cc-render-chip"><i></i>${project.renderCompleted || project.clips.filter(clip => clip.videoPath).length}/${project.renderTotal || project.clips.length} renderizados</span><details class="cc-more-actions"><summary>Mais</summary><div><button type="button" data-cc-forward="approveAll">Aprovar todos</button><button type="button" data-cc-forward="reanalyze">Melhorar seleção</button></div></details>`);
    const exportButton = root.querySelector('#renderAll'); if (exportButton && !project.isRendering) exportButton.textContent = 'Exportar cortes';
    layout.insertAdjacentHTML('afterbegin', toolRail());
    layout.insertAdjacentHTML('afterbegin', mediaBin(project));
    layout.insertAdjacentHTML('beforeend', timelineDock(project));
    bindWorkspace(project);
    if (activeClipId) selectClip(project, activeClipId);
  }

  function toolRail() {
    return `<nav class="cc-tool-rail" aria-label="Ferramentas principais"><div class="cc-editor-mark">AJ</div><button type="button" data-cc-mode="media"><b>⌂</b><span>Mídia</span></button><button class="active" type="button" data-cc-mode="cut"><b>⌁</b><span>Editar</span></button><button type="button" data-cc-mode="captions"><b>T</b><span>Legendas</span></button><button type="button" data-cc-mode="visual"><b>◐</b><span>Visual</span></button><button type="button" data-cc-mode="brand"><b>◇</b><span>Marca</span></button><button type="button" data-cc-mode="details"><b>•••</b><span>Detalhes</span></button></nav>`;
  }

  function mediaBin(project) {
    return `<aside class="cc-media-bin"><header><span class="cc-panel-icon">＋</span><div><strong>Mídia</strong><small>${project.clips.length} cortes</small></div></header><nav><button class="active" type="button">Cortes</button><button type="button" data-cc-source>Original</button></nav><div class="cc-clip-assets">${project.clips.map((clip, index) => `<button type="button" class="cc-asset" data-cc-clip="${clip.id}"><span class="cc-asset-thumb">${clip.coverPath ? `<img src="/api/projects/${project.id}/assets/${clip.coverPath}" alt="">` : '<i>▶</i>'}<em>${index + 1}</em></span><span><strong>${escapeHtml(clip.title)}</strong><small>${time(clip.end - clip.start)} · ${Math.round(clip.score)} pts</small></span></button>`).join('')}</div></aside>`;
  }

  function timelineDock(project) {
    const duration = Math.max(1, project.duration || Math.max(...project.clips.map(clip => clip.end)));
    return `<section class="cc-timeline-dock"><header><div class="cc-timeline-tools"><button type="button" data-cc-action="split" title="Dividir">✂</button><button type="button" data-cc-action="duplicate" title="Duplicar">▣</button><button type="button" data-cc-action="delete" title="Excluir corte">⌫</button><span></span><button type="button" data-cc-zoom="out">−</button><b>Timeline</b><button type="button" data-cc-zoom="in">＋</button></div><output id="ccTimelineClock">00:00:00</output></header><div class="cc-timeline-scroll"><div class="cc-timeline-content" style="--cc-zoom:1"><div class="cc-ruler">${Array.from({ length: 13 }, (_, index) => `<i style="left:${index / 12 * 100}%"><span>${time(duration * index / 12)}</span></i>`).join('')}</div><div class="cc-track-label"><b>V1</b><span>Vídeo</span></div><div class="cc-video-track">${project.clips.map(clip => `<button type="button" data-cc-track="${clip.id}" style="left:${clip.start / duration * 100}%;width:${Math.max(.8, (clip.end - clip.start) / duration * 100)}%"><span>${escapeHtml(clip.title)}</span></button>`).join('')}</div><div class="cc-track-label cc-audio-label"><b>A1</b><span>Áudio</span></div><div class="cc-audio-track" id="ccAudioWave">${Array.from({ length: 180 }, (_, i) => `<i style="height:${18 + (i * 29 % 68)}%"></i>`).join('')}</div><div class="cc-playhead" id="ccPlayhead"><i></i></div></div></div><div class="cc-trim-controls"><label>Entrada <input id="ccTrimStart" type="number" step=".1"></label><div class="cc-trim-range"><input id="ccRangeStart" type="range" min="0" max="${duration}" step=".1"><input id="ccRangeEnd" type="range" min="0" max="${duration}" step=".1"></div><label>Saída <input id="ccTrimEnd" type="number" step=".1"></label><strong id="ccTrimDuration">00:00</strong></div></section>`;
  }

  function bindWorkspace(project) {
    document.querySelectorAll('[data-cc-clip],[data-cc-track]').forEach(button => button.onclick = () => { selectClip(project, button.dataset.ccClip || button.dataset.ccTrack); if(button.dataset.ccClip) document.querySelector('[data-cc-mode="cut"]')?.click(); });
    document.querySelectorAll('[data-cc-mode]').forEach(button => button.onclick = () => {
      const mediaMode = button.dataset.ccMode === 'media';
      document.querySelector('.cc-editor-grid')?.classList.toggle('cc-show-media', mediaMode);
      if (!mediaMode) document.querySelector(`.clip-card[data-clip="${activeClipId}"] [data-edit-mode="${button.dataset.ccMode}"]`)?.click();
      document.querySelectorAll('[data-cc-mode]').forEach(item => item.classList.toggle('active', item === button));
    });
    document.querySelector('#ccClipPicker').onchange = event => selectClip(project, event.target.value);
    document.querySelectorAll('[data-cc-forward]').forEach(button => button.onclick = () => { document.querySelector(`#${button.dataset.ccForward}`)?.click(); button.closest('details').open = false; });
    document.querySelector('[data-cc-source]').onclick = () => switchEditorTab('source');
    document.querySelectorAll('[data-cc-zoom]').forEach(button => button.onclick = () => { timelineZoom = Math.max(1, Math.min(5, timelineZoom + (button.dataset.ccZoom === 'in' ? .5 : -.5))); document.querySelector('.cc-timeline-content')?.style.setProperty('--cc-zoom', timelineZoom); });
    document.querySelector('[data-cc-action="split"]').onclick = () => activeClipId && splitClip(project.id, activeClipId);
    document.querySelector('[data-cc-action="duplicate"]').onclick = () => activeClipId && duplicateClip(project.id, activeClipId);
    document.querySelector('[data-cc-action="delete"]').onclick = () => activeClipId && deleteClip(project.id, activeClipId);
    ['ccRangeStart', 'ccRangeEnd', 'ccTrimStart', 'ccTrimEnd'].forEach(id => document.querySelector(`#${id}`).oninput = event => updateTrim(event.target.id));
    const observer = new MutationObserver(() => bindPreviewClock());
    observer.observe(document.querySelector('#preview'), { childList: true, subtree: true });
    bindPreviewClock();
  }

  function syncWorkspace(project, id) {
    const root = document.querySelector('.cc-workspace-v2'); if (!root) return;
    const clip = project.clips.find(item => item.id === id); if (!clip) return;
    root.querySelectorAll('.clip-card').forEach(card => { const active = card.dataset.clip === id; card.classList.toggle('active', active); card.classList.toggle('cc-hidden-card', !active); });
    root.querySelectorAll('[data-cc-clip],[data-cc-track]').forEach(button => button.classList.toggle('active', (button.dataset.ccClip || button.dataset.ccTrack) === id));
    root.querySelector('#ccInspectorTitle').textContent = clip.title;
    root.querySelector('#ccInspectorScore').textContent = `${Math.round(clip.score)} pts`;
    root.querySelector('#ccClipPicker').value = id;
    const max = Math.max(1, project.duration || clip.end);
    const clipDuration = Math.max(.1, clip.end - clip.start);
    root.querySelectorAll('[data-cc-track]').forEach(button => { if (button.dataset.ccTrack === id) { button.style.left = '0'; button.style.width = '100%'; button.querySelector('span').textContent = clip.title; } });
    root.querySelectorAll('.cc-ruler span').forEach((label, index, all) => label.textContent = time(clipDuration * index / Math.max(1, all.length - 1)));
    setValue('ccTrimStart', clip.start); setValue('ccTrimEnd', clip.end); setValue('ccRangeStart', clip.start); setValue('ccRangeEnd', clip.end);
    ['ccRangeStart','ccRangeEnd'].forEach(field => root.querySelector(`#${field}`).max = max);
    root.querySelector('#ccTrimDuration').textContent = time(clipDuration);
    const card = root.querySelector(`.clip-card[data-clip="${id}"]`);
    ['start','end'].forEach(name => { const input=card?.querySelector(`[name="${name}"]`); if(input) input.value=(+input.value).toFixed(2); });
    root.querySelector('.cc-timeline-content')?.style.setProperty('--cc-zoom', timelineZoom);
    bindPreviewClock();
  }

  function setValue(id, value) { const input = document.querySelector(`#${id}`); if (input) input.value = (+value).toFixed(1); }

  function updateTrim(source) {
    const card = document.querySelector(`.clip-card[data-clip="${activeClipId}"]`); if (!card) return;
    const startControl = document.querySelector(source.includes('Start') ? '#ccTrimStart' : '#ccTrimEnd');
    const rangeControl = document.querySelector(source.includes('Start') ? '#ccRangeStart' : '#ccRangeEnd');
    const input = document.querySelector(`#${source}`); const value = +input.value;
    startControl.value = value.toFixed(1); rangeControl.value = value.toFixed(1);
    let start = +document.querySelector('#ccTrimStart').value, end = +document.querySelector('#ccTrimEnd').value;
    if (end - start < 1) { if (source.includes('Start')) start = end - 1; else end = start + 1; }
    start = Math.max(0, start); end = Math.max(start + 1, end);
    setValue('ccTrimStart', start); setValue('ccTrimEnd', end); setValue('ccRangeStart', start); setValue('ccRangeEnd', end);
    const originalStart = card.querySelector('[name="start"]'), originalEnd = card.querySelector('[name="end"]');
    if (originalStart) originalStart.value = start.toFixed(1); if (originalEnd) originalEnd.value = end.toFixed(1);
    const timelineStart = card.querySelector('[name="timelineStart"]'), timelineEnd = card.querySelector('[name="timelineEnd"]');
    if (timelineStart) timelineStart.value = start; if (timelineEnd) timelineEnd.value = end;
    document.querySelector('#ccTrimDuration').textContent = time(end - start);
    originalStart?.dispatchEvent(new Event('input', { bubbles: true }));
  }

  function bindPreviewClock() {
    const video = document.querySelector('#preview video'); if (!video || video.dataset.ccClockBound) return;
    video.dataset.ccClockBound = 'true';
    video.addEventListener('timeupdate', () => {
      const clip = editorProject?.clips.find(item => item.id === activeClipId); if (!clip) return;
      const relative = video.dataset.sourcePreview ? Math.max(0, video.currentTime - clip.start) : video.currentTime;
      const duration = Math.max(.1, clip.end - clip.start); const progress = Math.min(100, relative / duration * 100);
      document.querySelector('#ccPlayhead')?.style.setProperty('left', `${progress}%`);
      const clock = document.querySelector('#ccTimelineClock'); if (clock) clock.textContent = `${time(relative)} / ${time(duration)}`;
    });
  }
})();
