let professionalClipId = null;
const professionalRenderBase = renderProject;
const professionalSelectBase = selectClip;

renderProject = function (project) {
  professionalRenderBase(project);
  if (project.status !== 'ready') return;
  document.querySelector('#projectView')?.classList.add('professional-workspace');
  installWorkspaceTabs(project);
  const preview = document.querySelector('#preview');
  if (!preview) return;
  const shell = preview.parentElement;
  shell.classList.add('preview-column');
  shell.insertAdjacentHTML('afterbegin', `<div class="monitor-head"><div><span class="eyebrow">MONITOR DE PROGRAMA</span><strong id="monitorTitle">Selecione um corte</strong></div><button class="btn btn-sm btn-outline-light" onclick="toggleTheater()">Modo cinema</button></div>`);
  shell.insertAdjacentHTML('beforeend', `<div class="transport" role="toolbar" aria-label="Controles do vídeo"><button onclick="seekPreview(-5)">−5s</button><button onclick="seekPreview(-1)">−1s</button><button class="transport-play" onclick="togglePreview()">▶ / ❚❚</button><button onclick="seekPreview(1)">+1s</button><button onclick="seekPreview(5)">+5s</button><button onclick="markPreview('start')">Marcar entrada</button><button onclick="markPreview('end')">Marcar saída</button><button onclick="fullscreenPreview()">Tela cheia</button></div><div class="shortcut-help"><kbd>Espaço</kbd> reproduzir <kbd>←</kbd><kbd>→</kbd> navegar <kbd>I</kbd> entrada <kbd>O</kbd> saída</div>`);
  const first = project.clips[0]; if (first) selectClip(project, professionalClipId && project.clips.some(c => c.id === professionalClipId) ? professionalClipId : first.id);
};

function installWorkspaceTabs(project) {
  const root = document.querySelector('#projectView'), heading = root?.querySelector('.section-head');
  if (!root || !heading || root.querySelector('.editor-workspace-tabs')) return;
  heading.insertAdjacentHTML('afterend', `<div class="editor-workspace-tabs nav nav-pills" role="tablist"><button class="nav-link active" type="button" data-editor-tab="suggestions">Cortes sugeridos <span>${project.clips.filter(c => c.source !== 'manual').length}</span></button><button class="nav-link" type="button" data-editor-tab="source">Editar vídeo completo</button></div>`);
  root.insertAdjacentHTML('beforeend', fullSourceWorkspace(project));
  root.querySelectorAll('[data-editor-tab]').forEach(button => button.onclick = () => switchEditorTab(button.dataset.editorTab));
  const video = root.querySelector('#sourceVideo'), scrubber = root.querySelector('#sourceScrubber');
  video.addEventListener('loadedmetadata', () => { const duration = Number.isFinite(video.duration) ? video.duration : project.duration; scrubber.max = duration; root.querySelector('#sourceEnd').value = Math.min(duration, 75).toFixed(3); updateSourceClock(video, duration); });
  video.addEventListener('timeupdate', () => { scrubber.value = video.currentTime; updateSourceClock(video, video.duration || project.duration); });
  scrubber.oninput = () => { video.currentTime = +scrubber.value; updateSourceClock(video, video.duration || project.duration); };
  root.querySelector('#sourcePlay').onclick = () => video.paused ? video.play() : video.pause();
  root.querySelectorAll('[data-source-seek]').forEach(button => button.onclick = () => video.currentTime = Math.max(0, Math.min(video.duration || project.duration, video.currentTime + +button.dataset.sourceSeek)));
  root.querySelector('#markSourceIn').onclick = () => markSourceEdge('sourceStart');
  root.querySelector('#markSourceOut').onclick = () => markSourceEdge('sourceEnd');
  root.querySelector('#createManualClip').onclick = () => createManualClip(project);
}

function fullSourceWorkspace(project) {
  const max = Math.max(project.duration || 0, 1);
  return `<section id="sourceWorkspace" class="source-workspace d-none" aria-label="Editor do vídeo completo"><div class="source-monitor"><div class="monitor-head"><div><span class="eyebrow">VÍDEO-FONTE · ${escapeHtml(project.sourceKind === 'youTube' ? 'YOUTUBE' : 'ARQUIVO LOCAL')}</span><strong>${escapeHtml(project.name)}</strong></div><span class="source-duration">${time(project.duration || 0)}</span></div><video id="sourceVideo" preload="metadata" playsinline src="/api/projects/${project.id}/source"></video><div class="source-transport"><button id="sourcePlay" class="transport-play">▶ / ❚❚</button><button data-source-seek="-5">−5s</button><button data-source-seek="-1">−1s</button><button data-source-seek="1">+1s</button><button data-source-seek="5">+5s</button><span id="sourceClock">0:00 / ${time(project.duration || 0)}</span></div><input id="sourceScrubber" class="source-scrubber" type="range" min="0" max="${max}" step="0.033" value="0" aria-label="Posição no vídeo completo"><div class="transcript-density" aria-hidden="true">${sourceTimelineMarks(project)}</div></div><aside class="source-inspector"><span class="panel-number">IN / OUT</span><h3>Novo corte manual</h3><p class="text-secondary">Navegue pela fonte, marque o intervalo com precisão e salve para continuar editando. O vídeo só será renderizado quando você solicitar.</p><div class="row g-2"><div class="col"><label class="form-label" for="sourceStart">Entrada (s)</label><input id="sourceStart" class="form-control" type="number" min="0" max="${max}" step="0.001" value="0"></div><div class="col"><label class="form-label" for="sourceEnd">Saída (s)</label><input id="sourceEnd" class="form-control" type="number" min="0" max="${max}" step="0.001" value="${Math.min(max, 75).toFixed(3)}"></div></div><div class="d-grid gap-2 mt-3"><button id="markSourceIn" class="btn btn-outline-light">Marcar entrada no quadro atual <kbd>I</kbd></button><button id="markSourceOut" class="btn btn-outline-light">Marcar saída no quadro atual <kbd>O</kbd></button><button id="createManualClip" class="btn btn-gold btn-lg mt-2">Criar corte sem renderizar</button></div><div class="manual-note mt-4"><strong>Fluxo profissional</strong><span>1. Marque · 2. Crie · 3. Ajuste · 4. Renderize</span></div></aside></section>`;
}

