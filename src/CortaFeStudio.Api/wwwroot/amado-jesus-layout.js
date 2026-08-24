(function () {
  const app = document.querySelector('#app');
  if (!app) return;

  const sidebar = document.createElement('aside');
  sidebar.className = 'aj-sidebar';
  sidebar.innerHTML = `
    <button class="aj-brand" data-aj-route="home" aria-label="Ir para o início">
      <span class="aj-brand-mark">AJ</span><span><strong>Amado Jesus</strong><small>STUDIO</small></span>
    </button>
    <nav class="aj-menu" aria-label="Navegação principal">
      <span class="aj-menu-label">Workspace</span>
      <button data-aj-route="home"><i>⌂</i><span>Início</span></button>
      <button data-aj-route="new"><i>＋</i><span>Novo projeto</span></button>
      <span class="aj-menu-label">Gerenciar</span>
      <button data-aj-route="social"><i>↗</i><span>Publicar no TikTok</span></button>
      <button data-aj-route="diagnostics"><i>◇</i><span>Diagnóstico</span></button>
    </nav>
    <div class="aj-sidebar-card"><span>FLUXO ATIVO</span><strong>TikTok Studio</strong><small>Crie, revise e exporte seus cortes em um só lugar.</small></div>`;
  document.body.insertBefore(sidebar, document.body.firstChild);

  const routes = { home: () => home(), new: () => newProject(), social: () => socialCenter(), diagnostics: () => diagnosticsCenter() };
  sidebar.querySelectorAll('[data-aj-route]').forEach(button => button.addEventListener('click', () => routes[button.dataset.ajRoute]?.()));

  function setActive(route) {
    sidebar.querySelectorAll('[data-aj-route]').forEach(button => button.classList.toggle('active', button.dataset.ajRoute === route));
  }

  function decorateHome() {
    const hero = app.querySelector('.launch-dashboard');
    if (!hero || app.querySelector('.aj-quick-create')) return;
    const quick = document.createElement('section');
    quick.className = 'aj-quick-create';
    quick.innerHTML = `
      <div class="aj-quick-heading"><span class="eyebrow">COMECE AGORA</span><h2>Transforme um vídeo em vários cortes</h2><p>Cole um link do YouTube ou envie um arquivo. A análise e a renderização acontecem localmente.</p></div>
      <div class="aj-link-box"><span class="aj-link-icon">▶</span><textarea rows="2" aria-label="Links dos vídeos" placeholder="Cole um ou vários links do YouTube, um por linha"></textarea><button type="button">Gerar cortes <span>→</span></button></div>
      <div class="aj-quick-meta"><span>✓ Até 20 cortes</span><span>✓ Formato vertical 9:16</span><span>✓ Títulos e hashtags</span><button type="button" data-upload>Ou enviar arquivo</button></div>`;
    hero.insertAdjacentElement('afterend', quick);
    const start = upload => {
      const links = quick.querySelector('textarea').value.trim();
      newProject();
      setTimeout(() => {
        if (upload) document.querySelector('[data-source="upload"]')?.click();
        else {
          const field = document.querySelector('[name="url"]');
          if (field) { field.value = links; field.focus(); }
        }
      }, 0);
    };
    quick.querySelector('.aj-link-box button').addEventListener('click', () => start(false));
    quick.querySelector('[data-upload]').addEventListener('click', () => start(true));
    quick.querySelector('textarea').addEventListener('keydown', event => {
      if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') start(false);
    });
  }

  const originalHome = window.home;
  window.home = async function () {
    setActive('home');
    await originalHome();
    decorateHome();
  };
  const originalNewProject = window.newProject;
  window.newProject = function () { setActive('new'); return originalNewProject(); };
  const originalSocial = window.socialCenter;
  window.socialCenter = async function () { setActive('social'); return originalSocial(); };
  const originalDiagnostics = window.diagnosticsCenter;
  window.diagnosticsCenter = async function () { setActive('diagnostics'); return originalDiagnostics(); };

  setActive('home');
  setTimeout(decorateHome, 0);
})();
