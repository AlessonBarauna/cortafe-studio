const timelineClipCardBase = clipCard;
const timelineSaveBase = saveClip;

clipCard = function (project, clip, index) {
  const duration = Math.max(1, project.duration || clip.end);
  const selectedWords = (project.transcript || []).flatMap(segment => segment.words || []).filter(word => word.end >= clip.start && word.start <= clip.end);
  const bars = Array.from({ length: 72 }, (_, i) => {
    const word = selectedWords[Math.floor(i / 72 * Math.max(1, selectedWords.length))];
    const height = word ? 22 + ((word.word || '').length * 11 + i * 7) % 70 : 18 + (i * 13) % 34;
    return `<i style="height:${height}%"></i>`;
  }).join('');
  const transcript = clip.editedTranscript || clip.transcript || '';
  const timeline = `<section class="timeline-editor mt-3" onclick="event.stopPropagation()">
    <div class="timeline-heading"><span class="eyebrow">LINHA DO TEMPO</span><strong data-duration>${time(clip.end - clip.start)}</strong></div>
    <div class="waveform">${bars}</div>
    <div class="range-stack">
      <input aria-label="Início do corte" name="timelineStart" type="range" min="0" max="${duration}" step=".1" value="${clip.start}" oninput="syncTimeline(this,'start')">
      <input aria-label="Fim do corte" name="timelineEnd" type="range" min="0" max="${duration}" step=".1" value="${clip.end}" oninput="syncTimeline(this,'end')">
    </div>
    <div class="timeline-times"><span data-start-label>${time(clip.start)}</span><span data-end-label>${time(clip.end)}</span></div>
    <label class="form-label mt-3">Transcrição usada nas legendas</label>
    <textarea class="form-control transcript-editor" name="editedTranscript" rows="5">${escapeHtml(transcript)}</textarea>
    <div class="d-flex flex-wrap gap-2 mt-3"><button type="button" class="btn btn-sm btn-outline-light" onclick="duplicateClip('${project.id}','${clip.id}')">Duplicar</button><button type="button" class="btn btn-sm btn-outline-light" onclick="splitClip('${project.id}','${clip.id}')">Dividir no centro</button><button type="button" class="btn btn-sm btn-outline-secondary" onclick="resetTranscript(this)">Restaurar texto</button></div>
  </section>`;
  return timelineClipCardBase(project, clip, index).replace('<p class="clip-transcript mt-3">', timeline + '<p class="clip-transcript mt-3">');
};

saveClip = async function (project, card) {
  const clip = project.clips.find(item => item.id === card.dataset.clip);
  clip.editedTranscript = card.querySelector('[name="editedTranscript"]')?.value || null;
  await timelineSaveBase(project, card);
  await api(`/api/projects/${project.id}/clips/${clip.id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ editedTranscript: clip.editedTranscript }) });
};

function syncTimeline(input, edge) {
  const card = input.closest('.clip-card');
  const start = card.querySelector('[name="timelineStart"]'); const end = card.querySelector('[name="timelineEnd"]');
  if (+end.value - +start.value < 3) input.value = edge === 'start' ? +end.value - 3 : +start.value + 3;
  card.querySelector('[name="start"]').value = (+start.value).toFixed(1); card.querySelector('[name="end"]').value = (+end.value).toFixed(1);
  card.querySelector('[data-start-label]').textContent = time(+start.value); card.querySelector('[data-end-label]').textContent = time(+end.value);
  card.querySelector('[data-duration]').textContent = time(+end.value - +start.value);
}

function resetTranscript(button) { const card = button.closest('.clip-card'); const clip = current.clips.find(item => item.id === card.dataset.clip); card.querySelector('[name="editedTranscript"]').value = clip.transcript; }
async function duplicateClip(projectId, clipId) { try { await api(`/api/projects/${projectId}/clips/${clipId}/duplicate`, { method: 'POST' }); toast('Corte duplicado'); openProject(projectId); } catch (error) { toast(error.message); } }
async function splitClip(projectId, clipId) { const clip = current.clips.find(item => item.id === clipId); const at = clip.start + (clip.end - clip.start) / 2; try { await api(`/api/projects/${projectId}/clips/${clipId}/split`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ at }) }); toast('Corte dividido em duas partes'); openProject(projectId); } catch (error) { toast(error.message); } }
