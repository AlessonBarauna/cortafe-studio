(function () {
  const renderBeforeFocusedEditor = renderProject;
  renderProject = function (project) {
    renderBeforeFocusedEditor(project);
    if (project.status !== 'ready') return;
    document.querySelectorAll('.cc-properties-panel .clip-card').forEach(organizeClipEditor);
    installProductivitySuite(project);
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

  const PRODUCTIVITY_STYLE_ID = 'aj-productivity-suite-style';
  const BRAND_STORAGE_KEY = 'cortafe-brand-kits-v1';
  const templates = {
    pregacao: { name: 'Pregação Impacto', subtitleStyle: 'sermon', transitionStyle: 'editorial', layoutMode: 'fill', playbackSpeed: 1, silenceTrimmingEnabled: true, brandFrameEnabled: true, watermarkEnabled: true },
    devocional: { name: 'Devocional Limpo', subtitleStyle: 'clean', transitionStyle: 'smooth', layoutMode: 'fill', playbackSpeed: 1, silenceTrimmingEnabled: true, brandFrameEnabled: true, watermarkEnabled: true },
    podcast: { name: 'Podcast Dinâmico', subtitleStyle: 'podcast', transitionStyle: 'editorial', layoutMode: 'fill', playbackSpeed: 1, silenceTrimmingEnabled: true, brandFrameEnabled: true, watermarkEnabled: true },
    louvor: { name: 'Louvor Suave', subtitleStyle: 'worship', transitionStyle: 'smooth', layoutMode: 'fill', playbackSpeed: 1, silenceTrimmingEnabled: false, brandFrameEnabled: true, watermarkEnabled: true },
    viral: { name: 'Impacto Social', subtitleStyle: 'bold', transitionStyle: 'dynamic', layoutMode: 'fill', playbackSpeed: 1.25, silenceTrimmingEnabled: true, brandFrameEnabled: true, watermarkEnabled: true }
  };

  function installProductivitySuite(project) {
    const view = document.querySelector('#projectView');
    if (!view || view.querySelector('.aj-productivity-suite')) return;
    injectProductivityStyles();
    const sectionHead = view.querySelector('.section-head');
    const host = document.createElement('section');
    host.className = 'aj-productivity-suite';
    host.innerHTML = `<div class="aj-productivity-title"><div><span class="eyebrow">EDIÇÃO EM MASSA</span><strong>Padronize vários cortes de uma vez</strong></div><label><input type="checkbox" data-batch-select-all> Selecionar todos</label></div><div class="aj-productivity-controls"><span data-batch-count>0 selecionados</span><select class="form-select" data-batch-template><option value="">Template...</option>${Object.entries(templates).map(([id,item])=>`<option value="${id}">${item.name}</option>`).join('')}</select><select class="form-select" data-batch-brand><option value="">Brand Kit...</option></select><button type="button" class="btn btn-gold" data-batch-apply>Aplicar aos selecionados</button><button type="button" class="btn btn-outline-light" data-brand-save>Salvar visual como Brand Kit</button><button type="button" class="btn btn-outline-secondary" data-brand-delete>Excluir kit</button></div><small class="text-secondary">Templates alteram ritmo, legenda e transição. Brand Kits reaplicam identidade visual. Os cortes são marcados para nova renderização automaticamente.</small>`;
    sectionHead?.after(host);

    document.querySelectorAll('.clip-card').forEach(card => {
      if (card.querySelector('[data-batch-select]')) return;
      card.insertAdjacentHTML('afterbegin', '<label class="aj-batch-check" onclick="event.stopPropagation()"><input type="checkbox" data-batch-select> lote</label>');
    });

    const selectAll = host.querySelector('[data-batch-select-all]');
    selectAll.addEventListener('change', () => {
      document.querySelectorAll('[data-batch-select]').forEach(input => { input.checked = selectAll.checked; });
      updateBatchCount(host);
    });
    document.querySelectorAll('[data-batch-select]').forEach(input => input.addEventListener('change', () => updateBatchCount(host)));
    host.querySelector('[data-batch-apply]').addEventListener('click', () => applyBatchStyle(project, host));
    host.querySelector('[data-brand-save]').addEventListener('click', () => saveBrandKit(project, host));
    host.querySelector('[data-brand-delete]').addEventListener('click', () => deleteBrandKit(host));
    refreshBrandOptions(host);
    updateBatchCount(host);
  }

  function injectProductivityStyles() {
    if (document.getElementById(PRODUCTIVITY_STYLE_ID)) return;
    const style = document.createElement('style');
    style.id = PRODUCTIVITY_STYLE_ID;
    style.textContent = `.aj-productivity-suite{margin:0 0 20px;padding:16px 18px;border:1px solid rgba(199,163,90,.28);border-radius:16px;background:rgba(18,16,14,.88);display:grid;gap:12px}.aj-productivity-title{display:flex;justify-content:space-between;gap:16px;align-items:center}.aj-productivity-title strong{display:block;font-size:1.05rem}.aj-productivity-title label{font-size:.86rem;color:#d8cfbf;white-space:nowrap}.aj-productivity-controls{display:grid;grid-template-columns:auto minmax(150px,1fr) minmax(150px,1fr) auto auto auto;gap:8px;align-items:center}.aj-productivity-controls [data-batch-count]{font-size:.82rem;color:#c7a35a;white-space:nowrap}.aj-batch-check{position:absolute;z-index:6;top:8px;right:8px;background:rgba(0,0,0,.78);border:1px solid rgba(255,255,255,.15);border-radius:999px;padding:4px 8px;font-size:.72rem;color:#ddd}.clip-card{position:relative}.clip-card:has([data-batch-select]:checked){outline:2px solid rgba(199,163,90,.55);outline-offset:2px}@media(max-width:1100px){.aj-productivity-controls{grid-template-columns:1fr 1fr}.aj-productivity-controls [data-batch-count]{grid-column:1/-1}}@media(max-width:650px){.aj-productivity-title{align-items:flex-start;flex-direction:column}.aj-productivity-controls{grid-template-columns:1fr}.aj-productivity-controls [data-batch-count]{grid-column:auto}}`;
    document.head.append(style);
  }

  function selectedClipIds() {
    return [...document.querySelectorAll('.clip-card')]
      .filter(card => card.querySelector('[data-batch-select]')?.checked)
      .map(card => card.dataset.clip)
      .filter(Boolean);
  }

  function updateBatchCount(host) {
    const ids = selectedClipIds();
    const label = host.querySelector('[data-batch-count]');
    if (label) label.textContent = `${ids.length} ${ids.length === 1 ? 'selecionado' : 'selecionados'}`;
    const all = host.querySelector('[data-batch-select-all]');
    const total = document.querySelectorAll('[data-batch-select]').length;
    if (all && ids.length !== total) all.checked = false;
  }

  function loadBrandKits() {
    try {
      const parsed = JSON.parse(localStorage.getItem(BRAND_STORAGE_KEY) || '[]');
      return Array.isArray(parsed) ? parsed : [];
    } catch { return []; }
  }

  function storeBrandKits(kits) { localStorage.setItem(BRAND_STORAGE_KEY, JSON.stringify(kits)); }

  function refreshBrandOptions(host, selectedId = '') {
    const select = host.querySelector('[data-batch-brand]');
    if (!select) return;
    const kits = loadBrandKits();
    select.innerHTML = '<option value="">Brand Kit...</option>' + kits.map(kit => `<option value="${escapeHtml(kit.id)}" ${kit.id === selectedId ? 'selected' : ''}>${escapeHtml(kit.name)}</option>`).join('');
  }

  function activeOrSelectedClip(project) {
    const active = document.querySelector('.clip-card.active')?.dataset.clip;
    const selected = selectedClipIds()[0];
    return project.clips.find(clip => clip.id === (active || selected)) || project.clips[0];
  }

  function saveBrandKit(project, host) {
    const clip = activeOrSelectedClip(project);
    if (!clip) return toast('Abra um corte antes de criar o Brand Kit');
    const name = prompt('Nome do Brand Kit:', 'Minha identidade');
    if (!name?.trim()) return;
    const kit = {
      id: crypto.randomUUID().replaceAll('-', '').slice(0, 10), name: name.trim(),
      brandTheme: clip.brandTheme || 'amado-jesus', coverAccent: clip.coverAccent || '#C7A35A',
      brandFrameEnabled: clip.brandFrameEnabled !== false, watermarkEnabled: clip.watermarkEnabled !== false,
      watermarkText: clip.watermarkText || 'AJ  |  AMADO JESUS', watermarkOpacity: Number.isFinite(+clip.watermarkOpacity) ? +clip.watermarkOpacity : .82,
      subtitleStyle: clip.subtitleTrack?.style || clip.subtitleStyle || 'sermon'
    };
    const kits = loadBrandKits(); kits.push(kit); storeBrandKits(kits); refreshBrandOptions(host, kit.id); toast(`Brand Kit “${kit.name}” salvo`);
  }

  function deleteBrandKit(host) {
    const select = host.querySelector('[data-batch-brand]'), id = select?.value;
    if (!id) return toast('Escolha um Brand Kit para excluir');
    const kit = loadBrandKits().find(item => item.id === id);
    if (!confirm(`Excluir o Brand Kit “${kit?.name || 'selecionado'}”?`)) return;
    storeBrandKits(loadBrandKits().filter(item => item.id !== id)); refreshBrandOptions(host); toast('Brand Kit excluído');
  }

  async function applyBatchStyle(project, host) {
    const ids = selectedClipIds();
    if (!ids.length) return toast('Selecione pelo menos um corte');
    const template = templates[host.querySelector('[data-batch-template]')?.value] || null;
    const brandId = host.querySelector('[data-batch-brand]')?.value;
    const brand = loadBrandKits().find(item => item.id === brandId) || null;
    if (!template && !brand) return toast('Escolha um Template, um Brand Kit ou os dois');
    const button = host.querySelector('[data-batch-apply]'); button.disabled = true; button.textContent = `Aplicando em ${ids.length}...`;
    try {
      for (const id of ids) {
        const clip = project.clips.find(item => item.id === id); if (!clip) continue;
        const body = {};
        if (template) Object.assign(body, template);
        if (brand) Object.assign(body, {
          brandTheme: brand.brandTheme, coverAccent: brand.coverAccent, brandFrameEnabled: brand.brandFrameEnabled,
          watermarkEnabled: brand.watermarkEnabled, watermarkText: brand.watermarkText, watermarkOpacity: brand.watermarkOpacity,
          subtitleStyle: brand.subtitleStyle
        });
        if (project.options?.contentType === 'louvor') body.playbackSpeed = 1;
        await api(`/api/projects/${project.id}/clips/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        Object.assign(clip, body);
        const subtitleStyle = brand?.subtitleStyle || template?.subtitleStyle;
        if (subtitleStyle && clip.subtitleTrack) {
          clip.subtitleTrack.style = subtitleStyle;
          clip.subtitleTrack.editedByUser = true;
          clip.subtitleTrack.autoGenerated = false;
          clip.subtitleTrack = await api(`/api/projects/${project.id}/clips/${id}/subtitles`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(clip.subtitleTrack) });
        }
      }
      toast(`✓ Padrão aplicado a ${ids.length} ${ids.length === 1 ? 'corte' : 'cortes'}`);
      await openProject(project.id);
    } catch (error) {
      toast(error.message);
      button.disabled = false; button.textContent = 'Aplicar aos selecionados';
    }
  }
})();
