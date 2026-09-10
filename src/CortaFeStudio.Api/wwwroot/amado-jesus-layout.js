(function () {
  const app = document.querySelector('#app');
  if (!app) return;

  let worshipLoader = null;

  function ensureWorshipFmLoaded() {
    if (typeof window.worshipFmCenter === 'function') return Promise.resolve();
    if (worshipLoader) return worshipLoader;

    worshipLoader = new Promise((resolve, reject) => {
      const existing = document.querySelector('script[data-worship-fm-script]');
      const script = existing || document.createElement('script');
      let settled = false;

      const finish = () => {
        if (settled) return;
        settled = true;
        if (typeof window.worshipFmCenter === 'function') resolve();
        else reject(new Error('O arquivo worship-fm.js carregou, mas o workspace não foi registrado.'));
      };

      const fail = () => {
        if (settled) return;
        settled = true;
        reject(new Error('Não foi possível carregar /worship-fm.js.'));
      };

      script.addEventListener('load', finish, { once: true });
      script.addEventListener('error', fail, { once: true });

      if (!existing) {
        script.src = '/worship-fm.js?v=20260910.2';
        script.async = false;
        script.dataset.worshipFmScript = '1';
        document.body.appendChild(script);
      } else if (typeof window.worshipFmCenter === 'function') {
        finish();
      }

      window.setTimeout(() => {
        if (settled) return;
        if (typeof window.worshipFmCenter === 'function') finish();
        else fail();
      }, 5000);
    }).catch(error => {
      worshipLoader = null;
      throw error;
    });

    return worshipLoader;
  }

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
      <button data-aj-route="worship"><i>♫</i><span>Worship FM</span></button>
      <span class="aj-menu-label">Gerenciar</span>
      <button data-aj-route="social"><i>↗</i><span>Publicar no TikTok</span></button>
      <button data-aj-route="diagnostics"><i>◇</i><span>Diagnóstico</span></button>
    </nav>
    <div class="aj-sidebar-card"><span>FLUXO ATIVO</span><strong>TikTok Studio</strong><small>Crie, revise e exporte seus cortes em um só lugar.</small></div>`;
  document.body.insertBefore(sidebar, document.body.firstChild);

  const routes = {
    home: () => home(),
    new: () => newProject(),
    worship: () => openWorshipFm(),
    social: () => socialCenter(),
    diagnostics: () => diagnosticsCenter()
  };
  sidebar.querySelectorAll('[data-aj-route]').forEach(button => button.addEventListener('click', () => routes[button.dataset.ajRoute]?.()));

  function setActive(route) {
    sidebar.querySelectorAll('[data-aj-route]').forEach(button => button.classList.toggle('active', button.dataset.ajRoute === route));
  }

  async function openWorshipFm() {
    setActive('worship');
    app.innerHTML = '<div class="processing-stage py-5">Carregando Worship FM…</div>';

    try {
      await ensureWorshipFmLoaded();
      if (typeof window.worshipFmCenter !== 'function') throw new Error('Workspace Worship FM indisponível.');
      window.worshipFmCenter();
    } catch (error) {
      console.error('[Worship FM]', error);
      app.innerHTML = `
        <section class="studio-panel p-4">
          <span class="eyebrow">WORSHIP FM</span>
          <h2>Não foi possível abrir o workspace.</h2>
          <p class="text-secondary">${String(error?.message || error)}</p>
          <button class="btn btn-gold" type="button" data-worship-retry>Tentar novamente</button>
        </section>`;
      app.querySelector('[data-worship-retry]')?.addEventListener('click', openWorshipFm);
    }
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

  window.openWorshipFm = openWorshipFm;
  setActive('home');
  setTimeout(decorateHome, 0);
})();
