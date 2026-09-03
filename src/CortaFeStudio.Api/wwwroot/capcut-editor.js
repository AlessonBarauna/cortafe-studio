(function () {
  const cardBase = clipCard;
  clipCard = function (project, clip, index) {
    let html = cardBase(project, clip, index);
    const tabs = `<nav class="edit-mode-tabs" aria-label="Ferramentas de edição">
      <button type="button" class="active" data-edit-mode="cut">✂ Corte</button>
      <button type="button" data-edit-mode="captions">▤ Legendas</button>
      <button type="button" data-edit-mode="visual">◫ Visual</button>
      <button type="button" data-edit-mode="brand">◆ Marca</button>
    </nav>`;
    html = html.replace(/(<article class="clip-card"[^>]*>)/, `$1${tabs}`);
    html = html.replaceAll('>Exibir legendas</span>', '>Adicionar legendas</span>');
    return html;
  };

  const renderBase = renderProject;
  renderProject = function (project) {
    renderBase(project);
    if (project.status !== 'ready') return;
    document.querySelectorAll('.clip-card').forEach(card => activateEditMode(card, 'cut'));
  };

  function activateEditMode(card, mode) {
    card.dataset.editMode = mode;
    card.querySelectorAll('[data-edit-mode]').forEach(button => button.classList.toggle('active', button.dataset.editMode === mode));
    card.querySelector('.timeline-editor')?.classList.toggle('tool-visible', mode === 'cut');
    card.querySelector('.subtitle-editor')?.classList.toggle('tool-visible', mode === 'captions');
    card.querySelector('.editor-tools')?.classList.toggle('tool-visible', mode === 'visual');
    card.querySelector('.brand-editor')?.classList.toggle('tool-visible', mode === 'brand');
    const visibleDetails = mode === 'visual' ? card.querySelector('.editor-tools') : mode === 'brand' ? card.querySelector('.brand-editor') : null;
    if (visibleDetails) visibleDetails.open = true;
    if (mode === 'captions') {
      const clip = current?.clips.find(item => item.id === card.dataset.clip);
      const video = document.querySelector('#preview video');
      if (clip && video) updateSubtitlePreview(video, clip);
    }
  }

  document.addEventListener('click', event => {
    const button = event.target.closest('[data-edit-mode]');
    if (!button) return;
    event.preventDefault(); event.stopPropagation();
    activateEditMode(button.closest('.clip-card'), button.dataset.editMode);
  });
})();
