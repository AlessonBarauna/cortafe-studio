const scoredClipCard = clipCard;
clipCard = function (project, clip, index) {
  const scores = clip.scoreBreakdown || {};
  const items = [['Gancho', scores.hook], ['Clareza', scores.clarity], ['Emoção', scores.emotion], ['Aplicação', scores.practicalValue], ['Conclusão', scores.completion], ['Compartilhar', scores.shareability]];
  const detail = `<div class="d-flex flex-wrap gap-1 mt-2">${items.map(([label, value]) => `<span class="badge text-bg-dark">${label} ${value ?? '–'}</span>`).join('')}</div>`;
  const hook = clip.hookSentence ? `<p class="mt-3 mb-1"><span class="eyebrow">GANCHO</span><br>“${escapeHtml(clip.hookSentence)}”</p>` : '';
  return scoredClipCard(project, clip, index).replace('<input class="form-control fs-5', `${detail}${hook}<input class="form-control fs-5`);
};
