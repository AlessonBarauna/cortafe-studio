(function () {
  const cardBase = clipCard;
  clipCard = function (project, clip, index) {
    const labels = { draft: 'Rascunho', ready: 'Pronto', scheduled: 'Programado', published: 'Publicado', discarded: 'Descartado' };
    const options = Object.entries(labels).map(([value, label]) => `<option value="${value}" ${clip.tikTokWorkflowStatus === value ? 'selected' : ''}>${label}</option>`).join('');
    const workflow = `<div class="tiktok-workflow" onclick="event.stopPropagation()"><span>Fluxo TikTok</span><select class="form-select form-select-sm" onchange="updateTikTokWorkflow('${project.id}','${clip.id}',this.value)">${options}</select>${clip.tikTokPublishedAt ? `<small>Publicado em ${new Date(clip.tikTokPublishedAt).toLocaleDateString('pt-BR')}</small>` : ''}</div>`;
    return cardBase(project, clip, index).replace('</article>', workflow + '</article>');
  };
})();

async function updateTikTokWorkflow(projectId, clipId, status) {
  try { await api(`/api/projects/${projectId}/clips/${clipId}/tiktok-workflow`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ status }) }); const clip = current.clips.find(item => item.id === clipId); if (clip) clip.tikTokWorkflowStatus = status; toast(`TikTok: ${status === 'published' ? 'marcado como publicado' : 'status atualizado'}`); }
  catch (error) { toast(error.message); }
}
