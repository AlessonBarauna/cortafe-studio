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
    const toolName = { ffmpeg: 'FFmpeg', ffprobe: 'FFprobe', ytDlp: 'yt-dlp', python: 'Python', node: 'Node.js para YouTube', ollama: 'Ollama', transcriber: 'Transcritor' };
    host.innerHTML = `<div class="diagnostic-grid">
      <article><span>PROJETOS</span><strong>${data.projects.total}</strong><p>${data.projects.ready} prontos · ${data.projects.processing} processando · ${data.projects.failed} com falha</p></article>
      <article><span>ARMAZENAMENTO</span><strong>${data.disk.freeGb} GB</strong><p>livres · ${data.disk.storageGb} GB usados pelo estúdio</p></article>
      <article><span>PROCESSAMENTO</span><strong>${data.runtime.processors}</strong><p>núcleos · ${data.runtime.memoryMb} MB disponíveis</p></article>
    </div>
    <div class="studio-panel p-4 mt-4"><span class="eyebrow">FERRAMENTAS LOCAIS</span><div class="tool-grid mt-3">${Object.entries(data.tools).map(([name, state]) => `<div class="tool-row"><i class="${state.available === false ? 'off' : ''}"></i><strong>${toolName[name] || name}</strong><small>${state.version || (state === true ? 'disponível' : state.error || 'instalado')}</small></div>`).join('')}</div></div>
    <div class="studio-panel p-4 mt-4"><span class="eyebrow">AVISOS</span>${data.warnings.length ? data.warnings.map(warning => `<p class="alert alert-warning mt-3 mb-0">${escapeHtml(warning)}</p>`).join('') : '<p class="text-success mt-3 mb-0">Tudo pronto para processar.</p>'}</div>`;
  } catch (error) { host.innerHTML = `<div class="alert alert-danger">${escapeHtml(error.message)}</div>`; }
}
