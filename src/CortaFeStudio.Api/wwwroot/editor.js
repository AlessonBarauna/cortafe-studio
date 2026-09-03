const clipCardWithEditorial = clipCard;

clipCard = function (project, clip, index) {
  const option = (value, current, label) => `<option value="${value}" ${value === current ? 'selected' : ''}>${label}</option>`;
  const timestamp = clip.coverTimestamp ?? Math.min(clip.end, clip.start + 3);
  const speedControl = project.options.contentType === 'louvor' ? '' : `<label>Velocidade do corte<select class="form-select" name="playbackSpeed">${option('1', String(clip.playbackSpeed || 1), 'Normal · 1x')}${option('1.25', String(clip.playbackSpeed), 'Dinâmico · 1,25x')}${option('1.5', String(clip.playbackSpeed), 'Rápido · 1,50x')}</select><small class="text-secondary d-block mt-1">${Math.round((clip.end-clip.start)/(clip.playbackSpeed||1))} s no vídeo final</small></label>`;
  const controls = `<details class="editor-tools mt-3" onclick="event.stopPropagation()">
    <summary>Direção visual</summary>
    <div class="editor-grid mt-3">
      <label>Foco do vídeo<select class="form-select" name="cropFocus">${option('top', clip.cropFocus, 'Superior')}${option('center', clip.cropFocus || 'center', 'Central')}${option('bottom', clip.cropFocus, 'Inferior')}</select></label>
      <label>Composição<select class="form-select" name="layoutMode" onchange="toggleSplitControls(this)">${option('fill', clip.layoutMode || 'fill', 'Locutor em foco')}${option('split', clip.layoutMode, 'Dois participantes · tela dividida')}${option('blur', clip.layoutMode, 'Plano aberto · fundo desfocado')}</select></label>
      <label>Formato de saída<select class="form-select" name="outputPreset">${option('vertical', clip.outputPreset || 'vertical', 'Vertical · 1080×1920')}${option('portrait', clip.outputPreset, 'Feed retrato · 1080×1350')}${option('square', clip.outputPreset, 'Quadrado · 1080×1080')}${option('landscape', clip.outputPreset, 'Horizontal · 1920×1080')}</select></label>
      <label>Transições de cena<select class="form-select" name="transitionStyle">${option('smooth', clip.transitionStyle || 'smooth', 'Suave · contínua')}${option('editorial', clip.transitionStyle, 'Editorial · cortes elegantes')}${option('dynamic', clip.transitionStyle, 'Dinâmica · ritmo rápido')}</select><small class="text-secondary d-block mt-1">Usa as mudanças de cena detectadas no vídeo.</small></label>
      ${speedControl}
      <label class="form-check editor-check"><input class="form-check-input" type="checkbox" name="silenceTrimmingEnabled" ${clip.silenceTrimmingEnabled !== false ? 'checked' : ''}><span>Reduzir apenas pausas longas</span><small class="text-secondary d-block">Mantém a fala e remove silêncios seguros.</small></label>
      <label>Posição horizontal<input class="form-range" name="cropX" type="range" min="0" max="1" step=".01" value="${clip.cropX ?? .5}"></label>
      <label class="split-control ${clip.layoutMode === 'split' ? '' : 'd-none'}">Participante superior<input class="form-range" name="splitLeftX" type="range" min="0" max="1" step=".01" value="${clip.splitLeftX ?? .25}"></label>
      <label class="split-control ${clip.layoutMode === 'split' ? '' : 'd-none'}">Participante inferior<input class="form-range" name="splitRightX" type="range" min="0" max="1" step=".01" value="${clip.splitRightX ?? .75}"></label>
      <label>Estilo da legenda<select class="form-select" name="subtitleStyle">${option('impact', clip.subtitleStyle || 'impact', 'Impacto')}${option('clean', clip.subtitleStyle, 'Limpa')}${option('podcast', clip.subtitleStyle, 'Podcast')}${option('sermon', clip.subtitleStyle, 'Pregação')}${option('motivational', clip.subtitleStyle, 'Motivacional')}${option('minimal', clip.subtitleStyle, 'Minimalista')}${option('worship', clip.subtitleStyle, 'Louvor')}${option('bold', clip.subtitleStyle, 'Palco')}</select></label>
      <label>Posição na capa<select class="form-select" name="coverPosition">${option('top', clip.coverPosition, 'Superior')}${option('center', clip.coverPosition, 'Centro')}${option('bottom', clip.coverPosition || 'bottom', 'Inferior')}</select></label>
      <label>Cor de destaque<input class="form-control form-control-color" name="coverAccent" type="color" value="${escapeHtml(clip.coverAccent || '#C7A35A')}"></label>
      <label>Frame da capa (segundos)<input class="form-control" name="coverTimestamp" type="number" min="${clip.start}" max="${clip.end}" step=".1" value="${timestamp}"></label>
    </div>
    <div class="camera-keyframes mt-3"><div><strong>Câmera manual</strong><small>${clip.framingTrack?.length || 0} pontos na timeline</small></div><div class="d-flex gap-2 flex-wrap"><button type="button" class="btn btn-sm btn-outline-warning" onclick="addCameraKeyframe('${project.id}','${clip.id}',this)">+ Ponto no tempo atual</button><button type="button" class="btn btn-sm btn-outline-secondary" onclick="resetCameraKeyframes('${project.id}','${clip.id}')">Limpar pontos</button></div></div>
    <div class="d-flex gap-2 mt-3 flex-wrap"><button type="button" class="btn btn-sm btn-outline-warning" onclick="refreshCover('${project.id}','${clip.id}')">Atualizar capa</button><button type="button" class="btn btn-sm btn-outline-light" onclick="analyzeFraming('${project.id}','${clip.id}')">Detectar rosto</button>${index === 0 ? `<a class="btn btn-sm btn-outline-light" href="/api/projects/${project.id}/exports/project.json" download>Exportar projeto JSON</a>` : ''}</div>
  </details>`;
  return clipCardWithEditorial(project, clip, index).replace('<div class="d-flex gap-2 mt-3"><button class="btn btn-outline-light flex-fill" data-save>', controls + '<div class="d-flex gap-2 mt-3"><button class="btn btn-outline-light flex-fill" data-save>');
};

