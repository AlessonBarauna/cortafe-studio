const scoredClipCard = clipCard;
clipCard = function (project, clip, index) {
  const social = clip.socialScore || {};
  const items = [
    ['Gancho', social.hook],
    ['Retenção', social.retention],
    ['Conclusão', social.conclusion]
  ];
  const potential = Number.isFinite(Number(social.potential)) ? Number(social.potential) : null;
  const potentialClass = potential >= 85 ? 'text-bg-success' : potential >= 70 ? 'text-bg-warning' : 'text-bg-secondary';
  const detail = `<div class="d-flex flex-wrap gap-1 mt-2">${items.map(([label, value]) => `<span class="badge text-bg-dark border border-secondary">${label} ${value ?? '–'}</span>`).join('')}${potential === null ? '' : `<span class="badge ${potentialClass}">Potencial social ${potential}</span>`}</div>`;
  const hook = clip.hookSentence ? `<p class="mt-3 mb-1"><span class="eyebrow">GANCHO</span><br>“${escapeHtml(clip.hookSentence)}”</p>` : '';
  return scoredClipCard(project, clip, index).replace('<input class="form-control fs-5', `${detail}${hook}<input class="form-control fs-5`);
};
