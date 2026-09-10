const socialCenterBase = socialCenter;
const publicationPlatformNames = { youTube: 'YouTube Shorts', instagram: 'Instagram Reels', tikTok: 'TikTok' };
const publicationStatusLabels = { scheduled: 'Agendada', queued: 'Na fila', uploading: 'Enviando', published: 'Publicada', failed: 'Falhou', cancelled: 'Cancelada' };

socialCenter = async function () {
  await socialCenterBase();
  const history = await api('/api/social/history');
  const host = document.querySelector('#publishHistory');
  if (!host) return;

  if (!history.length) {
    host.innerHTML = '<div class="publication-empty">Nenhuma publicação ainda. Abra um corte renderizado e escolha uma rede para começar.</div>';
    return;
  }

  const scheduled = history
    .filter(item => item.status === 'scheduled')
    .sort((a, b) => new Date(a.scheduledAt) - new Date(b.scheduledAt));
  const recent = history
    .filter(item => item.status !== 'scheduled')
    .slice(0, 20);

  host.innerHTML = `
    <div class="publication-dashboard">
      <div class="publication-summary">
        <div><span>AGENDADAS</span><strong>${scheduled.length}</strong></div>
        <div><span>PUBLICADAS</span><strong>${history.filter(item => item.status === 'published').length}</strong></div>
        <div><span>COM FALHA</span><strong>${history.filter(item => item.status === 'failed').length}</strong></div>
      </div>
      ${scheduled.length ? `
        <section class="publication-section">
          <div class="publication-section-head"><span class="eyebrow">PRÓXIMAS PUBLICAÇÕES</span><h4>Agenda</h4></div>
          <div class="publication-agenda">${scheduled.map(publicationCard).join('')}</div>
        </section>` : ''}
      <section class="publication-section">
        <div class="publication-section-head"><span class="eyebrow">ATIVIDADE RECENTE</span><h4>Histórico</h4></div>
        <div class="publication-agenda">${recent.map(publicationCard).join('')}</div>
      </section>
    </div>`;
};

function publicationCard(item) {
  const date = item.scheduledAt || item.publishedAt || item.createdAt;
  const when = date ? new Date(date).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : '';
  const description = String(item.description || '').replace(/\s+/g, ' ').trim();
  const excerpt = description.length > 150 ? `${description.slice(0, 147)}…` : description;
  return `<article class="publication-card status-${escapeHtml(item.status)}">
    <div class="publication-card-main">
      <div class="publication-card-top">
        <span class="publication-platform">${publicationPlatformNames[item.platform] || escapeHtml(item.platform)}</span>
        <span class="publication-status">${publicationStatusLabels[item.status] || escapeHtml(item.status)}</span>
      </div>
      <h5>${escapeHtml(item.title || 'Publicação sem título')}</h5>
      ${excerpt ? `<p>${escapeHtml(excerpt)}</p>` : ''}
      <small>${item.status === 'scheduled' ? 'Programada para' : 'Atualizada em'} ${when}</small>
      ${item.status === 'uploading' ? `<div class="progress mt-3"><div class="progress-bar" style="width:${item.progress || 0}%"></div></div>` : ''}
      ${item.error ? `<div class="publication-error">${escapeHtml(item.error)}</div>` : ''}
    </div>
    <div class="publication-card-actions">
      ${item.externalUrl ? `<a class="btn btn-sm btn-outline-light" href="${item.externalUrl}" target="_blank" rel="noopener">Abrir publicação</a>` : ''}
      ${item.status === 'failed' ? `<button class="btn btn-sm btn-outline-warning" onclick="retryPublication('${item.id}')">Tentar novamente</button>` : ''}
      ${item.platform === 'youTube' && item.externalId ? `<button class="btn btn-sm btn-outline-secondary" onclick="refreshPublication('${item.id}')">Atualizar estado</button>` : ''}
    </div>
  </article>`;
}

function normalizePublicationHashtags(value) {
  const tags = String(value || '')
    .split(/[\s,]+/)
    .map(tag => tag.trim())
    .filter(Boolean)
    .map(tag => tag.startsWith('#') ? tag : `#${tag}`)
    .filter((tag, index, all) => all.findIndex(item => item.toLowerCase() === tag.toLowerCase()) === index)
    .slice(0, 7);
  return tags;
}

function publicationDescriptionFromFields(caption, hashtags) {
  const cleanCaption = String(caption || '').trim();
  const tags = normalizePublicationHashtags(hashtags);
  return `${cleanCaption}${tags.length ? `\n\n${tags.join(' ')}` : ''}`.trim();
}

