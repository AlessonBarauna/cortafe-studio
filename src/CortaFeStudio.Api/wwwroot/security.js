(() => {
  const nativeFetch = window.fetch.bind(window);
  let open = null;

  window.fetch = async (input, init) => {
    const response = await nativeFetch(input, init);
    try {
      const url = typeof input === 'string' ? input : input.url;
      if (response.status === 401 && url.includes('/api/') && !url.includes('/api/security/')) showPanel('login');
    } catch { /* ignora */ }
    return response;
  };

  const post = async (url, body) => {
    const response = await nativeFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (!response.ok) {
      let data = null;
      try { data = await response.json(); } catch { /* sem corpo */ }
      const error = new Error(data?.error || 'Não foi possível concluir.');
      error.status = response.status;
      throw error;
    }
  };

  const validPin = value => /^\d{4,12}$/.test(value);

  function showPanel(mode) {
    if (open && open.dataset.mode === mode) return;
    open?.remove();
    const isLogin = mode === 'login';
    const wrap = document.createElement('div');
    wrap.className = 'pin-overlay';
    wrap.dataset.mode = mode;
    wrap.innerHTML = `
      <form class="pin-card" autocomplete="off">
        <span class="pin-eyebrow">${isLogin ? 'ACESSO PROTEGIDO' : 'SEGURANÇA DO ESTÚDIO'}</span>
        <h2>${isLogin ? 'Digite seu PIN' : 'Defina um PIN'}</h2>
        <p>${isLogin ? 'Este estúdio está protegido. A sessão dura 12 horas.' : 'De 4 a 12 números. Ele protege o estúdio quando você acessa pela rede.'}</p>
        <input name="pin" type="password" inputmode="numeric" maxlength="12" placeholder="${isLogin ? 'PIN' : 'Novo PIN'}" required autofocus>
        ${isLogin ? '' : '<input name="again" type="password" inputmode="numeric" maxlength="12" placeholder="Repita o PIN" required>'}
        <div class="pin-error" role="alert"></div>
        <div class="pin-actions">
          <button type="submit" class="pin-go">${isLogin ? 'Entrar' : 'Salvar PIN'}</button>
          ${isLogin ? '' : '<button type="button" class="pin-cancel">Cancelar</button>'}
        </div>
      </form>`;
    document.body.append(wrap);
    open = wrap;
    const form = wrap.querySelector('form');
    const error = wrap.querySelector('.pin-error');
    wrap.querySelector('.pin-cancel')?.addEventListener('click', () => { wrap.remove(); open = null; });
    form.addEventListener('submit', async event => {
      event.preventDefault();
      error.textContent = '';
      const pin = form.pin.value.trim();
      if (!validPin(pin)) { error.textContent = 'Use apenas números, de 4 a 12 dígitos.'; return; }
      if (!isLogin && pin !== form.again.value.trim()) { error.textContent = 'Os dois PINs precisam ser iguais.'; return; }
      try {
        if (!isLogin) await post('/api/security/configure', { pin });
        await post('/api/security/login', { pin });
        location.reload();
      } catch (problem) {
        if (problem.status === 401) { wrap.remove(); open = null; showPanel('login'); return; }
        error.textContent = problem.message;
        form.pin.select();
      }
    });
    form.pin.focus();
  }

  async function addNavButton() {
    let enabled = false;
    try { enabled = (await (await nativeFetch('/api/security/status')).json()).enabled; } catch { return; }
    const anchor = document.querySelector('[data-action="diagnostics"]');
    if (!anchor || document.querySelector('[data-action="pin"]')) return;
    const button = document.createElement('button');
    button.className = enabled ? 'btn btn-outline-light' : 'btn btn-outline-light pin-needed';
    button.dataset.action = 'pin';
    button.textContent = enabled ? 'Trocar PIN' : 'Definir PIN';
    button.addEventListener('click', () => showPanel('setup'));
    anchor.parentElement.insertBefore(button, anchor);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', addNavButton);
  else addNavButton();
})();
