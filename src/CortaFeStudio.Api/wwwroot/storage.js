const bytesLabel = bytes => {
  const value = Number(bytes || 0);
  if (value >= 1024 ** 3) return `${(value / 1024 ** 3).toFixed(2)} GB`;
  if (value >= 1024 ** 2) return `${(value / 1024 ** 2).toFixed(1)} MB`;
  if (value >= 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${value} B`;
};

function youtubeVideoId(value) {
  try {
    const url = new URL(value);
    const host = url.hostname.toLowerCase();
    if (host === 'youtu.be') return url.pathname.split('/').filter(Boolean)[0] || null;
    if (!['youtube.com', 'www.youtube.com', 'm.youtube.com'].includes(host)) return null;
    if (url.pathname === '/watch') return url.searchParams.get('v');
    const parts = url.pathname.split('/').filter(Boolean);
    if (['shorts', 'live', 'embed'].includes(parts[0])) return parts[1] || null;
  } catch {}
  return null;
}

async function cleanupProject(projectId) {
  const deleteSource = confirm(
    'Deseja apagar também o vídeo original para liberar mais espaço?\n\n' +
    'OK = apagar o vídeo e manter o histórico.\n' +
    'Cancelar = limpar somente arquivos temporários.'
  );

  try {
    const result = await api(`/api/projects/${projectId}/cleanup`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ deleteSource })
    });
    toast(`${bytesLabel(result.freedBytes)} liberados · histórico preservado`);
    openProject(projectId);
  } catch (error) {
    toast(error.message);
  }
}

async function archiveProject(projectId) {
  if (!confirm('Arquivar este projeto e removê-lo da biblioteca principal? O histórico será mantido.')) return;
  try {
    await api(`/api/projects/${projectId}/archive`, { method: 'POST' });
    toast('Projeto arquivado no histórico');
    home();
  } catch (error) {
    toast(error.message);
  }
}

function historyCards(items) {
  if (!items.length) return '<div class="empty">Nenhum projeto encontrado no histórico.</div>';

  return items.map(item => {
    const isYouTube = item.sourceKind === 'youTube';
    const status = item.sourceAvailable
      ? `Original disponível · ${bytesLabel(item.sourceBytes)}`
      : 'Original removido · histórico preservado';
    const statusClass = item.sourceAvailable ? 'text-success' : 'text-warning';
    const sourceAction = isYouTube && item.source
      ? `<a class="btn btn-sm btn-outline-light" href="${escapeHtml(item.source)}" target="_blank" rel="noopener">Abrir no YouTube ↗</a>`
      : '';

    return `<article class="project-card history-card" data-history-id="${item.id}">
      <div class="d-flex justify-content-between gap-3 align-items-start">
        <span class="eyebrow">${isYouTube ? 'YOUTUBE' : 'ARQUIVO'} · ${escapeHtml(item.contentType || '')}</span>
        ${item.archived ? '<span class="badge text-bg-secondary">ARQUIVADO</span>' : ''}
      </div>
      <h3>${escapeHtml(item.name)}</h3>
      <p class="${statusClass}">${status}</p>
      <div class="text-secondary small mb-3">
        ${item.clipCount || 0} cortes · ${bytesLabel(item.bytes)} armazenados<br>
        Processado em ${new Date(item.createdAt).toLocaleString('pt-BR')}
      </div>
      <div class="d-flex gap-2 flex-wrap">
        <button class="btn btn-sm btn-gold" onclick="openProject('${item.id}')">Abrir projeto</button>
        ${sourceAction}
        ${item.sourceAvailable ? `<button class="btn btn-sm btn-outline-warning" onclick="cleanupProject('${item.id}')">Liberar espaço</button>` : ''}
      </div>
    </article>`;
  }).join('');
}

async function historyView() {
  clearInterval(poller);
  const report = await api('/api/storage');
  const items = report.projects || [];

  app.innerHTML = `<div class="workspace-title">
    <button class="back-link" onclick="home()">← Biblioteca</button>
    <span class="eyebrow">HISTÓRICO PERMANENTE</span>
    <h1>Vídeos já processados.</h1>
    <p class="text-secondary">Apague os arquivos pesados sem perder nome, link, data ou referência do projeto.</p>
  </div>
  <div class="studio-panel p-3 p-lg-4 mb-4">
    <div class="row g-3 align-items-center">
      <div class="col-lg-8"><input id="historySearch" class="form-control" placeholder="Buscar por nome, link ou tipo de conteúdo"></div>
      <div class="col-lg-4 text-lg-end"><strong>${items.length}</strong> projetos · <strong>${bytesLabel(report.totalBytes)}</strong> no Amado Jesus Studio</div>
    </div>
  </div>
  <div id="historyProjects" class="project-grid">${historyCards(items)}</div>`;

  const search = document.querySelector('#historySearch');
  search.oninput = () => {
    const term = search.value.trim().toLowerCase();
    const filtered = !term ? items : items.filter(item =>
      `${item.name} ${item.source} ${item.contentType} ${item.status}`.toLowerCase().includes(term));
    document.querySelector('#historyProjects').innerHTML = historyCards(filtered);
  };
}

function installHistoryButton() {
  const head = document.querySelector('#projects')?.previousElementSibling;
  if (!head || head.querySelector('[data-history]')) return;
  const refresh = head.querySelector('[data-action="refresh"]');
  if (!refresh) return;
  refresh.insertAdjacentHTML('beforebegin', '<button class="btn btn-outline-light me-2" data-history>Histórico</button>');
  head.querySelector('[data-history]').onclick = historyView;
}

const homeWithHistory = home;
home = async function () {
  await homeWithHistory();
  installHistoryButton();
};

const submitProjectWithHistory = submitProject;
submitProject = async function (event) {
  event.preventDefault();

  if (source === 'url') {
    const form = new FormData(event.target);
    const urls = String(form.get('url') || '')
      .split(/\r?\n/)
      .map(value => value.trim())
      .filter(Boolean);

    if (urls.length) {
      try {
        const report = await api('/api/storage');
        const history = report.projects || [];
        const previousById = new Map(
          history
            .filter(item => item.sourceKind === 'youTube')
            .map(item => [youtubeVideoId(item.source), item])
            .filter(([id]) => id)
        );

        const duplicates = urls
          .map(url => ({ url, id: youtubeVideoId(url) }))
          .map(entry => ({ ...entry, previous: entry.id ? previousById.get(entry.id) : null }))
          .filter(entry => entry.previous);

        if (duplicates.length) {
          const names = duplicates
            .slice(0, 5)
            .map(entry => `• ${entry.previous.name}`)
            .join('\n');
          const more = duplicates.length > 5 ? `\n• +${duplicates.length - 5} outro(s)` : '';
          const proceed = confirm(
            `${duplicates.length === 1 ? 'Este vídeo já foi processado' : `${duplicates.length} vídeos já foram processados`}:\n\n` +
            names + more +
            '\n\nDeseja processar novamente mesmo assim?'
          );
          if (!proceed) {
            toast('Envio cancelado · consulte o Histórico');
            return;
          }
        }
      } catch {
        // O histórico nunca deve impedir a criação de um novo projeto.
      }
    }
  }

  return submitProjectWithHistory(event);
};

const renderProjectWithStorage = renderProject;
renderProject = function (project) {
  renderProjectWithStorage(project);
  if (project.status !== 'ready') return;
  const actions = document.querySelector('#projectView .section-head .d-flex');
  if (!actions) return;
  actions.insertAdjacentHTML('afterbegin', `<div class="dropdown"><button class="btn btn-outline-secondary dropdown-toggle" data-bs-toggle="dropdown">Armazenamento</button><ul class="dropdown-menu dropdown-menu-dark"><li><button class="dropdown-item" onclick="cleanupProject('${project.id}')">Liberar espaço mantendo histórico</button></li><li><button class="dropdown-item" onclick="archiveProject('${project.id}')">Arquivar no histórico</button></li></ul></div>`);
};

setTimeout(installHistoryButton, 500);
