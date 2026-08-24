const bindCommonBeforeDiagnostics = bindCommon;
bindCommon = function () {
  bindCommonBeforeDiagnostics();
  document.querySelectorAll('[data-action="diagnostics"]').forEach(button => button.onclick = diagnosticsCenter);
};
document.querySelectorAll('[data-action="diagnostics"]').forEach(button => button.onclick = diagnosticsCenter);

async function diagnosticsCenter() {
  clearInterval(poller);
  template('#diagnosticsTemplate');
  const host = document.querySelector('#diagnosticsView');
  try {
    const data = await api('/api/diagnostics');
    const encoder = await api('/api/render/encoder');
    const toolName = { ffmpeg: 'FFmpeg', ffprobe: 'FFprobe', ytDlp: 'yt-dlp', python: 'Python', node: 'Node.js para YouTube', ollama: 'Ollama', transcriber: 'Transcritor' };
    const updates = await api('/api/tools/updates').catch(() => null);
    host.innerHTML = `<div class="diagnostic-grid">
      <article><span>PROJETOS</span><strong>${data.projects.total}</strong><p>${data.projects.ready} prontos · ${data.projects.processing} processando · ${data.projects.failed} com falha</p></article>
      <article><span>ARMAZENAMENTO</span><strong>${data.disk.freeGb} GB</strong><p>livres · ${data.disk.storageGb} GB usados pelo estúdio</p></article>
      <article><span>PROCESSAMENTO</span><strong>${data.runtime.processors}</strong><p>núcleos · ${data.runtime.memoryMb} MB disponíveis</p></article>
    </div>
    <div class="studio-panel p-4 mt-4"><span class="eyebrow">FERRAMENTAS LOCAIS</span><div class="tool-grid mt-3">${Object.entries(data.tools).map(([name, state]) => `<div class="tool-row"><i class="${state.available === false ? 'off' : ''}"></i><strong>${toolName[name] || name}</strong><small>${state.version || (state === true ? 'disponível' : state.error || 'instalado')}</small></div>`).join('')}</div></div>
    <div class="studio-panel p-4 mt-4"><div class="d-flex justify-content-between align-items-center"><div><span class="eyebrow">ATUALIZAÇÕES</span><p class="mb-0 mt-2 text-secondary">yt-dlp instalado: ${escapeHtml(updates?.installed || 'indisponível')} · mais recente: ${escapeHtml(updates?.latest || 'não consultado')}</p></div>${updates?.updateAvailable ? '<button class="btn btn-gold" id="updateYtDlp">Atualizar yt-dlp</button>' : '<span class="badge text-bg-success">Atualizado</span>'}</div></div>
    <div class="studio-panel p-4 mt-4"><span class="eyebrow">AVISOS</span>${data.warnings.length ? data.warnings.map(warning => `<p class="alert alert-warning mt-3 mb-0">${escapeHtml(warning)}</p>`).join('') : '<p class="text-success mt-3 mb-0">Tudo pronto para processar.</p>'}</div>`;
    document.querySelector('#updateYtDlp')?.addEventListener('click', async event => { event.currentTarget.disabled = true; event.currentTarget.textContent = 'Atualizando…'; try { const result = await api('/api/tools/yt-dlp/update', { method: 'POST' }); toast(`yt-dlp atualizado: ${result.version}`); diagnosticsCenter(); } catch (error) { toast(error.message); event.currentTarget.disabled = false; } });
    host.insertAdjacentHTML('afterbegin', `<div class="row g-3 mb-4"><div class="col-lg-6"><div class="studio-panel p-4 h-100"><span class="eyebrow">MOTOR DE RENDERIZAÇÃO</span><h3 class="mt-2">${escapeHtml(encoder.name)}</h3><p class="mb-0 text-secondary">${encoder.hardwareAccelerated ? 'Aceleração por GPU testada e disponível.' : 'Fallback universal e confiável por CPU.'}</p></div></div><div class="col-lg-6"><div class="studio-panel p-4 h-100"><span class="eyebrow">CAPACIDADE SEGURA</span><h3 class="mt-2">${data.capacity.allowed?'Pronto para lote':'Espaço insuficiente'}</h3><p class="mb-0 text-secondary">${escapeHtml(data.capacity.message)}</p><button class="btn btn-sm btn-outline-light mt-3" id="cleanupTemporary">Limpar temporários seguros</button></div></div></div>`);
    document.querySelector('#cleanupTemporary')?.addEventListener('click',async event=>{event.currentTarget.disabled=true;try{const result=await api('/api/storage/temporary-cleanup',{method:'POST'});toast(`${(result.freedBytes/1024/1024).toFixed(1)} MB liberados`);diagnosticsCenter()}catch(error){toast(error.message)}});
  } catch (error) { host.innerHTML = `<div class="alert alert-danger">${escapeHtml(error.message)}</div>`; }
}
