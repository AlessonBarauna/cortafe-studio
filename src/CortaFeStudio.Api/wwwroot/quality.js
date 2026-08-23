const clipCardBeforeQuality = clipCard;
clipCard = function(project, clip, index) {
  const report = clip.qualityReport;
  const status = report?.status || 'pending';
  const label = status === 'pass' ? 'PASS' : status === 'warning' ? 'ATENÇÃO' : status === 'blocked' ? 'BLOQUEADO' : 'VALIDAR';
  const quality = `<button class="quality-pill quality-${status}" type="button" onclick="event.stopPropagation();showQuality('${project.id}','${clip.id}')"><span>${label}</span><b>${report?.score ?? '—'}</b></button>`;
  return clipCardBeforeQuality(project, clip, index).replace('<div class="d-flex justify-content-between">', `<div class="d-flex justify-content-between align-items-start"><div>${quality}</div>`);
};

async function showQuality(projectId, clipId) {
  try {
    const report = await api(`/api/projects/${projectId}/clips/${clipId}/quality`);
    const icon = {pass:'✓',warning:'!',blocked:'×'};
    const content = `<div class="quality-modal-backdrop" onclick="this.remove()"><section class="quality-modal" onclick="event.stopPropagation()"><button class="quality-close" onclick="this.closest('.quality-modal-backdrop').remove()">×</button><span class="eyebrow">CONTROLE DE QUALIDADE</span><div class="quality-score quality-${report.status}"><strong>${report.score}</strong><span>${report.status === 'pass' ? 'APROVADO' : report.status === 'warning' ? 'COM AVISOS' : 'PUBLICAÇÃO BLOQUEADA'}</span></div><div class="quality-checks">${report.checks.map(check=>`<div class="quality-check quality-${check.status}"><i>${icon[check.status]}</i><div><b>${escapeHtml(check.label)}</b><small>${escapeHtml(check.detail)}</small></div></div>`).join('')}</div>${report.canAutoRepair?`<button class="btn btn-gold w-100 mt-3" onclick="repairQuality('${projectId}','${clipId}',this)">Corrigir e renderizar novamente</button>`:''}</section></div>`;
    document.body.insertAdjacentHTML('beforeend', content);
  } catch(error) { toast(error.message); }
}

async function repairQuality(projectId, clipId, button) {
  button.disabled=true; button.textContent='Reprocessando…';
  try { const report=await api(`/api/projects/${projectId}/clips/${clipId}/quality/repair`,{method:'POST'}); toast(`Quality Gate: ${report.score} pontos`); document.querySelector('.quality-modal-backdrop')?.remove(); openProject(projectId); }
  catch(error){toast(error.message);button.disabled=false;button.textContent='Corrigir e renderizar novamente'}
}
