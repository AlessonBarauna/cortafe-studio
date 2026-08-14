const clipCardBeforeRenderStatus = clipCard;
clipCard = function (project, clip, index) {
  const html = clipCardBeforeRenderStatus(project, clip, index);
  if (!clip.videoPath) return html;
  return html.replace(
    '<input class="form-control fs-5',
    '<div class="render-state"><span>✓</span><div><strong>Vídeo pronto</strong><small>Renderizado e disponível para visualizar ou baixar</small></div></div><input class="form-control fs-5'
  );
};

const renderProjectBeforeStatus = renderProject;
renderProject = function (project) {
  renderProjectBeforeStatus(project);
  if (project.status !== 'ready') return;
  const root = document.querySelector('#projectView');
  const layout = root?.querySelector('.clip-layout');
  if (!layout) return;
  const approved = project.clips.filter(clip => clip.approved).length;
  const rendered = project.clips.filter(clip => clip.approved && clip.videoPath).length;
  const complete = approved > 0 && rendered === approved;
  const summary = document.createElement('div');
  summary.className = `render-summary ${complete ? 'complete' : ''}`;
  summary.innerHTML = `<div class="render-summary-icon">${complete ? '✓' : '↗'}</div><div><span class="eyebrow">STATUS DA RENDERIZAÇÃO</span><strong>${complete ? 'Todos os vídeos estão prontos' : `${rendered} de ${approved} cortes renderizados`}</strong><small>${complete ? 'Você já pode visualizar, baixar ou publicar os cortes.' : 'Os cortes restantes ainda precisam ser renderizados.'}</small></div><span class="render-count">${rendered}/${approved}</span>`;
  layout.before(summary);
};

renderAll = async function (project) {
  const button = document.querySelector('#renderAll');
  const approved = project.clips.filter(clip => clip.approved).length;
  if (button) { button.disabled = true; button.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Renderizando…'; }
  toast(`Renderizando ${approved} ${approved === 1 ? 'corte' : 'cortes'}…`);
  try {
    await api(`/api/projects/${project.id}/render-all`, { method: 'POST' });
    toast(`✓ ${approved} ${approved === 1 ? 'vídeo pronto' : 'vídeos prontos para baixar'}`);
    await openProject(project.id);
  } catch (error) {
    toast(error.message);
    if (button) { button.disabled = false; button.textContent = 'Tentar renderizar novamente'; }
  }
};
