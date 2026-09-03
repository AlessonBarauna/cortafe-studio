// Sermon Intelligence V6 · análise contextual local para pregações
(function(){
  const previousRenderProject = renderProject;
  renderProject = function(project){
    previousRenderProject(project);
    if(project.status !== 'ready' || project.options?.contentType !== 'pregacao') return;
    installSermonIntelligence(project);
  };

  const books = ['gênesis','genesis','êxodo','exodo','levítico','levitico','números','numeros','deuteronômio','deuteronomio','josué','josue','juízes','juizes','rute','samuel','reis','crônicas','cronicas','esdras','neemias','ester','jó','jo','salmos','salmo','provérbios','proverbios','eclesiastes','cantares','isaías','isaias','jeremias','lamentações','lamentacoes','ezequiel','daniel','oséias','oseias','joel','amós','amos','obadias','jonas','miquéias','miqueias','naum','habacuque','sofonias','ageu','zacarias','malaquias','mateus','marcos','lucas','joão','joao','atos','romanos','coríntios','corintios','gálatas','galatas','efésios','efesios','filipenses','colossenses','tessalonicenses','timóteo','timoteo','tito','filemom','hebreus','tiago','pedro','judas','apocalipse'];
  const dependentOpenings = ['e aí','e então','porque','por isso','mas aí','como eu disse','isso aqui','aquilo','esse ponto','continuando','voltando','ele também','ela também','isso também'];
  const explanationSignals = ['isso significa','quer dizer','o texto mostra','o texto diz','a palavra mostra','em outras palavras','ou seja','porque','entenda','perceba'];
  const applicationSignals = ['na sua vida','na nossa vida','você precisa','nós precisamos','por isso faça','então faça','aplique','pratique','decida','confie','creia','lembre-se'];
  const conclusionSignals = ['portanto','por isso','então','no fim','é por isso','isso significa','a verdade é','concluindo'];
  const hookSignals = ['presta atenção','deixa eu te','posso te falar','você tem noção','imagina isso','sabe por quê','o problema é','a verdade é','quando você'];

  function norm(value){ return String(value||'').toLowerCase().replace(/\s+/g,' ').trim(); }
  function clamp(value){ return Math.max(0,Math.min(100,Math.round(value))); }
  function hasAny(text,list){ return list.some(item=>text.includes(item)); }
  function hasBiblicalReference(text){
    const book = books.some(item=>text.includes(item));
    const chapterVerse = /\b\d{1,3}\s*[:.]\s*\d{1,3}\b/.test(text);
    const spoken = /\b(capítulo|capitulo)\s+\d{1,3}\b/.test(text) && /\b(versículo|versiculo)\s+\d{1,3}\b/.test(text);
    return book && (chapterVerse || spoken || text.includes('versículo') || text.includes('versiculo'));
  }

  function sermonMetrics(clip){
    const text = norm(clip.editedTranscript || clip.transcript);
    const first = text.split(/[.!?]/).find(Boolean)?.trim() || text.slice(0,120);
    const last = text.split(/[.!?]/).filter(Boolean).at(-1)?.trim() || text.slice(-120);
    const b = clip.scoreBreakdown || {};
    const reference = hasBiblicalReference(text);
    const explanation = hasAny(text, explanationSignals);
    const application = hasAny(text, applicationSignals);
    const conclusion = hasAny(text, conclusionSignals) || Number(b.completion||0) > 0;
    const hook = hasAny(first, hookSignals) || first.includes('?') || Number(b.hook||0) > 0;
    const context = text.length > 180 && (text.includes('porque') || text.includes('quando') || text.includes('por exemplo') || Number(b.structure||0) > 0);
    const dependent = dependentOpenings.some(item=>first.startsWith(item)) || Number(b.contextPenalty||0) < 0;
    const questionOpen = first.includes('?');
    const answerSignal = explanation || conclusion || text.includes('a resposta') || text.includes('a razão');
    const unfinished = !/[.!?]$/.test(String(clip.editedTranscript||clip.transcript||'').trim()) && !conclusion;
    const phases = {hook,context,reference,explanation,application,conclusion};
    const phaseCount = Object.values(phases).filter(Boolean).length;
    const contextIntegrity = clamp(58 + phaseCount*6 + (reference&&explanation?9:0) + Number(b.structure||0)*1.1 + Number(b.completion||0)*1.2 + Number(b.contextPenalty||0)*1.8 - (dependent?16:0) - (unfinished?10:0));
    const standalone = clamp(62 + Number(b.structure||0)*1.5 + Number(b.completion||0)*1.8 + Number(b.clarity||0)*1.2 + Number(b.contextPenalty||0)*2 - (dependent?15:0) - (questionOpen&&!answerSignal?12:0));
    const warnings=[];
    if(dependent) warnings.push('começa dependente do contexto anterior');
    if(questionOpen&&!answerSignal) warnings.push('abre uma pergunta sem resposta clara no próprio corte');
    if(reference&&!explanation) warnings.push('cita texto bíblico sem explicação clara');
    if(unfinished) warnings.push('final pode parecer interrompido');
    if(!application && reference) warnings.push('não detectei aplicação prática após o texto');
    return {contextIntegrity,standalone,phases,warnings,reference};
  }

  function installSermonIntelligence(project){
    injectStyles();
    const view=document.querySelector('#projectView');
    if(!view||view.querySelector('.sermon-intelligence-summary'))return;
    const camera=view.querySelector('.smart-camera-panel');
    const attention=view.querySelector('.attention-ai-panel');
    const analyses=project.clips.map(clip=>({clip,analysis:sermonMetrics(clip)}));
    const safe=analyses.filter(item=>item.analysis.contextIntegrity>=75&&item.analysis.standalone>=72&&item.analysis.warnings.length<=1).length;
    const host=document.createElement('section');
    host.className='sermon-intelligence-summary';
    host.innerHTML=`<div><span class="eyebrow">SERMON INTELLIGENCE · CONTEXTO</span><h3>Integridade da mensagem</h3><small class="text-secondary">Evita cortes fortes que perdem sentido ou distorcem a ideia quando saem da pregação completa.</small></div><div class="sermon-summary-score"><b>${safe}/${project.clips.length}</b><span>cortes contextualmente fortes</span></div>`;
    (camera||attention)?.after(host);
    analyses.forEach(({clip,analysis})=>installClipSermon(clip,analysis));
  }

  function installClipSermon(clip,analysis){
    const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);
    if(!card||card.querySelector('.sermon-intelligence-card'))return;
    const target=card.querySelector('.cc-mode-details')||card;
    const panel=document.createElement('section');
    panel.className='sermon-intelligence-card';
    const phases=Object.entries(analysis.phases).map(([key,active])=>`<span class="${active?'active':''}">${phaseLabel(key)}</span>`).join('');
    const warnings=analysis.warnings.length?`<ul>${analysis.warnings.map(item=>`<li>${escapeHtml(item)}</li>`).join('')}</ul>`:'<p class="sermon-ok">✓ mensagem autocontida e coerente</p>';
    panel.innerHTML=`<header><strong>SERMON INTELLIGENCE</strong><div><span>Contexto <b>${analysis.contextIntegrity}</b></span><span>Autônomo <b>${analysis.standalone}</b></span></div></header><div class="sermon-phases">${phases}</div>${warnings}`;
    target.prepend(panel);
  }

  function phaseLabel(key){ return ({hook:'Gancho',context:'Contexto',reference:'Texto bíblico',explanation:'Explicação',application:'Aplicação',conclusion:'Conclusão'})[key]||key; }

  function injectStyles(){
    if(document.querySelector('#sermon-intelligence-styles'))return;
    const style=document.createElement('style'); style.id='sermon-intelligence-styles';
    style.textContent=`.sermon-intelligence-summary{margin:0 0 20px;padding:16px 18px;border:1px solid rgba(185,140,255,.2);border-radius:18px;background:rgba(23,13,34,.78);display:flex;justify-content:space-between;gap:18px;align-items:center}.sermon-intelligence-summary h3{margin:.2rem 0 0;font-size:1.15rem}.sermon-summary-score{display:grid;text-align:right}.sermon-summary-score b{font-size:1.5rem;color:#cbb1ff}.sermon-summary-score span{font-size:.72rem;color:#ad9dbf}.sermon-intelligence-card{padding:12px;border:1px solid rgba(185,140,255,.15);border-radius:13px;background:rgba(19,10,29,.58);margin-bottom:12px}.sermon-intelligence-card header{display:flex;justify-content:space-between;gap:10px;align-items:center}.sermon-intelligence-card header strong{font-size:.78rem;letter-spacing:.06em}.sermon-intelligence-card header>div{display:flex;gap:8px}.sermon-intelligence-card header span{font-size:.68rem;color:#b6a8c6}.sermon-intelligence-card header b{color:#dacaff}.sermon-phases{display:flex;gap:5px;flex-wrap:wrap;margin:9px 0}.sermon-phases span{border:1px solid rgba(255,255,255,.09);border-radius:999px;padding:3px 7px;font-size:.62rem;color:#6f6878}.sermon-phases span.active{color:#dfd1ef;border-color:rgba(203,177,255,.35);background:rgba(203,177,255,.08)}.sermon-intelligence-card ul{margin:8px 0 0;padding-left:18px;color:#d5b6b6;font-size:.68rem}.sermon-ok{margin:8px 0 0;color:#a8d7c5;font-size:.7rem}@media(max-width:760px){.sermon-intelligence-summary{align-items:flex-start;flex-direction:column}.sermon-summary-score{text-align:left}.sermon-intelligence-card header{align-items:flex-start;flex-direction:column}}`;
    document.head.append(style);
  }

  window.AmadoJesusSermonIntelligence={analyze:sermonMetrics};
})();
