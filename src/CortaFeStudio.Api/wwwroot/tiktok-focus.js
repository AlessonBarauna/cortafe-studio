(function () {
  const homeBase = home;
  home = async function () {
    await homeBase();
    const hero = document.querySelector('.hero'); if (!hero) return;
    const ready = projects.filter(project => project.status === 'ready').length;
    const clips = projects.reduce((total, project) => total + (project.clips?.length || 0), 0);
    hero.className = 'launch-dashboard mb-5';
    hero.innerHTML = `<div class="launch-copy"><span class="eyebrow">CORTAFÉ CREATOR OS · TIKTOK FIRST</span><h1>Do vídeo longo<br>ao próximo <em>grande corte.</em></h1><p>Seleção inteligente, acabamento premium e um pacote completo para programar sua sequência no TikTok Studio.</p><div class="launch-actions"><button class="btn btn-launch" data-action="new">Criar nova produção →</button><button class="btn btn-outline-light" data-action="factory">Produção em lote</button></div></div><div class="launch-orbit"><div class="orbit-core"><strong>${clips}</strong><span>cortes criados</span></div><i></i><i></i><i></i></div><div class="launch-metrics"><article><strong>${projects.length}</strong><span>projetos</span></article><article><strong>${ready}</strong><span>prontos</span></article><article><strong>1080p</strong><span>qualidade social</span></article><article><strong>TikTok</strong><span>canal ativo</span></article></div>`;
    bindCommon(); hero.querySelector('[data-action="factory"]')?.addEventListener('click', factoryCenter);
  };

  const renderBase = renderProject;
  renderProject = function (project) {
    renderBase(project);
    if (project.status !== 'ready') return;
    document.querySelectorAll('[onclick*="\'youTube\'"],[onclick*="\'instagram\'"]').forEach(button => button.remove());
    document.querySelectorAll('[onclick*="\'tikTok\'"]').forEach(button => { button.textContent = 'Publicar no TikTok'; button.classList.add('tiktok-action'); });
    const zip = document.querySelector('#downloadZip');
    if (zip) { zip.textContent = 'Pacote TikTok Studio'; zip.classList.add('btn-tiktok'); zip.onclick = () => window.location.assign(`/api/projects/${project.id}/exports/tiktok-studio.zip`); }
    const heading = document.querySelector('.section-head > div');
    if (heading && !heading.querySelector('.tiktok-ready-badge')) heading.insertAdjacentHTML('beforeend','<span class="tiktok-ready-badge">● Fluxo TikTok ativo</span>');
  };

  const socialBase = socialCenter;
  socialCenter = async function () {
    await socialBase();
    document.querySelector('.workspace-title h1')?.replaceChildren(document.createTextNode('TikTok. Um fluxo completo.'));
    const intro = document.querySelector('.workspace-title p'); if (intro) intro.textContent = 'Conecte sua conta ou exporte o pacote para programar manualmente no TikTok Studio.';
  };
  setTimeout(() => home(), 0);
})();
