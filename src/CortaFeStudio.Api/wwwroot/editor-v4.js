(function () {
  let activeMode = 'cut';
  let activeClip = null;

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

  document.addEventListener('pointerdown', event => {
    const lane = event.target.closest('.aj-editor-v4 .cc-video-track,.aj-editor-v4 .cc-audio-track');
    const video = document.querySelector('#preview video');
    if (!lane || !video || event.target.closest('[data-cc-track]')) return;
    const box = lane.getBoundingClientRect();
    const ratio = Math.max(0, Math.min(1, (event.clientX - box.left) / box.width));
    const clip = current?.clips.find(item => item.id === activeClip);
    if (!clip) return;
    const duration = clip.end - clip.start;
    video.currentTime = (video.dataset.sourcePreview ? clip.start : 0) + duration * ratio;
    const playhead = document.querySelector('#ccPlayhead');
    if (playhead) playhead.style.left = `${ratio * 100}%`;
  }, true);
})();
