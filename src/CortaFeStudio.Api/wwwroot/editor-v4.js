(function () {
  let activeMode = 'cut';
  let activeClip = null;
  let timelineDrag = null;

  const renderBeforeV4 = renderProject;
  renderProject = function (project) {
    renderBeforeV4(project);
    if (project.status !== 'ready') return;
    requestAnimationFrame(() => mountEditorV4(project));
  };

  const selectBeforeV4 = selectClip;
  selectClip = function (project, id) {
    selectBeforeV4(project, id);
    activeClip = id;
    requestAnimationFrame(() => {
      decorateActiveClip(project, id);
      setEditorMode(activeMode, false);
    });
  };

  function mountEditorV4(project) {
    const root = document.querySelector('.cc-workspace-v2');
    if (!root) return;
    root.classList.add('aj-editor-v4');
    activeClip = document.querySelector('.clip-card.active')?.dataset.clip || project.clips[0]?.id || null;
    decorateRail();
    decorateTopbar(project);
    decorateTimeline();
    decorateTransport();
    observePreview();
    if (activeClip) decorateActiveClip(project, activeClip);
    setEditorMode(activeMode, false);
  }

  function decorateRail() {
    const labels = {
      media: ['▣', 'Mídia'], cut: ['✂', 'Corte'], captions: ['T', 'Legendas'],
      visual: ['◐', 'Ajustes'], brand: ['◇', 'Marca'], details: ['•••', 'Publicar']
    };
    document.querySelectorAll('.cc-tool-rail [data-cc-mode]').forEach(button => {
      const value = labels[button.dataset.ccMode];
      if (value) button.innerHTML = `<b>${value[0]}</b><span>${value[1]}</span>`;
      button.title = value?.[1] || button.dataset.ccMode;
    });
  }

  function decorateTopbar(project) {
    const heading = document.querySelector('.cc-workspace-v2>.section-head');
    if (!heading || heading.querySelector('.v4-project-state')) return;
    const rendered = project.clips.filter(clip => clip.videoPath && !clip.renderOutdated).length;
    heading.querySelector('h2')?.insertAdjacentHTML('afterend', `<span class="v4-project-state"><i></i>${rendered}/${project.clips.length} vídeos prontos</span>`);
  }

  function decorateTimeline() {
    const timeline = document.querySelector('.cc-timeline-dock');
    if (!timeline || timeline.querySelector('.v4-timeline-title')) return;
    timeline.querySelector('.cc-timeline-tools')?.insertAdjacentHTML('afterbegin', '<strong class="v4-timeline-title">LINHA DO TEMPO</strong>');
  }

  function decorateTransport() {
    const transport = document.querySelector('.cc-canvas-stage .transport');
    if (!transport || transport.querySelector('.v4-playback-tools')) return;
    transport.insertAdjacentHTML('beforeend', `<div class="v4-playback-tools"><output id="v4Timecode">00:00.00 / 00:00.00</output><button type="button" data-v4-mute title="Ativar ou silenciar áudio">🔊</button><input data-v4-volume type="range" min="0" max="1" step=".05" value="1" aria-label="Volume da prévia"><select data-v4-speed aria-label="Velocidade da prévia"><option value=".75">0,75x</option><option value="1" selected>1x</option><option value="1.25">1,25x</option><option value="1.5">1,50x</option><option value="2">2x</option></select></div>`);
    transport.querySelector('[data-v4-mute]').onclick = () => { const video = previewVideoV4(); if (!video) return; video.muted = !video.muted; syncTransport(video); };
    transport.querySelector('[data-v4-volume]').oninput = event => { const video = previewVideoV4(); if (!video) return; video.volume = +event.target.value; video.muted = video.volume === 0; syncTransport(video); };
    transport.querySelector('[data-v4-speed]').onchange = event => { const video = previewVideoV4(); if (video) video.playbackRate = +event.target.value; };
    bindVideoTransport();
  }

  function observePreview() {
    const preview = document.querySelector('#preview');
    if (!preview || preview.dataset.v4Observed) return;
    preview.dataset.v4Observed = 'true';
    new MutationObserver(() => bindVideoTransport()).observe(preview, { childList: true });
  }

  function previewVideoV4() { return document.querySelector('#preview video'); }
  function bindVideoTransport() {
    const video = previewVideoV4();
    if (!video || video.dataset.v4TransportBound) return;
    video.dataset.v4TransportBound = 'true';
    video.addEventListener('timeupdate', () => syncTransport(video));
    video.addEventListener('durationchange', () => syncTransport(video));
    video.addEventListener('volumechange', () => syncTransport(video));
    video.addEventListener('play', () => document.querySelector('.transport-play')?.classList.add('is-playing'));
    video.addEventListener('pause', () => document.querySelector('.transport-play')?.classList.remove('is-playing'));
    syncTransport(video);
  }

  function clipTiming(video) {
    const clip = current?.clips.find(item => item.id === activeClip);
    if (!clip) return { current: 0, duration: 0 };
    const duration = Math.max(.01, clip.end - clip.start);
    const currentTime = video.dataset.sourcePreview ? video.currentTime - clip.start : video.currentTime;
    return { current: Math.max(0, Math.min(duration, currentTime)), duration };
  }

  function preciseTime(seconds) { const value = Math.max(0, seconds || 0), minutes = Math.floor(value / 60), rest = value - minutes * 60; return `${String(minutes).padStart(2,'0')}:${rest.toFixed(2).padStart(5,'0')}`; }
  function syncTransport(video) {
    const timing = clipTiming(video), output = document.querySelector('#v4Timecode');
    if (output) output.textContent = `${preciseTime(timing.current)} / ${preciseTime(timing.duration)}`;
    const playhead = document.querySelector('#ccPlayhead');
    if (playhead) playhead.style.left = `${Math.max(0, Math.min(100, timing.current / timing.duration * 100))}%`;
    const mute = document.querySelector('[data-v4-mute]'); if (mute) mute.textContent = video.muted || video.volume === 0 ? '🔇' : '🔊';
    const volume = document.querySelector('[data-v4-volume]'); if (volume && document.activeElement !== volume) volume.value = video.muted ? 0 : video.volume;
  }

  function decorateActiveClip(project, id) {
    const clip = project.clips.find(item => item.id === id);
    const card = document.querySelector(`.clip-card[data-clip="${id}"]`);
    if (!clip || !card) return;
    card.dataset.editMode = activeMode;
    const head = document.querySelector('.cc-panel-head span');
    if (head) head.textContent = modeTitle(activeMode);
    document.querySelectorAll('.cc-asset').forEach(asset => asset.classList.toggle('active', asset.dataset.ccClip === id));
  }

  function modeTitle(mode) {
    return ({ cut: 'PROPRIEDADES DO CORTE', captions: 'LEGENDAS', visual: 'AJUSTES VISUAIS', brand: 'IDENTIDADE AMADO JESUS', details: 'EXPORTAÇÃO E PUBLICAÇÃO', media: 'BIBLIOTECA DE MÍDIA' })[mode] || 'PROPRIEDADES';
  }

  function setEditorMode(mode, announce = true) {
    if (mode === 'media') {
      document.querySelector('.cc-media-bin')?.classList.add('v4-focus');
      if (announce) toast('Biblioteca de mídia ativa');
      return;
    }
    activeMode = mode;
    document.querySelector('.cc-media-bin')?.classList.remove('v4-focus');
    document.querySelectorAll('.cc-tool-rail [data-cc-mode]').forEach(button => button.classList.toggle('active', button.dataset.ccMode === mode));
    const card = document.querySelector(`.clip-card[data-clip="${activeClip}"]`) || document.querySelector('.clip-card.active');
    if (card) {
      card.dataset.editMode = mode;
      card.querySelectorAll('[data-edit-mode]').forEach(button => button.classList.toggle('active', button.dataset.editMode === mode));
    }
    const head = document.querySelector('.cc-panel-head span');
    if (head) head.textContent = modeTitle(mode);
    if (announce) toast(`${modeTitle(mode)} aberto`);
  }

  document.addEventListener('click', event => {
    const tool = event.target.closest('.aj-editor-v4 .cc-tool-rail [data-cc-mode]');
    if (tool) {
      event.preventDefault();
      event.stopImmediatePropagation();
      setEditorMode(tool.dataset.ccMode);
      return;
    }
    const localTab = event.target.closest('.aj-editor-v4 .cc-properties-panel [data-edit-mode]');
    if (localTab) {
      event.preventDefault();
      event.stopImmediatePropagation();
      setEditorMode(localTab.dataset.editMode);
    }
  }, true);

  function seekTimeline(event, lane) {
    const video = previewVideoV4(), clip = current?.clips.find(item => item.id === activeClip);
    if (!video || !clip) return;
    const box = lane.getBoundingClientRect(), ratio = Math.max(0, Math.min(1, (event.clientX - box.left) / box.width));
    video.currentTime = (video.dataset.sourcePreview ? clip.start : 0) + (clip.end - clip.start) * ratio;
    syncTransport(video);
  }

  document.addEventListener('pointerdown', event => {
    const lane = event.target.closest('.aj-editor-v4 .cc-video-track,.aj-editor-v4 .cc-audio-track');
    if (!lane) return;
    timelineDrag = { lane, pointerId: event.pointerId };
    lane.setPointerCapture?.(event.pointerId);
    seekTimeline(event, lane);
  }, true);
  document.addEventListener('pointermove', event => { if (timelineDrag?.pointerId === event.pointerId) seekTimeline(event, timelineDrag.lane); }, true);
  document.addEventListener('pointerup', event => { if (timelineDrag?.pointerId === event.pointerId) timelineDrag = null; }, true);
  document.addEventListener('keydown', event => {
    if (!document.querySelector('.aj-editor-v4') || ['INPUT','TEXTAREA','SELECT'].includes(document.activeElement?.tagName)) return;
    const video = previewVideoV4(); if (!video) return;
    if (event.key.toLowerCase() === 'k') { event.preventDefault(); video.paused ? video.play() : video.pause(); }
    if (event.key.toLowerCase() === 'j') { event.preventDefault(); video.currentTime = Math.max(0, video.currentTime - 1); }
    if (event.key.toLowerCase() === 'l') { event.preventDefault(); video.currentTime = Math.min(video.duration || Infinity, video.currentTime + 1); }
  }, true);
})();
