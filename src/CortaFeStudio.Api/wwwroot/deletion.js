const homeBeforeDeletion = home;
home = async function () {
  await homeBeforeDeletion();
  decorateProjectCards();
};

function decorateProjectCards() {
  document.querySelectorAll('.project-card:not(:has(.card-delete))').forEach(card => {
    const button = document.createElement('button');
    button.type = 'button'; button.className = 'card-delete'; button.textContent = '×';
    button.title = 'Excluir projeto'; button.setAttribute('aria-label', 'Excluir projeto');
    button.onclick = event => { event.stopPropagation(); deleteProject(card.dataset.id); };
    card.prepend(button);
  });
}

new MutationObserver(decorateProjectCards).observe(app, { childList: true, subtree: true });
decorateProjectCards();

const clipCardBeforeDeletion = clipCard;
clipCard = function (project, clip, index) {
  const opening = `<article class="clip-card" data-clip="${clip.id}">`;
  const button = `<button type="button" class="card-delete" title="Excluir corte" aria-label="Excluir corte" onclick="event.stopPropagation();deleteClip('${project.id}','${clip.id}')">×</button>`;
  return clipCardBeforeDeletion(project, clip, index).replace(opening, opening + button);
};

async function deleteProject(projectId) {
  const project = projects.find(item => item.id === projectId);
  if (!confirm(`Excluir “${project?.name || 'este projeto'}” e todos os vídeos gerados? Esta ação não pode ser desfeita.`)) return;
  try { await api(`/api/projects/${projectId}`, { method: 'DELETE' }); toast('Projeto excluído'); await home(); }
  catch (error) { toast(error.message); }
}

async function deleteClip(projectId, clipId) {
  const clip = current?.clips.find(item => item.id === clipId);
  if (!confirm(`Excluir o corte “${clip?.title || 'selecionado'}” e seu vídeo gerado?`)) return;
  try { await api(`/api/projects/${projectId}/clips/${clipId}`, { method: 'DELETE' }); toast('Corte excluído'); await openProject(projectId); }
  catch (error) { toast(error.message); }
}
