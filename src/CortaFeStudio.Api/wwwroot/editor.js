const clipCardWithEditorial = clipCard;

clipCard = function (project, clip, index) {
  const option = (value, current, label) => `<option value="${value}" ${value === current ? 'selected' : ''}>${label}</option>`;
  const timestamp = clip.coverTimestamp ?? Math.min(clip.end, clip.start + 3);
  const controls = `<details class="editor-tools mt-3" onclick="event.stopPropagation()">
    <summary>Direção visual</summary>
    <div class="editor-grid mt-3">
      <label>Foco do vídeo<select class="form-select" name="cropFocus">${option('top', clip.cropFocus, 'Superior')}${option('center', clip.cropFocus || 'center', 'Central')}${option('bottom', clip.cropFocus, 'Inferior')}</select></label>
      <label>Composição<select class="form-select" name="layoutMode">${option('fill', clip.layoutMode || 'fill', 'Preencher 9:16')}${option('blur', clip.layoutMode, 'Fundo desfocado')}</select></label>
      <label>Posição horizontal<input class="form-range" name="cropX" type="range" min="0" max="1" step=".01" value="${clip.cropX ?? .5}"></label>
      <label>Estilo da legenda<select class="form-select" name="subtitleStyle">${option('impact', clip.subtitleStyle || 'impact', 'Impacto')}${option('clean', clip.subtitleStyle, 'Limpa')}${option('bold', clip.subtitleStyle, 'Palco')}</select></label>
      <label>Posição na capa<select class="form-select" name="coverPosition">${option('top', clip.coverPosition, 'Superior')}${option('center', clip.coverPosition, 'Centro')}${option('bottom', clip.coverPosition || 'bottom', 'Inferior')}</select></label>
      <label>Cor de destaque<input class="form-control form-control-color" name="coverAccent" type="color" value="${escapeHtml(clip.coverAccent || '#F0B44D')}"></label>
      <label>Frame da capa (segundos)<input class="form-control" name="coverTimestamp" type="number" min="${clip.start}" max="${clip.end}" step=".1" value="${timestamp}"></label>
    </div>
    <div class="d-flex gap-2 mt-3"><button type="button" class="btn btn-sm btn-outline-warning" onclick="refreshCover('${project.id}','${clip.id}')">Atualizar capa</button><button type="button" class="btn btn-sm btn-outline-light" onclick="analyzeFraming('${project.id}','${clip.id}')">Detectar rosto</button></div>
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
    coverPosition: value('coverPosition'), coverTimestamp: +value('coverTimestamp'), cropX: +value('cropX'), layoutMode: value('layoutMode')
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