function ensurePublishModal() {
  let element = document.querySelector('#publishModal');
  if (element) return element;

  document.body.insertAdjacentHTML('beforeend', `
    <div class="modal fade" id="publishModal" tabindex="-1" aria-hidden="true">
      <div class="modal-dialog modal-lg modal-dialog-centered modal-dialog-scrollable">
        <div class="modal-content publish-modal-content">
          <div class="modal-header border-0 pb-0">
            <div><span class="eyebrow">PUBLICAÇÃO SOCIAL</span><h3 class="modal-title mt-1">Preparar publicação</h3></div>
            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Fechar"></button>
          </div>
          <form id="publishForm">
            <div class="modal-body">
              <input type="hidden" name="projectId"><input type="hidden" name="clipId">
              <div class="publish-block">
                <label class="form-label">Onde publicar</label>
                <div id="publishPlatforms" class="publish-platform-grid"></div>
              </div>
              <div class="publish-block">
                <label class="form-label">Título</label>
                <input class="form-control" name="title" maxlength="100" required>
                <small class="text-secondary">Use o título sugerido pelo CortaFé ou ajuste para esta publicação.</small>
              </div>
              <div class="publish-block">
                <label class="form-label">Legenda</label>
                <textarea class="form-control" name="caption" rows="5" maxlength="1800" required></textarea>
              </div>
              <div class="publish-block">
                <label class="form-label">Hashtags</label>
                <input class="form-control" name="hashtags" placeholder="#fe #jesus #pregacao">
                <small class="text-secondary">Até 7 hashtags. Elas serão anexadas automaticamente ao final da legenda.</small>
              </div>
              <div class="row g-3 publish-block">
                <div class="col-md-6">
                  <label class="form-label">YouTube</label>
                  <select class="form-select" name="youtubePrivacy">
                    <option value="private">Privado</option>
                    <option value="unlisted">Não listado</option>
                    <option value="public">Público</option>
                  </select>
                </div>
                <div class="col-md-6">
                  <label class="form-label">TikTok</label>
                  <select class="form-select" name="tiktokPrivacy">
                    <option value="private">Privado</option>
                    <option value="public">Público</option>
                  </select>
                  <small class="text-secondary">Publicação pública depende das permissões do aplicativo TikTok.</small>
                </div>
              </div>
              <div class="publish-block">
                <label class="form-label">Quando publicar</label>
                <div class="publish-timing-options">
                  <label><input type="radio" name="timing" value="now" checked> <span>Publicar agora</span></label>
                  <label><input type="radio" name="timing" value="schedule"> <span>Agendar</span></label>
                </div>
                <div id="publishScheduleFields" class="mt-3 d-none">
                  <input class="form-control" name="publishAt" type="datetime-local">
                  <small class="text-secondary">O CortaFé precisa estar rodando no horário programado.</small>
                </div>
              </div>
            </div>
            <div class="modal-footer border-0 pt-0">
              <button type="button" class="btn btn-outline-light" data-bs-dismiss="modal">Cancelar</button>
              <button type="submit" class="btn btn-gold" id="publishSubmit">Publicar</button>
            </div>
          </form>
        </div>
      </div>
    </div>`);

  element = document.querySelector('#publishModal');
  const form = element.querySelector('#publishForm');
  form.querySelectorAll('[name="timing"]').forEach(input => input.addEventListener('change', () => {
    const scheduled = form.querySelector('[name="timing"]:checked')?.value === 'schedule';
    form.querySelector('#publishScheduleFields').classList.toggle('d-none', !scheduled);
    form.querySelector('#publishSubmit').textContent = scheduled ? 'Agendar publicação' : 'Publicar agora';
  }));
  form.addEventListener('submit', submitPublicationForm);
  return element;
}

