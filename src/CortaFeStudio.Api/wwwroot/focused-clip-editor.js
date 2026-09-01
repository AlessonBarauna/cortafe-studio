(function () {
  const renderBeforeFocusedEditor = renderProject;
  renderProject = function (project) {
    renderBeforeFocusedEditor(project);
    if (project.status !== 'ready') return;
    document.querySelectorAll('.cc-properties-panel .clip-card').forEach(organizeClipEditor);
  };

  function organizeClipEditor(card) {
    if (card.dataset.focusedEditor) return;
    card.dataset.focusedEditor = 'true';

    const tabs = card.querySelector('.edit-mode-tabs');
    tabs?.insertAdjacentHTML('beforeend', '<button type="button" data-edit-mode="details">••• Detalhes</button>');

    const title = card.querySelector('[name="title"]');
    if (title) {
      const group = document.createElement('section');
      group.className = 'cc-title-editor cc-mode-cut';
      group.innerHTML = '<label>Título do vídeo</label><small>Este título acompanhará o corte ao baixar.</small>';
      title.before(group); group.append(title);
      const tools = card.querySelector('.title-tools'); if (tools) group.append(tools);
      const suggestions = card.querySelector('.title-suggestions'); if (suggestions) group.append(suggestions);
    }

    const trimRow = [...card.children].find(child => child.matches('.row') && child.querySelector('[name="start"]') && child.querySelector('[name="end"]'));
    trimRow?.classList.add('cc-mode-cut', 'cc-trim-card');
    trimRow?.insertAdjacentHTML('afterbegin', '<div class="col-12"><span class="cc-group-title">Intervalo do corte</span></div>');

    const primaryActions = [...card.children].find(child => child.querySelector?.('[data-save]') && child.querySelector?.('[data-render]'));
    primaryActions?.classList.add('cc-mode-cut', 'cc-primary-actions');
    [...card.querySelectorAll('button')].filter(button => button.textContent.trim() === 'Prévia rápida').forEach(button => button.classList.add('cc-mode-cut'));

    const details = document.createElement('section');
    details.className = 'cc-mode-details';
    details.innerHTML = '<header><strong>Informações e publicação</strong><small>Métricas, textos e aprovação ficam separados da edição.</small></header>';
    card.append(details);

    moveField(card.querySelector('[name="coverText"]'), details, 'Texto da capa');
    moveField(card.querySelector('[name="caption"]'), details, 'Descrição e hashtags');

    [...card.children].filter(child => child !== details && (
      child.classList.contains('render-state') ||
      (child.matches('.d-flex') && child.querySelector('.badge')) ||
      (child.matches('p') && child.querySelector('.eyebrow')) ||
      child.textContent?.includes('POR QUE ESTE TRECHO')
    )).forEach(child => details.append(child));

    const download = [...card.children].find(child => child.matches?.('a[download]'));
    if (download) { details.append(download); const publishing = download.nextElementSibling; if (publishing?.matches('.d-flex')) details.append(publishing); }
    [...card.children].filter(child => child !== details && child.textContent?.includes('Publicar no TikTok')).forEach(child => details.append(child));
    [...card.querySelectorAll('button')].filter(button => ['Copy por plataforma', 'Registrar métricas'].includes(button.textContent.trim())).forEach(button => details.append(button));
    card.querySelectorAll('.tiktok-workflow').forEach(workflow => details.append(workflow));

    const subtitle = card.querySelector('.subtitle-editor'); subtitle?.classList.add('cc-mode-captions');
    const visual = card.querySelector('.editor-tools'); visual?.classList.add('cc-mode-visual');
    const brand = card.querySelector('.brand-editor'); brand?.classList.add('cc-mode-brand');
  }

  function moveField(field, destination, label) {
    if (!field) return;
    const wrapper = document.createElement('label'); wrapper.className = 'cc-detail-field';
    wrapper.append(document.createTextNode(label)); field.before(wrapper); wrapper.append(field); destination.append(wrapper);
  }

  document.addEventListener('click', event => {
    const button = event.target.closest('.cc-properties-panel [data-edit-mode]');
    if (!button) return;
    const card = button.closest('.clip-card');
    requestAnimationFrame(() => card.dataset.editMode = button.dataset.editMode);
  });

  document.addEventListener('input', event => {
    if (!event.target.matches('.cc-properties-panel [name="title"]')) return;
    const card = event.target.closest('.clip-card'), id = card?.dataset.clip, value = event.target.value.trim() || 'Corte sem título';
    const inspector = document.querySelector('#ccInspectorTitle'); if (inspector) inspector.textContent = value;
    const asset = document.querySelector(`[data-cc-clip="${id}"] strong`); if (asset) asset.textContent = value;
    const track = document.querySelector(`[data-cc-track="${id}"] span`); if (track) track.textContent = value;
    const monitor = document.querySelector('#monitorTitle'); if (monitor) monitor.textContent = value;
  });
})();
