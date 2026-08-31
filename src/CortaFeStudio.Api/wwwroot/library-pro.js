(function () {
  let storageByProject = new Map();
  const homeBase = home;
  home = async function () {
    await homeBase();
    try { const storage = await api('/api/storage'); storageByProject = new Map((storage.projects || []).map(item => [item.id, item])); } catch { storageByProject = new Map(); }
    installLibraryTools();
  };

  function installLibraryTools() {
    const grid = document.querySelector('#projects'); if (!grid || document.querySelector('#libraryProTools')) return;
    grid.insertAdjacentHTML('beforebegin', `<section id="libraryProTools" class="library-pro-tools"><div class="library-search"><span>⌕</span><input id="librarySearch" placeholder="Buscar projeto, tema ou fala da transcrição"></div><select id="libraryStatus"><option value="">Todos os status</option><option value="ready">Prontos</option><option value="failed">Com falha</option><option value="processing">Em processamento</option><option value="favorite">Favoritos</option></select><select id="libraryProfile"><option value="">Todos os nichos</option>${[...new Set(projects.map(project => project.options?.contentType).filter(Boolean))].map(value => `<option>${escapeHtml(value)}</option>`).join('')}</select><button class="btn btn-outline-danger d-none" id="libraryBatchClean">Liberar espaço selecionado</button></section>`);
    grid.querySelectorAll('.project-card').forEach(card => decorateCard(card));
    document.querySelector('#librarySearch').oninput = filterLibrary; document.querySelector('#libraryStatus').onchange = filterLibrary; document.querySelector('#libraryProfile').onchange = filterLibrary; document.querySelector('#libraryBatchClean').onclick = cleanSelectedProjects;
    reorderLibrary();
  }

  function decorateCard(card) {
    const project = projects.find(item => item.id === card.dataset.id); if (!project) return; const storage = storageByProject.get(project.id);
    card.dataset.search = `${project.name} ${project.source} ${project.options?.contentType} ${(project.clips || []).map(clip => `${clip.title} ${clip.transcript} ${clip.diversityTopic}`).join(' ')}`.toLowerCase(); card.dataset.status = project.status; card.dataset.profile = project.options?.contentType || ''; card.dataset.favorite = String(!!project.favorite); card.dataset.pinned = String(!!project.pinned);
    card.insertAdjacentHTML('afterbegin', `<div class="library-card-tools" onclick="event.stopPropagation()"><input class="form-check-input" type="checkbox" data-library-select title="Selecionar"><button data-library-favorite title="Favoritar">${project.favorite ? '★' : '☆'}</button><button data-library-pin title="Fixar">${project.pinned ? '●' : '○'}</button></div>${storage ? `<span class="library-storage">${bytesLabel(storage.bytes)}</span>` : ''}`);
    card.querySelector('[data-library-select]').onchange = updateBatchButton; card.querySelector('[data-library-favorite]').onclick = () => updateLibraryState(project, card, 'favorite'); card.querySelector('[data-library-pin]').onclick = () => updateLibraryState(project, card, 'pinned');
  }

  async function updateLibraryState(project, card, field) { project[field] = !project[field]; await api(`/api/projects/${project.id}/library`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ [field]: project[field] }) }); card.dataset[field] = String(project[field]); card.querySelector(field === 'favorite' ? '[data-library-favorite]' : '[data-library-pin]').textContent = project[field] ? (field === 'favorite' ? '★' : '●') : (field === 'favorite' ? '☆' : '○'); reorderLibrary(); }
  function reorderLibrary() { const grid = document.querySelector('#projects'); [...grid.children].sort((a,b) => Number(b.dataset.pinned === 'true') - Number(a.dataset.pinned === 'true') || Number(b.dataset.favorite === 'true') - Number(a.dataset.favorite === 'true')).forEach(card => grid.append(card)); }
  function filterLibrary() { const term = document.querySelector('#librarySearch').value.trim().toLowerCase(), status = document.querySelector('#libraryStatus').value, profile = document.querySelector('#libraryProfile').value; document.querySelectorAll('#projects .project-card').forEach(card => { const processing = !['ready','failed','cancelled'].includes(card.dataset.status); const statusMatch = !status || card.dataset.status === status || status === 'processing' && processing || status === 'favorite' && card.dataset.favorite === 'true'; card.classList.toggle('d-none', !(card.dataset.search.includes(term) && statusMatch && (!profile || card.dataset.profile === profile))); }); }
  function updateBatchButton() { const count = document.querySelectorAll('[data-library-select]:checked').length, button = document.querySelector('#libraryBatchClean'); button.classList.toggle('d-none', !count); button.textContent = `Liberar espaço de ${count} projeto${count === 1 ? '' : 's'}`; }
  async function cleanSelectedProjects() { const ids = [...document.querySelectorAll('[data-library-select]:checked')].map(input => input.closest('.project-card').dataset.id); if (!ids.length || !confirm(`Excluir os arquivos pesados de ${ids.length} projeto(s), mantendo todo o histórico?`)) return; try { const result = await api('/api/projects/delete-data-batch', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ projectIds: ids }) }); toast(`${bytesLabel(result.freedBytes)} liberados`); home(); } catch (error) { toast(error.message); } }
})();
