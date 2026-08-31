(function () {
  const cardBase = clipCard;
  clipCard = function (project, clip, index) {
    const preview = `<button type="button" class="btn btn-sm btn-outline-info" data-fast-preview onclick="event.stopPropagation();fastPreview('${project.id}','${clip.id}',this)">Prévia rápida</button>`;
    return cardBase(project, clip, index).replace('<div class="d-flex gap-2 mt-3"><button class="btn btn-outline-light flex-fill" data-save>', `<div class="d-flex gap-2 mt-3 flex-wrap">${preview}</div><div class="d-flex gap-2 mt-3"><button class="btn btn-outline-light flex-fill" data-save>`);
  };

  const renderClipBase = renderClip;
  renderClip = async function (project, card) {
    const button = card.querySelector('[data-render]');
    button.insertAdjacentHTML('afterend', `<button class="btn btn-outline-danger" data-cancel-render>Cancelar</button>`);
    const cancel = card.querySelector('[data-cancel-render]');
    cancel.onclick = async event => { event.stopPropagation(); try { await api(`/api/projects/${project.id}/clips/${card.dataset.clip}/render/cancel`, { method: 'POST' }); toast('Cancelamento solicitado'); } catch (error) { toast(error.message); } };
    try { await renderClipBase(project, card); } finally { cancel?.remove(); }
  };
})();

async function fastPreview(projectId, clipId, button) {
  button.disabled = true; button.textContent = 'Gerando prévia…';
  try {
    const result = await api(`/api/projects/${projectId}/clips/${clipId}/preview`, { method: 'POST' });
    const preview = document.querySelector('#preview'); preview.innerHTML = `<video controls autoplay playsinline src="/api/projects/${projectId}/assets/${result.path}?v=${Date.now()}"></video>`;
    toast('Prévia leve pronta');
  } catch (error) { toast(error.message); }
  finally { button.disabled = false; button.textContent = 'Prévia rápida'; }
}
