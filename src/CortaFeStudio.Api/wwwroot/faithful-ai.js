// Faithful AI 2.0 · potencial + fidelidade + evidência rastreável.
(function(){
  const dependent=['e ai','e então','e entao','por isso','porque','mas ai','como eu disse','isso aqui','aquilo','continuando','voltando'];
  const phaseSignals={
    gancho:['presta atenção','presta atencao','você sabe','voce sabe','sabe por que','deixa eu te','o problema é','o problema e','a verdade é','a verdade e'],
    contexto:['porque','quando','por exemplo','naquele tempo','isso aconteceu','o contexto'],
    explicacao:['isso significa','quer dizer','ou seja','o texto mostra','a palavra mostra','entenda','perceba'],
    aplicacao:['na sua vida','na nossa vida','você precisa','voce precisa','nós precisamos','nos precisamos','confie','creia','decida','pratique'],
    conclusao:['portanto','por isso','então','entao','no fim','concluindo','é por isso','e por isso']
  };

  const renderBase=renderProject;
  renderProject=function(project){renderBase(project);if(project.status==='ready')setTimeout(()=>install(project,0),240);};
  const selectBase=selectClip;
  selectClip=function(project,id){selectBase(project,id);setTimeout(()=>renderClip(project,project.clips.find(c=>c.id===id),true),150);};

  function norm(value){return String(value||'').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/\s+/g,' ').trim();}
  function clamp(value){return Math.max(0,Math.min(100,Math.round(Number(value)||0)));}
  function hasAny(text,list){return list.some(term=>text.includes(norm(term)));}

  function analyze(project,clip){
    const text=norm(clip.editedTranscript||clip.transcript||'');
    const first=text.split(/[.!?]/).find(Boolean)?.trim()||text.slice(0,120);
    const attention=window.AmadoJesusAttentionAi?.metrics?.(clip)?.total??Number(clip.score)||50;
    const sermon=window.AmadoJesusSermonIntelligence?.analyze?.(clip)||null;
    const bible=window.AmadoJesusBibleIntelligence?.clipReferences?.(clip)||[];
    const service=clip._serviceCategory||window.AmadoJesusServiceMap?.categoryForClip?.(clip)||'pregacao';
    const warnings=[...(sermon?.warnings||[])];
    const startsDependent=dependent.some(term=>first.startsWith(norm(term)));
    if(startsDependent&&!warnings.some(w=>w.includes('contexto anterior')))warnings.push('começa dependente do contexto anterior');
    const invalidRefs=bible.filter(ref=>!ref.valid);
    if(invalidRefs.length)warnings.push(`${invalidRefs.length} referência(s) bíblica(s) precisam de revisão`);
    if(!['pregacao','testemunho'].includes(service))warnings.push(`o trecho foi classificado como ${service}, não como pregação/testemunho`);
    const context=sermon?.contextIntegrity??clamp(72+(text.length>250?8:0)-(startsDependent?18:0));
    const standalone=sermon?.standalone??clamp(74+(text.length>220?6:0)-(startsDependent?20:0));
    const citation=bible.length===0?100:clamp(bible.reduce((sum,ref)=>sum+(ref.valid?ref.confidence:25),0)/bible.length);
    const completeness=clamp((context*.46)+(standalone*.44)+(citation*.10));
    let faithful=clamp(completeness-warnings.length*4+(service==='pregacao'?4:0));
    if(startsDependent)faithful=clamp(faithful-8);
    const risk=clamp(100-faithful+Math.max(0,warnings.length-1)*3);
    const status=faithful>=86&&risk<=18?'strong':faithful>=70&&risk<=34?'review':'hold';
    const evidence=evidenceFor(project,clip,bible);
    return {attention:clamp(attention),context,standalone,citation,faithful,risk,status,warnings:[...new Set(warnings)],evidence,service,bible};
  }

  function evidenceFor(project,clip,bible){
    const segments=(project.transcript||[]).filter(s=>Number(s.end)>Number(clip.start)&&Number(s.start)<Number(clip.end));
    const found=[];
    for(const segment of segments){
      const text=norm(segment.text),relative=Math.max(0,Number(segment.start)-Number(clip.start));
      let phase=null;
      if(window.AmadoJesusBibleIntelligence?.detect?.(segment.text)?.length)phase='texto bíblico';
      else for(const [name,terms] of Object.entries(phaseSignals))if(hasAny(text,terms)){phase=name;break;}
      if(!phase&&found.length===0)phase='abertura';
      if(phase&&!found.some(item=>item.phase===phase))found.push({phase,time:relative,absolute:Number(segment.start)||0,text:String(segment.text||'').trim().slice(0,170)});
      if(found.length>=6)break;
    }
    if(!found.some(item=>item.phase==='texto bíblico')&&bible.length){const ref=bible[0];found.push({phase:'texto bíblico',time:0,absolute:Number(clip.start)||0,text:`Referência detectada: ${ref.reference}`});}
    return found.sort((a,b)=>a.time-b.time);
  }

  function install(project,attempt=0){
    const view=document.querySelector('#projectView');if(!view)return;const insights=view.querySelector('.cc-editor-insights-body');if(!insights){if(attempt<8)setTimeout(()=>install(project,attempt+1),90);return;}
    const analyses=project.clips.map(clip=>({clip,data:analyze(project,clip)}));project._faithfulAnalyses=analyses;
    let panel=insights.querySelector('.faithful-ai-summary');if(!panel){panel=document.createElement('section');panel.className='faithful-ai-summary';const bible=insights.querySelector('.bible-intelligence-panel');bible?.after(panel)||insights.prepend(panel);}
    const strong=analyses.filter(item=>item.data.status==='strong').length,review=analyses.filter(item=>item.data.status==='review').length;
    panel.innerHTML=`<header><div><span class="eyebrow">FAITHFUL AI 2.0 · EVIDENCE MODE</span><h3>Potencial sem perder o contexto</h3><small>Não altera a fala para criar gancho. Avalia se o trecho funciona sozinho e mostra a evidência.</small></div><div class="faithful-summary-score"><b>${strong}</b><span>fortes para publicar</span></div></header><div class="faithful-summary-grid"><span><b>${strong}</b> fortes</span><span><b>${review}</b> revisar</span><span><b>${analyses.length-strong-review}</b> segurar</span></div>`;
    analyses.forEach(({clip,data})=>renderClip(project,clip,false,data));
  }

  function renderClip(project,clip,force=false,data=null){
    if(!clip)return;const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(!card)return;let panel=card.querySelector('.faithful-ai-card');if(panel&&!force)return;if(!panel){panel=document.createElement('section');panel.className='faithful-ai-card';const target=card.querySelector('.cc-mode-details')||card;target.prepend(panel);}data=data||analyze(project,clip);clip._faithful=data;
    const label={strong:'✓ Forte para publicar',review:'⚠ Revisar contexto',hold:'⛔ Não recomendado ainda'}[data.status];
    panel.dataset.status=data.status;
    panel.innerHTML=`<header><div><strong>FAITHFUL AI</strong><small>${label}</small></div><b>${data.faithful}/100</b></header><div class="faithful-metrics"><span>Fidelidade <b>${data.faithful}</b></span><span>Contexto <b>${data.context}</b></span><span>Autônomo <b>${data.standalone}</b></span><span>Atenção <b>${data.attention}</b></span><span>Risco <b>${data.risk}</b></span></div>${data.warnings.length?`<ul>${data.warnings.map(w=>`<li>${escapeHtml(w)}</li>`).join('')}</ul>`:'<p class="faithful-ok">✓ Não encontrei alerta contextual relevante.</p>'}<details class="faithful-evidence"><summary>Ver evidências do corte</summary>${data.evidence.map(item=>`<button type="button" data-faithful-seek="${item.absolute}"><span>${time(item.time)}</span><b>${escapeHtml(item.phase)}</b><p>${escapeHtml(item.text)}</p></button>`).join('')||'<p>Nenhum marcador estrutural explícito encontrado.</p>'}<button type="button" class="faithful-context-button" data-faithful-context>Ver 20s antes do corte</button></details>`;
    panel.querySelectorAll('[data-faithful-seek]').forEach(button=>button.onclick=()=>seek(+button.dataset.faithfulSeek));
    panel.querySelector('[data-faithful-context]').onclick=()=>seek(Math.max(0,Number(clip.start)-20));
  }

  function seek(seconds){if(typeof switchEditorTab==='function')switchEditorTab('source');setTimeout(()=>{const video=document.querySelector('#preview video');if(video){video.currentTime=seconds;video.play().catch(()=>{});}},120);}

  function injectStyles(){
    if(document.querySelector('#faithful-ai-styles'))return;const style=document.createElement('style');style.id='faithful-ai-styles';style.textContent=`.faithful-ai-summary{padding:15px;border:1px solid rgba(100,210,168,.18);border-radius:16px;background:rgba(7,25,20,.68);margin-bottom:12px}.faithful-ai-summary>header{display:flex;justify-content:space-between;gap:12px}.faithful-ai-summary h3{font-size:1rem;margin:.2rem 0}.faithful-ai-summary header small{font-size:.66rem;color:#7e9c91}.faithful-summary-score{display:grid;text-align:right}.faithful-summary-score b{font-size:1.4rem;color:#8fddbd}.faithful-summary-score span{font-size:.57rem;color:#719286}.faithful-summary-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:6px;margin-top:9px}.faithful-summary-grid span{padding:6px;border:1px solid rgba(255,255,255,.05);border-radius:8px;font-size:.61rem;color:#87988f}.faithful-ai-card{padding:11px;border:1px solid rgba(100,210,168,.15);border-radius:12px;background:rgba(8,25,20,.48);margin-bottom:10px}.faithful-ai-card[data-status="review"]{border-color:rgba(220,169,90,.22);background:rgba(35,26,12,.38)}.faithful-ai-card[data-status="hold"]{border-color:rgba(209,95,95,.22);background:rgba(35,13,13,.36)}.faithful-ai-card>header{display:flex;justify-content:space-between;align-items:center}.faithful-ai-card>header>div{display:grid}.faithful-ai-card>header strong{font-size:.68rem}.faithful-ai-card>header small{font-size:.58rem;color:#91a99e}.faithful-ai-card>header>b{font-size:1.1rem;color:#9dd9c1}.faithful-metrics{display:grid;grid-template-columns:repeat(5,1fr);gap:4px;margin-top:8px}.faithful-metrics span{display:grid;font-size:.51rem;color:#73857c;padding:4px;border-radius:6px;background:rgba(255,255,255,.025)}.faithful-metrics b{font-size:.68rem;color:#c9d7d1}.faithful-ai-card ul{padding-left:17px;margin:8px 0 0;font-size:.61rem;color:#c9a7a0}.faithful-ok{font-size:.62rem;color:#91c9b2;margin:8px 0 0}.faithful-evidence{margin-top:8px;border-top:1px solid rgba(255,255,255,.06);padding-top:7px}.faithful-evidence summary{font-size:.63rem;color:#9fb9ae;cursor:pointer}.faithful-evidence>button:not(.faithful-context-button){width:100%;display:grid;grid-template-columns:42px 75px 1fr;gap:6px;text-align:left;border:0;border-bottom:1px solid rgba(255,255,255,.04);background:transparent;color:#bbc5c0;padding:6px 0;font-size:.58rem}.faithful-evidence button span{color:#7c9188}.faithful-evidence button b{color:#a8cabb}.faithful-evidence button p{margin:0;color:#8e9994}.faithful-context-button{margin-top:7px;border:1px solid rgba(255,255,255,.08);background:transparent;color:#9eb0a8;border-radius:7px;padding:5px 8px;font-size:.59rem}@media(max-width:760px){.faithful-metrics{grid-template-columns:repeat(3,1fr)}.faithful-evidence>button:not(.faithful-context-button){grid-template-columns:38px 1fr}.faithful-evidence button p{grid-column:2}}`;document.head.append(style);
  }
  injectStyles();
  window.AmadoJesusFaithfulAi={analyze};
})();