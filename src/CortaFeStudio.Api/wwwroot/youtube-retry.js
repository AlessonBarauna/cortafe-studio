(function () {
  const originalRenderProject = window.renderProject;
  const browsers = [
    ['chrome', 'Google Chrome'],
    ['edge', 'Microsoft Edge'],
    ['firefox', 'Mozilla Firefox'],
    ['brave', 'Brave'],
    ['chromium', 'Chromium'],
    ['opera', 'Opera'],
    ['vivaldi', 'Vivaldi']
  ];

  window.renderProject = function (project) {
    originalRenderProject(project);
    if (!['failed', 'cancelled'].includes(project.status)) return;

    const retryButton = document.querySelector('#retry');
    if (!retryButton) return;
    const needsSession = ['youtube-auth-required', 'youtube-cookie-access'].includes(project.failureCode)
      || /sign in to confirm|confirmação de acesso|sessão do navegador/i.test(project.error || '');
    const panel = retryButton.closest('.studio-panel');

    if (needsSession) {
      const sessionPanel = document.createElement('div');
      sessionPanel.className = 'border border-secondary rounded-3 p-3 mb-3';
      sessionPanel.innerHTML = `
        <label class="form-label fw-semibold" for="youtubeBrowser">Navegador conectado ao YouTube</label>
        <select class="form-select" id="youtubeBrowser">
          ${browsers.map(([value, label]) => `<option value="${value}">${label}</option>`).join('')}
        </select>
        <small class="text-secondary d-block mt-2">O yt-dlp usa a sessão diretamente neste computador. O CortaFé não copia nem armazena os cookies.</small>`;
      retryButton.before(sessionPanel);
      retryButton.textContent = 'Processar com sessão do navegador';

      const anonymousButton = document.createElement('button');
      anonymousButton.className = 'btn btn-outline-light ms-2';
      anonymousButton.textContent = 'Tentar sem sessão';
      retryButton.after(anonymousButton);
      anonymousButton.onclick = () => retryProject(project, null, panel);
    }

    retryButton.onclick = () => retryProject(
      project,
      needsSession ? document.querySelector('#youtubeBrowser').value : null,
      panel);
  };

  async function retryProject(project, browser, panel) {
    panel.querySelectorAll('button,select').forEach(element => element.disabled = true);
    project.status = 'queued';
    project.progress = 1;
    project.stage = browser
      ? 'Aguardando acesso com a sessão do navegador'
      : 'Retentativa adicionada à fila';
    project.error = null;
    project.failureCode = null;
    window.renderProject(project);

    clearInterval(poller);
    startPolling(project.id);
    try {
      current = await api(`/api/projects/${project.id}/retry`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ browser })
      });
      window.renderProject(current);
      toast('Projeto adicionado à fila');
    } catch (error) {
      clearInterval(poller);
      toast(error.message);
      openProject(project.id);
    }
  }

  function startPolling(projectId) {
    poller = setInterval(async () => {
      try {
        current = await api(`/api/projects/${projectId}`);
        window.renderProject(current);
        if (['ready', 'failed', 'cancelled'].includes(current.status)) clearInterval(poller);
      } catch { }
    }, 1800);
  }
})();
