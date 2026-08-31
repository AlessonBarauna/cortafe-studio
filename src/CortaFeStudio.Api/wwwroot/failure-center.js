async function failureCenter() {
  clearInterval(poller);
  const failures = await api('/api/failures');
  app.innerHTML = `<div class="workspace-title"><button class="back-link" onclick="home()">← Biblioteca</button><span class="eyebrow">CONFIABILIDADE</span><h1>Central de<br>recuperação.</h1><p class="text-secondary">Tentativas, causas e pontos de retomada de cada processamento.</p></div>
    <div class="failure-center-list">${failures.length ? failures.map(failureCard).join('') : '<div class="empty">Nenhuma falha registrada.</div>'}</div>`;
}

function failureCard(project) {
  const latest = project.failures?.[0];
  const retrying = project.status === 'queued' && project.nextRetryAt;
  return `<article class="studio-panel failure-center-card"><div class="d-flex justify-content-between gap-3"><div><span class="eyebrow">${escapeHtml(project.failureCode || 'HISTÓRICO')}</span><h3>${escapeHtml(project.name)}</h3></div><span class="badge ${retrying ? 'text-bg-warning' : 'text-bg-danger'}">${retrying ? 'RETENTATIVA AUTOMÁTICA' : 'PRECISA DE ATENÇÃO'}</span></div>
    <p>${escapeHtml(project.error || project.stage)}</p><div class="failure-facts"><span>Tentativa <b>${project.attempt}</b></span><span>Retomada <b>${escapeHtml(project.lastCheckpoint || 'início')}</b></span>${project.nextRetryAt ? `<span>Próxima <b>${new Date(project.nextRetryAt).toLocaleTimeString('pt-BR')}</b></span>` : ''}</div>
    ${latest ? `<small>${new Date(latest.at).toLocaleString('pt-BR')} · ${escapeHtml(latest.stage)}</small>` : ''}<div class="mt-3 d-flex gap-2"><button class="btn btn-gold" onclick="openProject('${project.id}')">Abrir e corrigir</button><button class="btn btn-outline-light" onclick="retryFailure('${project.id}')" ${retrying ? 'disabled' : ''}>Tentar novamente</button></div></article>`;
}

async function retryFailure(projectId) {
  try { await api(`/api/projects/${projectId}/retry`, { method: 'POST' }); toast('Projeto recolocado na fila'); failureCenter(); }
  catch (error) { toast(error.message); }
}

const failureButton = document.createElement('button');
failureButton.className = 'btn btn-outline-light'; failureButton.textContent = 'Falhas'; failureButton.onclick = failureCenter;
document.querySelector('[data-action="diagnostics"]')?.before(failureButton);
