function bestRenderedClip(project) {
  return (project.clips || [])
    .filter(clip => clip.videoPath)
    .sort((left, right) => (Number(right.score) || 0) - (Number(left.score) || 0))[0] || null;
}

function installProjectPreviews() {
  document.querySelectorAll('#projects .project-card[data-id]').forEach(card => {
    if (card.querySelector('.project-hover-preview')) return;
    const project = projects.find(item => item.id === card.dataset.id);
    const clip = project ? bestRenderedClip(project) : null;
    if (!clip) return;

    const source = `/api/projects/${encodeURIComponent(project.id)}/assets/${clip.videoPath.split('/').map(encodeURIComponent).join('/')}`;
    card.insertAdjacentHTML('afterbegin', `<div class="project-hover-preview" aria-hidden="true">
      <video muted loop playsinline preload="none" data-preview-src="${escapeHtml(source)}"></video>
      <div class="project-preview-shade"></div>
      <span class="project-preview-label"><i>▶</i> Melhor corte · ${Math.round(Number(clip.score) || 0)} pts</span>
    </div>`);

    const video = card.querySelector('video');
    const start = async () => {
      if (!video.src) video.src = video.dataset.previewSrc;
      try { await video.play(); } catch { /* autoplay silencioso pode ser limitado pelo navegador */ }
    };
    const stop = () => {
      video.pause();
      if (Number.isFinite(video.duration)) video.currentTime = 0;
    };
    card.addEventListener('mouseenter', start);
    card.addEventListener('mouseleave', stop);
    card.addEventListener('focusin', start);
    card.addEventListener('focusout', stop);
  });
}

const homeWithProjectPreviews = home;
home = async function () {
  await homeWithProjectPreviews();
  installProjectPreviews();
};

setTimeout(installProjectPreviews, 500);