publishClip = async function (projectId, clipId, preferredPlatform) {
  const card = document.querySelector(`[data-clip="${clipId}"]`);
  const clip = current?.clips?.find(item => item.id === clipId);
  if (!card || !clip) return toast('Não foi possível localizar este corte.');
  if (!clip.videoPath) return toast('Renderize o corte antes de publicar.');

  let accounts;
  try { accounts = await api('/api/social/status'); }
  catch (error) { return toast(error.message); }

  const modal = ensurePublishModal();
  const form = modal.querySelector('#publishForm');
  form.reset();
  form.querySelector('[name="projectId"]').value = projectId;
  form.querySelector('[name="clipId"]').value = clipId;
  form.querySelector('[name="title"]').value = card.querySelector('[name="title"]').value;
  form.querySelector('[name="caption"]').value = card.querySelector('[name="caption"]').value;
  form.querySelector('[name="hashtags"]').value = (clip.hashtags || []).join(' ');
  form.querySelector('[name="youtubePrivacy"]').value = 'private';
  form.querySelector('[name="tiktokPrivacy"]').value = 'private';
  form.querySelector('[name="timing"][value="now"]').checked = true;
  form.querySelector('#publishScheduleFields').classList.add('d-none');
  form.querySelector('#publishSubmit').textContent = 'Publicar agora';

  const future = new Date(Date.now() + 60 * 60 * 1000);
  future.setMinutes(Math.ceil(future.getMinutes() / 15) * 15, 0, 0);
  form.querySelector('[name="publishAt"]').value = localDateTimeValue(future);

  const platformHost = form.querySelector('#publishPlatforms');
  platformHost.innerHTML = accounts.map(account => {
    const disabled = !account.connected;
    const checked = !disabled && account.platform === preferredPlatform;
    return `<label class="publish-platform-option ${disabled ? 'disabled' : ''}">
      <input type="checkbox" name="platform" value="${account.platform}" ${checked ? 'checked' : ''} ${disabled ? 'disabled' : ''}>
      <span class="publish-platform-icon">${account.platform === 'youTube' ? 'YT' : account.platform === 'instagram' ? 'IG' : 'TT'}</span>
      <span><strong>${publicationPlatformNames[account.platform]}</strong><small>${disabled ? 'Conta não conectada' : escapeHtml(account.accountName || 'Conta conectada')}</small></span>
    </label>`;
  }).join('');

  if (!platformHost.querySelector('input:checked')) {
    const first = platformHost.querySelector('input:not(:disabled)');
    if (first) first.checked = true;
  }

  bootstrap.Modal.getOrCreateInstance(modal).show();
};

async function submitPublicationForm(event) {
  event.preventDefault();
  const form = event.currentTarget;
  const button = form.querySelector('#publishSubmit');
  const platforms = [...form.querySelectorAll('[name="platform"]:checked')].map(input => input.value);
  if (!platforms.length) return toast('Escolha pelo menos uma rede conectada.');

  const scheduled = form.querySelector('[name="timing"]:checked')?.value === 'schedule';
  let publishAt = null;
  if (scheduled) {
    const raw = form.querySelector('[name="publishAt"]').value;
    if (!raw) return toast('Escolha a data e o horário da publicação.');
    const date = new Date(raw);
    if (Number.isNaN(date.getTime()) || date <= new Date()) return toast('Escolha um horário futuro.');
    publishAt = date.toISOString();
  }

  const projectId = form.querySelector('[name="projectId"]').value;
  const clipId = form.querySelector('[name="clipId"]').value;
  const title = form.querySelector('[name="title"]').value.trim();
  const description = publicationDescriptionFromFields(
    form.querySelector('[name="caption"]').value,
    form.querySelector('[name="hashtags"]').value);

  button.disabled = true;
  button.textContent = scheduled ? 'Agendando…' : 'Publicando…';
  let successes = 0;
  const failures = [];

  for (const platform of platforms) {
    const privacy = platform === 'instagram'
      ? 'public'
      : platform === 'tikTok'
        ? form.querySelector('[name="tiktokPrivacy"]').value
        : form.querySelector('[name="youtubePrivacy"]').value;
    try {
      await api(`/api/projects/${projectId}/clips/${clipId}/publish`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ platform, title, description, privacy, publishAt })
      });
      successes++;
    } catch (error) {
      failures.push(`${publicationPlatformNames[platform] || platform}: ${error.message}`);
    }
  }

  button.disabled = false;
  button.textContent = scheduled ? 'Agendar publicação' : 'Publicar agora';

  if (successes) {
    bootstrap.Modal.getOrCreateInstance(document.querySelector('#publishModal')).hide();
    toast(scheduled ? `${successes} publicação(ões) agendada(s)` : `${successes} publicação(ões) enviada(s)`);
  }
  if (failures.length) toast(failures.join(' · '));
}

function localDateTimeValue(date) {
  const pad = value => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

async function retryPublication(id) {
  try {
    await api(`/api/social/publications/${id}/retry`, { method: 'POST' });
    toast('Publicação colocada novamente na fila');
    socialCenter();
  } catch (error) { toast(error.message); }
}

async function refreshPublication(id) {
  try {
    await api(`/api/social/publications/${id}/refresh`, { method: 'POST' });
    toast('Estado atualizado');
    socialCenter();
  } catch (error) { toast(error.message); }
}