saveClip = async function (project, card) {
  const clip = project.clips.find(item => item.id === card.dataset.clip);
  const value = name => card.querySelector(`[name="${name}"]`)?.value;
  const body = {
    start: +value('start'), end: +value('end'), title: value('title'), coverText: value('coverText'),
    caption: value('caption'), approved: true, cropFocus: value('cropFocus'),
    subtitleStyle: value('subtitleStyle'), coverAccent: value('coverAccent'),
    coverPosition: value('coverPosition'), coverTimestamp: +value('coverTimestamp'), cropX: +value('cropX'), layoutMode: value('layoutMode'), splitLeftX: +value('splitLeftX'), splitRightX: +value('splitRightX'), outputPreset: value('outputPreset'), playbackSpeed: +(value('playbackSpeed') || 1), silenceTrimmingEnabled: card.querySelector('[name="silenceTrimmingEnabled"]')?.checked ?? true, transitionStyle: value('transitionStyle')
  };
  await api(`/api/projects/${project.id}/clips/${clip.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  Object.assign(clip, body);
  toast('Alterações salvas');
};

async function refreshCover(projectId, clipId) {
  const card = document.querySelector(`[data-clip="${clipId}"]`);
  try {
    await saveClip(current, card);
    toast('Compondo nova capa…');
    await api(`/api/projects/${projectId}/clips/${clipId}/cover`, { method: 'POST' });
    toast('Capa atualizada');
    openProject(projectId);
  } catch (error) { toast(error.message); }
}

async function analyzeFraming(projectId, clipId) { try { toast('Analisando rostos no trecho…'); await api(`/api/projects/${projectId}/clips/${clipId}/analyze-framing`, { method: 'POST' }); toast('Enquadramento ajustado'); openProject(projectId); } catch (error) { toast(error.message.includes('cv2') ? 'Instale o componente de visão pelo instalador do projeto' : error.message); } }

function toggleSplitControls(select) {
  select.closest('.editor-tools')?.querySelectorAll('.split-control').forEach(control => control.classList.toggle('d-none', select.value !== 'split'));
}

async function addCameraKeyframe(projectId, clipId, button) {
  const clip = current.clips.find(item => item.id === clipId), card = button.closest('.clip-card'), video = document.querySelector('#preview video');
  if (!clip || !video) return toast('Abra a prévia do corte antes de marcar a câmera');
  const relativeTime = Math.max(0, Math.min(clip.end - clip.start, video.dataset.sourcePreview ? video.currentTime - clip.start : video.currentTime));
  const x = +(card.querySelector('[name="cropX"]')?.value || clip.cropX || .5);
  const keyframes = [...(clip.framingTrack || []).filter(point => Math.abs(point.time - relativeTime) > .08), { time: relativeTime, x }].sort((a, b) => a.time - b.time);
  await saveFramingTrack(projectId, clip, keyframes);
  toast(`Câmera marcada em ${relativeTime.toFixed(1)} s`); openProject(projectId);
}

async function resetCameraKeyframes(projectId, clipId) {
  const clip = current.clips.find(item => item.id === clipId); if (!clip) return;
  await saveFramingTrack(projectId, clip, []);
  toast('Pontos manuais removidos'); openProject(projectId);
}

async function saveFramingTrack(projectId, clip, keyframes) {
  await api(`/api/projects/${projectId}/clips/${clip.id}/framing-track`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ keyframes }) });
  clip.framingTrack = keyframes; clip.renderOutdated = true;
}
