const socialCenterBase = socialCenter;

socialCenter = async function () {
  await socialCenterBase();
  const history = await api('/api/social/history');
  const host = document.querySelector('#publishHistory');
  if (!history.length) return;
  const names = { youTube: 'YouTube', instagram: 'Instagram', tikTok: 'TikTok' };
  const labels = { scheduled: 'Agendada', queued: 'Na fila', uploading: 'Enviando', published: 'Publicada', failed: 'Falhou' };
  host.innerHTML = history.map(item => `<article class="publication-row">
    <div><strong>${names[item.platform]}</strong> · ${labels[item.status] || item.status}<small>${item.scheduledAt ? `Programada para ${new Date(item.scheduledAt).toLocaleString('pt-BR')}` : new Date(item.createdAt).toLocaleString('pt-BR')}</small></div>
    ${item.externalUrl ? `<a class="btn btn-sm btn-outline-light" href="${item.externalUrl}" target="_blank" rel="noopener">Abrir</a>` : ''}
    ${item.status === 'failed' ? `<button class="btn btn-sm btn-outline-warning" onclick="retryPublication('${item.id}')">Tentar novamente</button>` : ''}
    ${item.error ? `<p class="text-danger mb-0">${escapeHtml(item.error)}</p>` : ''}
  </article>`).join('');
};

publishClip = async function (projectId, clipId, platform) {
  const card = document.querySelector(`[data-clip="${clipId}"]`);
  const title = card.querySelector('[name="title"]').value;
  const description = card.querySelector('[name="caption"]').value;
  const privacy = platform === 'tikTok' ? 'private' : (prompt('Visibilidade: private, unlisted ou public', 'private') || 'private');
  const schedule = prompt('Agendar? Informe data e hora (ex.: 15/08/2026 19:30) ou deixe vazio para publicar agora:', '');
  let publishAt = null;
  if (schedule) {
    const match = schedule.match(/^(\d{2})\/(\d{2})\/(\d{4})\s+(\d{2}):(\d{2})$/);
    if (!match) return toast('Use o formato DD/MM/AAAA HH:mm');
    publishAt = new Date(+match[3], +match[2] - 1, +match[1], +match[4], +match[5]).toISOString();
    if (new Date(publishAt) <= new Date()) return toast('Escolha uma data futura');
  }
  try {
    toast(schedule ? 'Salvando agendamento…' : `Enviando para ${platform}…`);
    const result = await api(`/api/projects/${projectId}/clips/${clipId}/publish`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ platform, title, description, privacy, publishAt }) });
    toast(result.status === 'scheduled' ? 'Publicação agendada' : 'Publicação concluída');
  } catch (error) { toast(error.message); }
};

async function retryPublication(id) {
  try { await api(`/api/social/publications/${id}/retry`, { method: 'POST' }); toast('Publicação colocada novamente na fila'); socialCenter(); }
  catch (error) { toast(error.message); }
}