function sourceTimelineMarks(project) { const duration = project.duration || 1; return (project.transcript || []).filter((_, index) => index % 3 === 0).map(segment => `<i style="left:${Math.min(100, segment.start / duration * 100)}%" title="${time(segment.start)}"></i>`).join(''); }
function switchEditorTab(tab) { const sourceMode = tab === 'source', root = document.querySelector('#projectView'); root.querySelector('.clip-layout')?.classList.toggle('d-none', sourceMode); root.querySelector('#sourceWorkspace')?.classList.toggle('d-none', !sourceMode); root.querySelectorAll('[data-editor-tab]').forEach(button => button.classList.toggle('active', button.dataset.editorTab === tab)); if (sourceMode) root.querySelector('#sourceVideo')?.focus(); }
function updateSourceClock(video, duration) { const clock = document.querySelector('#sourceClock'); if (clock) clock.textContent = `${time(video.currentTime)} / ${time(duration || 0)}`; }
function markSourceEdge(fieldId) { const video = document.querySelector('#sourceVideo'), field = document.querySelector(`#${fieldId}`); if (!video || !field) return; field.value = video.currentTime.toFixed(3); toast(fieldId === 'sourceStart' ? 'Entrada marcada' : 'Saída marcada'); }
async function createManualClip(project) { const button = document.querySelector('#createManualClip'), start = +document.querySelector('#sourceStart').value, end = +document.querySelector('#sourceEnd').value; button.disabled = true; button.textContent = 'Criando corte…'; try { const clip = await api(`/api/projects/${project.id}/clips/manual`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ start, end }) }); professionalClipId = clip.id; toast('Corte manual criado sem renderizar'); await openProject(project.id); } catch (error) { toast(error.message); button.disabled = false; button.textContent = 'Criar corte sem renderizar'; } }

selectClip = function (project, id) {
  professionalClipId = id; professionalSelectBase(project, id);
  const clip = project.clips.find(item => item.id === id); if (!clip) return;
  document.querySelector('#monitorTitle')?.replaceChildren(document.createTextNode(`${clip.title} · ${time(clip.end - clip.start)}`));
  document.querySelectorAll('.clip-card').forEach(card => card.classList.toggle('editor-collapsed', card.dataset.clip !== id));
  const video = document.querySelector('#preview video'); if (video) { video.autoplay = false; video.muted = false; video.playsInline = true; }
};

function previewVideo() { return document.querySelector('#preview video'); }
function activeEditorCard() { return document.querySelector(`.clip-card[data-clip="${professionalClipId}"]`); }
function seekPreview(seconds) { const video = previewVideo(); if (video) video.currentTime = Math.max(0, Math.min(video.duration || Infinity, video.currentTime + seconds)); }
function togglePreview() { const video = previewVideo(); if (!video) return toast('Renderize o corte para visualizar o vídeo final'); video.paused ? video.play() : video.pause(); }
function markPreview(edge) {
  const video = previewVideo(), card = activeEditorCard(), clip = current?.clips.find(item => item.id === professionalClipId); if (!video || !card || !clip) return toast('Renderize o corte antes de marcar pelo monitor');
  const absolute = video.dataset.sourcePreview ? video.currentTime : clip.start + video.currentTime; const field = card.querySelector(`[name="${edge}"]`); const timeline = card.querySelector(`[name="timeline${edge[0].toUpperCase() + edge.slice(1)}"]`);
  if (field) field.value = absolute.toFixed(1); if (timeline) { timeline.value = absolute.toFixed(1); syncTimeline(timeline, edge); }
  toast(edge === 'start' ? 'Entrada marcada no quadro atual' : 'Saída marcada no quadro atual');
}
function toggleTheater() { document.querySelector('.professional-workspace')?.classList.toggle('theater-mode'); }
function fullscreenPreview() { const preview = document.querySelector('#preview'); if (preview?.requestFullscreen) preview.requestFullscreen(); }

document.addEventListener('keydown', event => {
  if (!document.querySelector('.professional-workspace') || ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) return;
  if (!document.querySelector('#sourceWorkspace')?.classList.contains('d-none')) {
    const video = document.querySelector('#sourceVideo');
    if (event.code === 'Space') { event.preventDefault(); video?.paused ? video.play() : video?.pause(); }
    else if (event.key === 'ArrowLeft' && video) video.currentTime = Math.max(0, video.currentTime - (event.shiftKey ? 5 : 1));
    else if (event.key === 'ArrowRight' && video) video.currentTime = Math.min(video.duration || Infinity, video.currentTime + (event.shiftKey ? 5 : 1));
    else if (event.key.toLowerCase() === 'i') markSourceEdge('sourceStart');
    else if (event.key.toLowerCase() === 'o') markSourceEdge('sourceEnd');
    return;
  }
  if (event.code === 'Space') { event.preventDefault(); togglePreview(); }
  else if (event.key === 'ArrowLeft') seekPreview(event.shiftKey ? -5 : -1);
  else if (event.key === 'ArrowRight') seekPreview(event.shiftKey ? 5 : 1);
  else if (event.key.toLowerCase() === 'i') markPreview('start');
  else if (event.key.toLowerCase() === 'o') markPreview('end');
});
