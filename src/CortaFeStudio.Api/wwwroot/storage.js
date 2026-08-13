async function cleanupProject(projectId) {
  const deleteSource = confirm('Também apagar o vídeo original? Faça isso somente se os cortes finais já estiverem renderizados.');
  try { const result = await api(`/api/projects/${projectId}/cleanup`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ deleteSource }) }); toast(`${(result.freedBytes / 1024 / 1024).toFixed(1)} MB liberados`); openProject(projectId); } catch (error) { toast(error.message); }
}
async function archiveProject(projectId) { if (!confirm('Arquivar este projeto e removê-lo da biblioteca principal?')) return; try { await api(`/api/projects/${projectId}/archive`, { method: 'POST' }); toast('Projeto arquivado'); home(); } catch (error) { toast(error.message); } }

const renderProjectWithStorage = renderProject;
renderProject = function (project) {
  renderProjectWithStorage(project); if (project.status !== 'ready') return;
  const actions = document.querySelector('#projectView .section-head .d-flex'); if (!actions) return;
  actions.insertAdjacentHTML('afterbegin', `<div class="dropdown"><button class="btn btn-outline-secondary dropdown-toggle" data-bs-toggle="dropdown">Armazenamento</button><ul class="dropdown-menu dropdown-menu-dark"><li><button class="dropdown-item" onclick="cleanupProject('${project.id}')">Limpar temporários</button></li><li><button class="dropdown-item" onclick="archiveProject('${project.id}')">Arquivar projeto</button></li></ul></div>`);
};
