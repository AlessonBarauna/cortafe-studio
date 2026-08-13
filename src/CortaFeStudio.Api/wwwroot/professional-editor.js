let professionalClipId = null;
const professionalRenderBase = renderProject;
const professionalSelectBase = selectClip;

renderProject = function (project) {
  professionalRenderBase(project);
  if (project.status !== 'ready') return;
  document.querySelector('#projectView')?.classList.add('professional-workspace');
  const preview = document.querySelector('#preview');
  if (!preview) return;
  const shell = preview.parentElement;
  shell.classList.add('preview-column');
  shell.insertAdjacentHTML('afterbegin', `<div class="monitor-head"><div><span class="eyebrow">MONITOR DE PROGRAMA</span><strong id="monitorTitle">Selecione um corte</strong></div><button class="btn btn-sm btn-outline-light" onclick="toggleTheater()">Modo cinema</button></div>`);
  shell.insertAdjacentHTML('beforeend', `<div class="transport" role="toolbar" aria-label="Controles do vídeo"><button onclick="seekPreview(-5)">−5s</button><button onclick="seekPreview(-1)">−1s</button><button class="transport-play" onclick="togglePreview()">▶ / ❚❚</button><button onclick="seekPreview(1)">+1s</button><button onclick="seekPreview(5)">+5s</button><button onclick="markPreview('start')">Marcar entrada</button><button onclick="markPreview('end')">Marcar saída</button><button onclick="fullscreenPreview()">Tela cheia</button></div><div class="shortcut-help"><kbd>Espaço</kbd> reproduzir <kbd>←</kbd><kbd>→</kbd> navegar <kbd>I</kbd> entrada <kbd>O</kbd> saída</div>`);
  const first = project.clips[0]; if (first) selectClip(project, professionalClipId && project.clips.some(c => c.id === professionalClipId) ? professionalClipId : first.id);
};

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
  const absolute = clip.start + video.currentTime; const field = card.querySelector(`[name="${edge}"]`); const timeline = card.querySelector(`[name="timeline${edge[0].toUpperCase() + edge.slice(1)}"]`);
  if (field) field.value = absolute.toFixed(1); if (timeline) { timeline.value = absolute.toFixed(1); syncTimeline(timeline, edge); }
  toast(edge === 'start' ? 'Entrada marcada no quadro atual' : 'Saída marcada no quadro atual');
}
function toggleTheater() { document.querySelector('.professional-workspace')?.classList.toggle('theater-mode'); }
function fullscreenPreview() { const preview = document.querySelector('#preview'); if (preview?.requestFullscreen) preview.requestFullscreen(); }

document.addEventListener('keydown', event => {
  if (!document.querySelector('.professional-workspace') || ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) return;
  if (event.code === 'Space') { event.preventDefault(); togglePreview(); }
  else if (event.key === 'ArrowLeft') seekPreview(event.shiftKey ? -5 : -1);
  else if (event.key === 'ArrowRight') seekPreview(event.shiftKey ? 5 : 1);
  else if (event.key.toLowerCase() === 'i') markPreview('start');
  else if (event.key.toLowerCase() === 'o') markPreview('end');
});
