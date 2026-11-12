// Modo Simples: editor de cortes no estilo CapCut / TikTok.
// Abre por cima da tela de projeto pronto. O editor completo continua disponível no botão "Avançado".
(() => {
  const MODE_KEY = 'cortafe-editor-mode';
  const STYLES = [
    ['impact', 'Impacto'], ['sermon', 'Pregação'], ['bold', 'Negrito'], ['clean', 'Limpo'],
    ['minimal', 'Mínimo'], ['podcast', 'Podcast'], ['motivational', 'Motivacional'], ['worship', 'Louvor']
  ];
  const LAYOUTS = [['fill', 'Preencher'], ['blur', 'Fundo desfocado'], ['split', 'Dividido']];
  const FORMATS = [['vertical', '9:16', '9/16'], ['portrait', '4:5', '4/5'], ['square', '1:1', '1/1'], ['landscape', '16:9', '16/9']];
  const TRANSITIONS = [['smooth', 'Suave'], ['editorial', 'Editorial'], ['dynamic', 'Dinâmica']];
  const THEMES = [['amado-jesus', 'Amado Jesus'], ['podcast', 'Podcast'], ['worship', 'Louvor']];
  const SPEEDS = [[1, '1×'], [1.25, '1,25×'], [1.5, '1,5×']];
  const TOOLS = [['cortar', '✂', 'Cortar'], ['legendas', 'CC', 'Legendas'], ['texto', 'T', 'Texto'], ['efeitos', '✦', 'Efeitos'], ['marca', '◈', 'Marca']];
  const MIN_LEN = 5;
  const FILTERS = [['none', 'Original', ''], ['vivid', 'Vívido', 'saturate(1.35) contrast(1.08)'], ['cinema', 'Cinema', 'contrast(1.12) saturate(.9) sepia(.15) hue-rotate(-8deg)'],
    ['bw', 'P&B', 'grayscale(1) contrast(1.12)'], ['warm', 'Quente', 'sepia(.25) saturate(1.1)'], ['cool', 'Frio', 'hue-rotate(12deg) saturate(1.05) brightness(1.02)'],
    ['drama', 'Drama', 'contrast(1.25) saturate(1.1) brightness(.97)'], ['soft', 'Suave', 'contrast(.94) saturate(.92) brightness(1.03) blur(.4px)']];
  const filterCss = n => (FILTERS.find(f => f[0] === n) || FILTERS[0])[2];

  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const fmt = t => { t = Math.max(0, t || 0); return `${Math.floor(t / 60)}:${(t % 60).toFixed(1).padStart(4, '0')}`; };
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const $ = (sel, el) => (el || root).querySelector(sel);
  const $$ = (sel, el) => [...(el || root).querySelectorAll(sel)];
  const say = m => { try { toast(m); } catch { console.log(m); } };

  let root = null, P = null, clipId = null, tool = 'cortar';
  let tracks = {}, rendering = new Set(), showRendered = true, queue = Promise.resolve(), timers = {};
  let video = null, raf = 0, dur = 0;

  const clip = () => P?.clips.find(c => c.id === clipId);
  const asset = path => `/api/projects/${P.id}/assets/${String(path).split('/').map(encodeURIComponent).join('/')}`;
  const isMode = () => sessionStorage.getItem(MODE_KEY) || 'simple';
  const clipDur = c => Math.max(0, c.end - c.start);

  // ---------- API ----------
  function save(patch) {
    const c = clip(); if (!c) return;
    Object.assign(c, patch);
    clearTimeout(timers.save);
    timers.save = setTimeout(() => {
      const body = {
        start: c.start, end: c.end, title: c.title, coverText: c.coverText, caption: c.caption, approved: c.approved,
        cropX: c.cropX, layoutMode: c.layoutMode, outputPreset: c.outputPreset, brandFrameEnabled: c.brandFrameEnabled,
        brandTheme: c.brandTheme, watermarkEnabled: c.watermarkEnabled, watermarkText: c.watermarkText,
        watermarkOpacity: c.watermarkOpacity, playbackSpeed: c.playbackSpeed || 1,
        silenceTrimmingEnabled: c.silenceTrimmingEnabled, transitionStyle: c.transitionStyle, animatedFx: c.animatedFx,
        fxSfx: c.fxSfx, fxHook: c.fxHook, fxThemes: c.fxThemes, colorFilter: c.colorFilter || 'none'
      };
      const id = c.id;
      queue = queue.then(async () => {
        try {
          const r = await fetch(`/api/projects/${P.id}/clips/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
          if (!r.ok) throw new Error('Não foi possível salvar.');
          const up = (await r.json()).clips.find(x => x.id === id);
          const mine = P.clips.find(x => x.id === id);
          if (up && mine) { mine.renderOutdated = up.renderOutdated; mine.videoPath = up.videoPath; mine.start = up.start; mine.end = up.end; }
          paintClips(); paintActions();
        } catch (e) { say(e.message); }
      });
    }, 350);
  }

  async function loadTrack(id) {
    if (tracks[id]) return tracks[id];
    try { tracks[id] = await (await fetch(`/api/projects/${P.id}/clips/${id}/subtitles`)).json(); }
    catch { tracks[id] = null; }
    return tracks[id];
  }
  function saveTrack(id) {
    clearTimeout(timers['t' + id]);
    timers['t' + id] = setTimeout(() => queue = queue.then(async () => {
      try {
        const r = await fetch(`/api/projects/${P.id}/clips/${id}/subtitles`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(tracks[id]) });
        const data = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(data.error || 'Não foi possível salvar as legendas.');
        const mine = P.clips.find(x => x.id === id); if (mine && mine.videoPath) mine.renderOutdated = true;
        paintClips(); paintActions();
      } catch (e) { say(e.message); }
    }), 500);
  }

  // ---------- Layout ----------
  function build() {
    root = document.createElement('div');
    root.id = 'sx'; root.className = 'sx';
    root.innerHTML = `
      <header class="sx-top">
        <button class="sx-btn ghost" data-act="back">← <span>Biblioteca</span></button>
        <div class="sx-name"><small>MODO SIMPLES</small><strong>${esc(P.name)}</strong></div>
        <div class="sx-top-actions">
          <button class="sx-btn ghost" data-act="zip">⬇ <span>ZIP</span></button>
          <button class="sx-btn ghost" data-act="all">▶ <span>Gerar todos</span></button>
          <button class="sx-btn outline" data-act="advanced">⚙ <span>Avançado</span></button>
        </div>
      </header>
      <section class="sx-clips" id="sxClips"></section>
      <main class="sx-stage"><div class="sx-phone-wrap"><div class="sx-phone" id="sxPhone"></div></div><div class="sx-transport" id="sxTransport"></div><div class="sx-trim" id="sxTrim"></div></main>
      <aside class="sx-side"><nav class="sx-tabs" id="sxTabs"></nav><div class="sx-panel" id="sxPanel"></div><footer class="sx-actions" id="sxActions"></footer></aside>`;
    document.body.appendChild(root);
    document.body.classList.add('sx-open');
    root.addEventListener('click', onClick);
    $('#sxClips').addEventListener('click', e => { const b = e.target.closest('[data-clip]'); if (b) select(b.dataset.clip); });
  }

  function onClick(e) {
    const a = e.target.closest('[data-act]'); if (!a) return;
    const act = a.dataset.act;
    if (act === 'back') { close(); if (typeof home === 'function') home(); }
    else if (act === 'advanced') goAdvanced();
    else if (act === 'zip') location.assign(`/api/projects/${P.id}/exports/clips.zip`);
    else if (act === 'all') renderAll();
    else if (act === 'play') togglePlay();
    else if (act === 'approve') { const c = clip(); save({ approved: !c.approved }); fetch(`/api/projects/${P.id}/clips/${c.id}/feedback`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ feedback: c.approved ? 'approved' : 'rejected' }) }).catch(() => {}); paintActions(); paintClips(); }
    else if (act === 'render') renderOne(clipId);
    else if (act === 'fxoff') { save({ animatedFx: false }); applyLook(); paintPanel(); }
    else if (act === 'toggleView') { showRendered = !showRendered; paintStage(); paintActions(); }
  }

  function open(p) {
    if (root) close();
    P = p;
    const order = [...P.clips].sort((a, b) => (b.score || 0) - (a.score || 0));
    P.clips = order;
    build();
    clipId = clipId && P.clips.some(c => c.id === clipId) ? clipId : P.clips[0]?.id;
    paintClips(); paintTabs();
    if (clipId) select(clipId, true);
    else $('#sxPanel').innerHTML = '<p class="sx-empty">Este projeto ainda não tem cortes.</p>';
  }

  function close() {
    cancelAnimationFrame(raf); video = null;
    if (root) root.remove(); root = null;
    document.body.classList.remove('sx-open');
  }

  function goAdvanced() {
    sessionStorage.setItem(MODE_KEY, 'advanced');
    try { current = P; } catch { /* ignora */ }
    close(); showSwitch();
    if (typeof renderProject === 'function') renderProject(P);
  }

  function showSwitch() {
    if (document.getElementById('sxSwitch')) return;
    const b = document.createElement('button');
    b.id = 'sxSwitch'; b.className = 'sx-switch'; b.textContent = '✦ Modo Simples';
    b.onclick = () => { sessionStorage.setItem(MODE_KEY, 'simple'); b.remove(); open(current && current.clips ? current : P); };
    document.body.appendChild(b);
  }

  // ---------- Clip carousel ----------
  function paintClips() {
    if (!root) return;
    $('#sxClips').innerHTML = P.clips.map((c, i) => {
      const cover = c.coverPath ? `style="background-image:url('${asset(c.coverPath)}')"` : '';
      const state = rendering.has(c.id) ? '<i class="dot busy" title="Gerando"></i>' : c.videoPath && !c.renderOutdated ? '<i class="dot ok" title="Vídeo pronto"></i>' : c.videoPath ? '<i class="dot warn" title="Alterado: gere de novo"></i>' : '';
      return `<button class="sx-clip${c.id === clipId ? ' on' : ''}" data-clip="${c.id}"><span class="thumb" ${cover}><b>${String(i + 1).padStart(2, '0')}</b>${c.approved ? '<em>✓</em>' : ''}${state}</span><span class="meta"><strong>${esc(c.title || 'Corte ' + (i + 1))}</strong><small>${Math.round(clipDur(c))}s · nota ${Math.round(c.score || 0)}</small></span></button>`;
    }).join('');
    const on = $('.sx-clip.on'); if (on && on.scrollIntoView) on.scrollIntoView({ block: 'nearest', inline: 'center' });
  }

  async function select(id, first) {
    clipId = id; paintClips(); paintStage(); paintTrim(); paintPanel(); paintActions();
    await loadTrack(id); if (clipId === id) { paintPanel(); }
  }

  // ---------- Preview ----------
  function previewSrc(c) {
    const ready = c.videoPath && !c.renderOutdated && showRendered;
    return ready ? { url: asset(c.videoPath), rendered: true } : { url: `/api/projects/${P.id}/source`, rendered: false };
  }

  function paintStage() {
    const c = clip(); if (!c || !root) return;
    cancelAnimationFrame(raf);
    const ph = $('#sxPhone'), src = previewSrc(c), fmtInfo = FORMATS.find(f => f[0] === (c.outputPreset || 'vertical')) || FORMATS[0];
    ph.style.aspectRatio = fmtInfo[2];
    ph.dataset.layout = src.rendered ? 'fill' : (c.layoutMode || 'fill');
    ph.style.backgroundImage = c.coverPath ? `url('${asset(c.coverPath)}')` : '';
    ph.innerHTML = `<video playsinline preload="metadata" ${src.rendered ? '' : ''} src="${src.url}"></video>
      <div class="sx-brand" id="sxBrand"></div><div class="sx-cap" id="sxCap"></div><div class="sx-guide" id="sxGuide"><i></i><i></i></div><div class="sx-badge" id="sxBadge"></div>
      <button class="sx-play" data-act="play" aria-label="Reproduzir">▶</button>`;
    video = $('video', ph); bindCaptionDrag();
    video.addEventListener('loadedmetadata', () => {
      dur = (src.rendered ? P.duration : video.duration) || P.duration || video.duration || 0;
      if (!src.rendered) { video.currentTime = c.start; }
      video.playbackRate = src.rendered ? 1 : (c.playbackSpeed || 1);
      paintTrim(); applyLook();
    });
    video.addEventListener('error', () => { ph.classList.add('novideo'); $('#sxBadge').textContent = 'Prévia indisponível (use Gerar vídeo)'; });
    video.addEventListener('play', () => { ph.classList.add('playing'); tick(); });
    video.addEventListener('pause', () => ph.classList.remove('playing'));
    ph.onclick = e => { if (!e.target.closest('button')) togglePlay(); };
    applyLook();
  }

  function applyLook() {
    const c = clip(); if (!c || !root) return;
    const ph = $('#sxPhone'), rendered = !!(c.videoPath && !c.renderOutdated && showRendered);
    ph.dataset.layout = rendered ? 'fill' : (c.layoutMode || 'fill');
    if (video) video.style.filter = rendered ? '' : filterCss(c.colorFilter);
    if (video) video.style.objectPosition = rendered ? '50% 50%' : `${clamp((c.cropX ?? .5) * 100, 0, 100)}% 50%`;
    const b = $('#sxBrand');
    b.className = 'sx-brand' + (!rendered && c.brandFrameEnabled ? ' frame theme-' + (c.brandTheme || 'amado-jesus') : '');
    b.innerHTML = !rendered && c.watermarkEnabled && c.watermarkText ? `<span style="opacity:${c.watermarkOpacity ?? .6}">${esc(c.watermarkText)}</span>` : '';
    const bd = $('#sxBadge');
    if (!ph.classList.contains('novideo')) bd.textContent = rendered ? '● Vídeo final' : c.animatedFx ? '✦ Prévia · efeitos entram ao gerar' : 'Prévia';
    bd.className = 'sx-badge' + (rendered ? ' final' : '');
    paintCaption(video ? video.currentTime : c.start);
  }

  function paintCaption(t) {
    const c = clip(), cap = $('#sxCap'); if (!c || !cap) return;
    const track = tracks[c.id], rendered = c.videoPath && !c.renderOutdated && showRendered;
    if (rendered || !track || !track.enabled) { cap.textContent = ''; cap.className = 'sx-cap'; return; }
    const rel = t - c.start, blocks = (track.blocks || []).filter(b => b.enabled !== false);
    // Sempre mostra uma legenda: a do momento atual ou, se não houver, a mais próxima (assim o estilo aparece na hora).
    let blk = blocks.find(b => rel >= b.start && rel <= b.end);
    if (!blk && blocks.length) blk = blocks.reduce((best, b) => Math.abs((b.start + b.end) / 2 - rel) < Math.abs((best.start + best.end) / 2 - rel) ? b : best, blocks[0]);
    if (!dragging) cap.textContent = blk ? blk.text : 'Sua legenda aparece aqui';
    const fxLook = c.animatedFx && (c.outputPreset || 'vertical') === 'vertical';
    cap.className = `sx-cap ${fxLook ? 'st-fx' : 'st-' + (track.style || 'impact')}${dragging ? ' drag' : ''}`;
    cap.style.top = `${clamp(track.positionY ?? 72, 8, 92)}%`;
    cap.style.left = `${clamp(track.positionX ?? 50, 15, 85)}%`;
  }

  let dragging = false;
  function bindCaptionDrag() {
    const cap = $('#sxCap'), ph = $('#sxPhone');
    cap.onpointerdown = e => {
      const c = clip(), track = tracks[c && c.id]; if (!track) return;
      e.preventDefault(); e.stopPropagation(); dragging = true; moved = false;
      cap.setPointerCapture(e.pointerId); if (video) video.pause();
      const guide = $('#sxGuide'); guide.className = 'sx-guide on';
      cap.onpointermove = ev => {
        const r = ph.getBoundingClientRect();
        let x = clamp((ev.clientX - r.left) / r.width * 100, 15, 85), y = clamp((ev.clientY - r.top) / r.height * 100, 8, 92);
        const sx = Math.abs(x - 50) < 3, sy = Math.abs(y - 50) < 3;
        if (sx) x = 50; if (sy) y = 50;
        guide.className = 'sx-guide on' + (sx ? ' vx' : '') + (sy ? ' hy' : '');
        track.positionX = Math.round(x * 10) / 10; track.positionY = Math.round(y * 10) / 10; moved = true;
        paintCaption(video ? video.currentTime : c.start);
      };
      cap.onpointerup = () => {
        cap.onpointermove = cap.onpointerup = null; dragging = false; guide.className = 'sx-guide';
        if (moved) { saveTrack(c.id); if (tool === 'legendas') paintPanel(); } else { tool = 'legendas'; paintTabs(); paintPanel(); }
        paintCaption(video ? video.currentTime : c.start);
      };
    };
  }
  let moved = false;

  function tick() {
    cancelAnimationFrame(raf);
    const c = clip(); if (!video || !c) return;
    const rendered = c.videoPath && !c.renderOutdated && showRendered;
    if (!rendered && (video.currentTime >= c.end - .05 || video.currentTime < c.start - .5)) video.currentTime = c.start;
    paintCaption(video.currentTime);
    updateHead();
    if (!video.paused) raf = requestAnimationFrame(tick);
  }

  function togglePlay() {
    if (!video) return;
    if (video.paused) video.play().catch(() => say('Toque novamente para reproduzir.')); else video.pause();
  }

  // ---------- Trim bar ----------
  const windowOf = c => {
    const total = dur || P.duration || c.end + 30;
    const pad = Math.max(20, clipDur(c) * .8);
    const a = Math.max(0, c.start - pad), b = Math.min(total, c.end + pad);
    return [a, Math.max(b, a + 10)];
  };

  function paintTrim() {
    const c = clip(); if (!c || !root) return;
    const [a, b] = windowOf(c), span = b - a;
    const l = (c.start - a) / span * 100, w = (c.end - c.start) / span * 100;
    $('#sxTrim').innerHTML = `
      <div class="sx-trim-info"><span>Início <b>${fmt(c.start)}</b></span><span class="len">${clipDur(c).toFixed(1)}s${c.playbackSpeed > 1 ? ` → ${(clipDur(c) / c.playbackSpeed).toFixed(1)}s` : ''}</span><span>Fim <b>${fmt(c.end)}</b></span></div>
      <div class="sx-track" id="sxTrack"><div class="sx-sel" id="sxSel" style="left:${l}%;width:${w}%"><i class="h l" data-h="start"></i><i class="h r" data-h="end"></i></div><i class="sx-head" id="sxHead"></i></div>
      <div class="sx-trim-hint">Arraste as pontas para ajustar o início e o fim. Arraste o meio para mover o corte.</div>`;
    bindTrim(a, span); updateHead();
  }

  function updateHead() {
    const c = clip(), h = $('#sxHead'); if (!c || !h || !video) return;
    const [a, b] = windowOf(c);
    h.style.left = `${clamp((video.currentTime - a) / (b - a) * 100, 0, 100)}%`;
  }

  function bindTrim(a, span) {
    const track = $('#sxTrack'), sel = $('#sxSel');
    sel.onpointerdown = e => {
      const c = clip(), mode = e.target.dataset.h || 'move', rect = track.getBoundingClientRect();
      const t0 = a + (e.clientX - rect.left) / rect.width * span, s0 = c.start, e0 = c.end, total = dur || P.duration || e0 + 60;
      sel.setPointerCapture(e.pointerId); if (video) video.pause();
      sel.onpointermove = ev => {
        const d = a + (ev.clientX - rect.left) / rect.width * span - t0;
        if (mode === 'start') c.start = clamp(s0 + d, 0, e0 - MIN_LEN);
        else if (mode === 'end') c.end = clamp(e0 + d, s0 + MIN_LEN, total);
        else { const len = e0 - s0; c.start = clamp(s0 + d, 0, total - len); c.end = c.start + len; }
        c.start = Math.round(c.start * 10) / 10; c.end = Math.round(c.end * 10) / 10;
        const l = (c.start - a) / span * 100, w = (c.end - c.start) / span * 100;
        sel.style.left = `${l}%`; sel.style.width = `${w}%`;
        $('.sx-trim-info', root).innerHTML = `<span>Início <b>${fmt(c.start)}</b></span><span class="len">${clipDur(c).toFixed(1)}s</span><span>Fim <b>${fmt(c.end)}</b></span>`;
        if (video && !video.error) video.currentTime = mode === 'end' ? Math.max(c.start, c.end - 1) : c.start;
      };
      sel.onpointerup = () => { sel.onpointermove = sel.onpointerup = null; save({ start: c.start, end: c.end }); paintTrim(); paintPanel(); if (video) video.currentTime = c.start; };
    };
  }

  // ---------- Tools ----------
  function paintTabs() {
    $('#sxTabs').innerHTML = TOOLS.map(([k, ico, name]) => `<button class="${k === tool ? 'on' : ''}" data-tool="${k}"><i>${ico}</i><span>${name}</span></button>`).join('');
    $$('#sxTabs button').forEach(b => b.onclick = () => { tool = b.dataset.tool; paintTabs(); paintPanel(); });
  }

  const chips = (name, list, cur) => `<div class="sx-chips">${list.map(([v, label]) => `<button class="${String(cur) === String(v) ? 'on' : ''}" data-set="${name}" data-v="${v}">${label}</button>`).join('')}</div>`;
  const sw = (name, label, on, hint) => `<label class="sx-switch-row"><span><b>${label}</b>${hint ? `<small>${hint}</small>` : ''}</span><input type="checkbox" data-sw="${name}" ${on ? 'checked' : ''}><i></i></label>`;

  function paintPanel() {
    const c = clip(), el = $('#sxPanel'); if (!c || !el) return;
    const track = tracks[c.id];
    let h = '';
    if (tool === 'cortar') {
      h = `<h4>Duração e ritmo</h4>
        <div class="sx-fine"><span>Início</span><button data-nudge="start:-0.5">−0,5s</button><button data-nudge="start:0.5">+0,5s</button></div>
        <div class="sx-fine"><span>Fim</span><button data-nudge="end:-0.5">−0,5s</button><button data-nudge="end:0.5">+0,5s</button></div>
        <h4>Velocidade</h4>${chips('speed', SPEEDS, c.playbackSpeed || 1)}
        ${sw('silence', 'Cortar silêncios', c.silenceTrimmingEnabled, 'Remove pausas longas automaticamente')}`;
    } else if (tool === 'legendas') {
      if (!track) h = '<p class="sx-empty">Carregando legendas…</p>';
      else h = `${sw('capOn', 'Legendas no vídeo', track.enabled)}
        <h4>Estilo</h4><div class="sx-styles">${STYLES.map(([v, l]) => `<button class="${track.style === v ? 'on' : ''}" data-set="capStyle" data-v="${v}"><span class="sample st-${v}">Aa</span>${l}</button>`).join('')}</div>
        ${c.animatedFx && (c.outputPreset || 'vertical') === 'vertical' ? '<div class="sx-note">A <b>Animação FX</b> está ligada: a legenda final tem palavras que pulam. Para usar o estilo clássico escolhido abaixo, <button data-act="fxoff">desligue a animação</button>.</div>' : ''}
        <h4>Posição · arraste a legenda na tela</h4>${chips('capPos', [[20, 'Topo'], [50, 'Meio'], [80, 'Baixo']], Math.abs(track.positionY - 20) < 15 ? 20 : Math.abs(track.positionY - 50) < 15 ? 50 : 80)}
        <h4>Texto (${(track.blocks || []).length})</h4>
        <div class="sx-blocks">${(track.blocks || []).map((b, i) => `<div class="sx-block"><small>${fmt(b.start)}</small><textarea rows="2" data-blk="${i}">${esc(b.text)}</textarea></div>`).join('') || '<p class="sx-empty">Sem blocos de legenda.</p>'}</div>`;
    } else if (tool === 'texto') {
      h = `<label class="sx-field"><span>Título do corte</span><input data-txt="title" value="${esc(c.title)}" maxlength="120"></label>
        <label class="sx-field"><span>Texto da capa</span><input data-txt="coverText" value="${esc(c.coverText)}" maxlength="90"></label>
        <label class="sx-field"><span>Legenda do post</span><textarea rows="5" data-txt="caption" maxlength="2200">${esc(c.caption)}</textarea></label>`;
    } else if (tool === 'efeitos') {
      h = `<h4>Filtros de cor</h4><div class="sx-filters">${FILTERS.map(([v, l, css]) => `<button class="${(c.colorFilter || 'none') === v ? 'on' : ''}" data-set="filter" data-v="${v}"><span style="${c.coverPath ? `background-image:url('${asset(c.coverPath)}');` : ''}filter:${css || 'none'}"></span>${l}</button>`).join('')}</div>
        <h4>Animação</h4>${sw('fx', 'Animação Cortafé FX', c.animatedFx, 'Zoom, palavras que pulam, luz e som nos momentos fortes. Só em 9:16.')}
        ${c.animatedFx ? sw('fxHook', 'Texto de gancho no início', c.fxHook !== false) + sw('fxSfx', 'Sons e efeitos sonoros', c.fxSfx !== false) + sw('fxThemes', 'Luz, cruz, fogo e tempestade', c.fxThemes !== false, 'Efeitos visuais conforme o assunto') : ''}
        <h4>Enquadramento</h4>${chips('layout', LAYOUTS, c.layoutMode || 'fill')}
        ${(c.layoutMode || 'fill') === 'fill' ? `<label class="sx-field"><span>Posição horizontal</span><input type="range" min="0" max="100" value="${Math.round((c.cropX ?? .5) * 100)}" data-range="cropX"></label>` : ''}
        <h4>Formato</h4>${chips('format', FORMATS.map(f => [f[0], f[1]]), c.outputPreset || 'vertical')}
        <h4>Transição</h4>${chips('transition', TRANSITIONS, c.transitionStyle || 'smooth')}`;
    } else if (tool === 'marca') {
      h = `${sw('frame', 'Moldura da marca', c.brandFrameEnabled)}
        <h4>Tema</h4>${chips('theme', THEMES, c.brandTheme || 'amado-jesus')}
        ${sw('wm', 'Marca d’água', c.watermarkEnabled)}
        <label class="sx-field"><span>Texto da marca d’água</span><input data-txt="watermarkText" value="${esc(c.watermarkText)}" maxlength="40"></label>
        <label class="sx-field"><span>Transparência</span><input type="range" min="10" max="100" value="${Math.round((c.watermarkOpacity ?? .6) * 100)}" data-range="wmOpacity"></label>`;
    }
    el.innerHTML = h; bindPanel();
  }

  function bindPanel() {
    const c = clip(), track = tracks[c.id], el = $('#sxPanel');
    $$('[data-nudge]', el).forEach(b => b.onclick = () => {
      const [k, d] = b.dataset.nudge.split(':'), v = parseFloat(d), total = dur || P.duration || c.end + 60;
      if (k === 'start') c.start = clamp(Math.round((c.start + v) * 10) / 10, 0, c.end - MIN_LEN); else c.end = clamp(Math.round((c.end + v) * 10) / 10, c.start + MIN_LEN, total);
      save({ start: c.start, end: c.end }); paintTrim(); if (video) video.currentTime = k === 'start' ? c.start : Math.max(c.start, c.end - 1.5);
    });
    $$('[data-set]', el).forEach(b => b.onclick = () => {
      const k = b.dataset.set, v = b.dataset.v;
      if (k === 'speed') { save({ playbackSpeed: parseFloat(v) }); if (video) video.playbackRate = parseFloat(v); paintTrim(); }
      else if (k === 'layout') save({ layoutMode: v });
      else if (k === 'filter') save({ colorFilter: v });
      else if (k === 'format') save({ outputPreset: v });
      else if (k === 'transition') save({ transitionStyle: v });
      else if (k === 'theme') save({ brandTheme: v });
      else if (k === 'capStyle' && track) { track.style = v; saveTrack(c.id); }
      else if (k === 'capPos' && track) { track.positionY = parseFloat(v); saveTrack(c.id); }
      applyLook(); if (k === 'format') paintStage(); paintPanel();
    });
    $$('[data-sw]', el).forEach(i => i.onchange = () => {
      const k = i.dataset.sw, on = i.checked;
      if (k === 'silence') save({ silenceTrimmingEnabled: on });
      else if (k === 'fx') { save({ animatedFx: on }); applyLook(); paintPanel(); return; }
      else if (k === 'fxHook') save({ fxHook: on });
      else if (k === 'fxSfx') save({ fxSfx: on });
      else if (k === 'fxThemes') save({ fxThemes: on });
      else if (k === 'frame') save({ brandFrameEnabled: on });
      else if (k === 'wm') save({ watermarkEnabled: on });
      else if (k === 'capOn' && track) { track.enabled = on; saveTrack(c.id); }
      applyLook();
    });
    $$('[data-txt]', el).forEach(i => i.oninput = () => { save({ [i.dataset.txt]: i.value }); applyLook(); });
    $$('[data-range]', el).forEach(i => i.oninput = () => {
      const v = parseFloat(i.value) / 100;
      if (i.dataset.range === 'cropX') save({ cropX: v }); else save({ watermarkOpacity: v });
      applyLook();
    });
    $$('[data-blk]', el).forEach(t => t.oninput = () => { if (track) { track.blocks[+t.dataset.blk].text = t.value; saveTrack(c.id); paintCaption(video ? video.currentTime : c.start); } });
    $$('[data-blk]', el).forEach(t => t.onfocus = () => { const b = track && track.blocks[+t.dataset.blk]; if (b && video && !video.error) { video.pause(); video.currentTime = c.start + b.start + .05; paintCaption(video.currentTime); } });
  }

  // ---------- Actions ----------
  function paintActions() {
    const c = clip(), el = $('#sxActions'); if (!c || !el) return;
    const busy = rendering.has(c.id), has = !!c.videoPath, stale = has && c.renderOutdated;
    el.innerHTML = `<button class="sx-btn ${c.approved ? 'good' : 'outline'}" data-act="approve">${c.approved ? '✓ Aprovado' : 'Aprovar'}</button>
      ${has ? `<button class="sx-btn outline" data-act="toggleView">${showRendered ? 'Ver original' : 'Ver final'}</button>` : ''}
      ${has && !stale ? `<a class="sx-btn outline" href="${asset(c.videoPath)}" download>⬇ Baixar</a>` : ''}
      <button class="sx-btn primary" data-act="render" ${busy ? 'disabled' : ''}>${busy ? '<span class="spin"></span> Gerando…' : stale ? '↻ Gerar de novo' : has ? '↻ Gerar novamente' : '▶ Gerar vídeo'}</button>`;
    if (stale) el.insertAdjacentHTML('afterbegin', '<p class="sx-warn">Você mudou algo. Gere o vídeo de novo para atualizar.</p>');
  }

  async function renderOne(id) {
    const c = P.clips.find(x => x.id === id); if (!c || rendering.has(id)) return;
    await queue;
    rendering.add(id); paintClips(); paintActions();
    say('Gerando o vídeo… pode levar alguns minutos. Você pode continuar editando outros cortes.');
    try {
      const r = await fetch(`/api/projects/${P.id}/clips/${id}/render`, { method: 'POST' });
      if (!r.ok) { const d = await r.json().catch(() => ({})); throw new Error(d.detail || d.error || 'A geração falhou.'); }
      const up = await r.json();
      Object.assign(c, { videoPath: up.videoPath, renderOutdated: false, coverPath: up.coverPath || c.coverPath, previewPath: up.previewPath });
      say('Vídeo pronto!');
      if (clipId === id) { showRendered = true; paintStage(); }
    } catch (e) { say(e.message); }
    finally { rendering.delete(id); paintClips(); paintActions(); }
  }

  async function renderAll() {
    if (!confirm(`Gerar o vídeo dos ${P.clips.length} cortes agora? Pode demorar bastante.`)) return;
    for (const c of P.clips) await renderOne(c.id);
  }

  // ---------- Wiring ----------
  document.addEventListener('keydown', e => {
    if (!root || e.target.closest('input,textarea')) return;
    if (e.code === 'Space') { e.preventDefault(); togglePlay(); }
    else if (e.key === 'ArrowDown' || e.key === 'ArrowRight') { const i = P.clips.findIndex(c => c.id === clipId); if (P.clips[i + 1]) select(P.clips[i + 1].id); }
    else if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') { const i = P.clips.findIndex(c => c.id === clipId); if (P.clips[i - 1]) select(P.clips[i - 1].id); }
  });

  setInterval(() => {
    if (root && !document.querySelector('#projectView')) close();
    if (!root && document.getElementById('sxSwitch') && !document.querySelector('#projectView')) document.getElementById('sxSwitch').remove();
  }, 600);

  const previous = renderProject;
  renderProject = function (p) {
    previous(p);
    if (p && p.status === 'ready' && p.clips && p.clips.length) {
      if (isMode() === 'simple') { if (!root || P.id !== p.id) { if (P && P.id !== p.id) clipId = null; open(p); } }
      else showSwitch();
    }
  };
  window.CortafeSimpleEditor = { open: () => { sessionStorage.setItem(MODE_KEY, 'simple'); if (current) open(current); } };
})();
