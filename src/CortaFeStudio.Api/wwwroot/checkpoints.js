const renderProjectWithCheckpoints = renderProject;
renderProject = function (project) {
  renderProjectWithCheckpoints(project);
  const root = document.querySelector('#projectView'); if (!root || project.status !== 'ready') return;
  const steps = [['media', 'Mídia'], ['audio', 'Áudio'], ['transcript', 'Transcrição'], ['analysis', 'Análise']];
  const panel = document.createElement('section'); panel.className = 'checkpoint-panel';
  panel.innerHTML = `<div><span class="eyebrow">CHECKPOINTS</span><div class="checkpoint-steps">${steps.map(([key, label]) => `<button class="${(project.completedStages || []).includes(key) ? 'done' : ''}" data-stage="${key}"><i></i>${label}</button>`).join('')}</div></div><small>Etapas concluídas são reaproveitadas automaticamente. Clique em uma etapa para refazer a partir dela.</small>`;
  root.querySelector('.section-head')?.after(panel);
  panel.querySelectorAll('[data-stage]').forEach(button => button.onclick = async () => {
    if (!confirm(`Refazer o projeto a partir de ${button.textContent.trim()}? Os resultados posteriores serão substituídos.`)) return;
    try { await api(`/api/projects/${project.id}/restart-from`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ stage: button.dataset.stage }) }); toast('Reprocessamento colocado na fila'); openProject(project.id); } catch (error) { toast(error.message); }
  });
};
