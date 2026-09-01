(function () {
  const baseHome = home;
  home = async function () { await baseHome(); installRetentionButton(); };

  function installRetentionButton() {
    const toolbar = document.querySelector('#projects')?.previousElementSibling;
    const refresh = toolbar?.querySelector('[data-action="refresh"]');
    if (!refresh || toolbar.querySelector('[data-retention]')) return;
    refresh.insertAdjacentHTML('beforebegin', '<button class="btn btn-outline-light me-2" data-retention>Retenção</button>');
    toolbar.querySelector('[data-retention]').onclick = openRetentionCenter;
  }

  async function openRetentionCenter() {
    clearInterval(poller);
    try {
      const preview = await api('/api/storage/retention/preview');
      render(preview);
    } catch (error) { toast(error.message); }
  }

  function render(preview) {
    const policy = preview.policy;
    app.innerHTML = `<div class="workspace-title"><button class="back-link" onclick="home()">← Biblioteca</button><span class="eyebrow">RETENÇÃO INTELIGENTE</span><h1>Espaço sob controle.</h1><p class="text-secondary">Remova automaticamente arquivos antigos sem tocar em favoritos, fixados ou trabalhos em andamento.</p></div>
      <section class="retention-layout"><form id="retentionForm" class="studio-panel retention-policy">
        <div class="retention-switch"><div><strong>Limpeza automática</strong><small>Executada diariamente quando o Studio estiver aberto.</small></div><div class="form-check form-switch"><input class="form-check-input" type="checkbox" name="enabled" ${policy.enabled ? 'checked' : ''}></div></div>
        <label class="form-label mt-4">Remover após</label><div class="input-group"><input class="form-control" name="retentionDays" type="number" min="1" max="365" value="${policy.retentionDays}"><span class="input-group-text">dias</span></div>
        <label class="form-label mt-4">Modo de limpeza</label><select class="form-select" name="mode"><option value="projectData" ${policy.mode === 'projectData' ? 'selected' : ''}>Seguro · excluir arquivos e manter projeto</option><option value="fullProject" ${policy.mode === 'fullProject' ? 'selected' : ''}>Definitivo · excluir projeto completo</option></select>
        <div class="retention-protection"><span>✓ Favoritos protegidos</span><span>✓ Fixados protegidos</span><span>✓ Processamentos protegidos</span></div>
        <button class="btn btn-gold w-100 mt-4" type="submit">Salvar política</button>
      </form><div class="studio-panel retention-preview"><div class="d-flex justify-content-between align-items-start gap-3"><div><span class="eyebrow">PRÉVIA SEGURA</span><h3>${preview.candidates.length} projeto(s) elegível(is)</h3><p>${bytesLabel(preview.estimatedBytes)} podem ser liberados agora.</p></div><button class="btn btn-outline-danger" id="runRetention" ${preview.candidates.length ? '' : 'disabled'}>Executar agora</button></div>
      <div class="retention-list">${preview.candidates.length ? preview.candidates.map(item => `<article><div><strong>${escapeHtml(item.name)}</strong><small>${new Date(item.referenceDate).toLocaleDateString('pt-BR')} · ${bytesLabel(item.estimatedBytes)}</small></div><span>${item.willDeleteProject ? 'Excluir projeto' : 'Preservar histórico'}</span></article>`).join('') : '<div class="empty">Nenhum projeto atingiu o prazo configurado.</div>'}</div></div></section>`;
    document.querySelector('#retentionForm').onsubmit = savePolicy;
    document.querySelector('#runRetention').onclick = () => runRetention(preview);
  }

  async function savePolicy(event) {
    event.preventDefault(); const form = new FormData(event.currentTarget); const mode = form.get('mode');
    if (mode === 'fullProject' && !confirm('O modo definitivo exclui projetos completos após o prazo. Favoritos e fixados continuam protegidos. Deseja salvar?')) return;
    try {
      await api('/api/storage/retention', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabled: form.get('enabled') === 'on', retentionDays: Number(form.get('retentionDays')), mode, protectFavorites: true, protectPinned: true }) });
      toast('Política de retenção salva'); openRetentionCenter();
    } catch (error) { toast(error.message); }
  }

  async function runRetention(preview) {
    const destructive = preview.policy.mode === 'fullProject';
    if (!confirm(`${destructive ? 'Excluir definitivamente' : 'Remover os arquivos pesados de'} ${preview.candidates.length} projeto(s) agora?`)) return;
    try { const result = await api('/api/storage/retention/run', { method: 'POST' }); toast(`${bytesLabel(result.freedBytes)} liberados de ${result.processed} projeto(s)`); openRetentionCenter(); } catch (error) { toast(error.message); }
  }

  setTimeout(installRetentionButton, 600);
})();
