// Attention AI V4 + Smart Camera V5 · camada local multissinal
(function(){
  const previousRenderProject = renderProject;
  renderProject = function(project){
    previousRenderProject(project);
    if(project.status !== 'ready') return;
    installAttentionAi(project);
    installSmartCamera(project);
  };

  function clamp(value,min=0,max=100){ return Math.max(min,Math.min(max,Number.isFinite(+value)?+value:0)); }
  function n(value,fallback=0){ const parsed=+value; return Number.isFinite(parsed)?parsed:fallback; }

  function attentionMetrics(clip){
    const b=clip.scoreBreakdown||{}, s=clip.socialScore||{}, v=clip.visualDirection||{};
    const text=String(clip.editedTranscript||clip.transcript||'').toLowerCase();
    const impactTerms=['milagre','propósito','cura','transformação','dor','perdão','cruz','jesus','deus','verdade','decisão','recomeço','impossível','amor','paz'];
    const emotionalHits=impactTerms.filter(term=>text.includes(term)).length;
    const hook=clamp(n(s.hook,0)>0?s.hook:52+n(b.hook)*2.2+n(b.openingAdjustment)*1.25+n(b.impact)*.55);
    const retention=clamp(n(s.retention,0)>0?s.retention:58+n(b.structure)*1.6+n(b.clarity)*2.7+n(b.lengthAdjustment)*.8);
    const conclusion=clamp(n(s.conclusion,0)>0?s.conclusion:55+n(b.conclusion)*2.3+n(b.completion)*2.1+n(b.structure)*.8);
    const emotion=clamp(48+n(b.impact)*3.3+Math.min(7,emotionalHits)*4+n(b.contrast)*.9);
    const standalone=clamp(63+n(b.structure)*1.9+n(b.completion)*2.1+n(b.contextPenalty)*2.2+n(b.clarity));
    const visual=clamp(v.analyzed?n(v.score,50):50);
    const editorial=clamp(n(clip.score,50));
    const total=clamp(hook*.23+retention*.17+conclusion*.16+emotion*.15+standalone*.15+visual*.08+editorial*.06);
    return {hook,retention,conclusion,emotion,standalone,visual,total};
  }

  function tier(score){
    if(score>=86)return {label:'🔥 pico de atenção',className:'attention-tier-hot'};
    if(score>=74)return {label:'forte potencial',className:'attention-tier-strong'};
    return {label:'bom momento',className:'attention-tier-good'};
  }

  function installAttentionAi(project){
    const view=document.querySelector('#projectView');
    if(!view||view.querySelector('.attention-ai-panel'))return;
    const productivity=view.querySelector('.aj-productivity-suite');
    const sectionHead=view.querySelector('.section-head');
    const duration=Math.max(1,n(project.duration,Math.max(...project.clips.map(c=>n(c.end,0)),1)));
    const ranked=project.clips.map(clip=>({clip,metrics:attentionMetrics(clip)})).sort((a,b)=>b.metrics.total-a.metrics.total);
    const host=document.createElement('section');
    host.className='attention-ai-panel';
    host.innerHTML=`<div class="attention-ai-head"><div><span class="eyebrow">ATTENTION AI · MULTISSINAL</span><h3>Mapa de atenção do vídeo</h3><small class="text-secondary">Combina estrutura editorial, potencial social e análise visual já calculada pelo Studio.</small></div><div class="attention-ai-legend"><span>baixo</span><span>→</span><b>alto</b></div></div><div class="attention-ai-track" role="list" aria-label="Mapa de atenção">${ranked.map(({clip,metrics})=>attentionSegmentHtml(clip,metrics,duration)).join('')}</div><div class="attention-top">${ranked.slice(0,5).map(({clip,metrics},index)=>`<button type="button" data-attention-jump="${escapeHtml(clip.id)}"><b>#${index+1} · ${Math.round(metrics.total)}</b> ${escapeHtml((clip.title||'Momento').slice(0,48))}</button>`).join('')}</div>`;
    (productivity||sectionHead)?.after(host);
    host.querySelectorAll('[data-attention-jump]').forEach(button=>button.addEventListener('click',()=>jumpToClip(project,button.dataset.attentionJump)));
    host.querySelectorAll('.attention-segment').forEach(button=>button.addEventListener('click',()=>jumpToClip(project,button.dataset.clipId)));
    project.clips.forEach(clip=>installClipAttention(project,clip,attentionMetrics(clip)));
  }

  function attentionSegmentHtml(clip,metrics,duration){
    const left=clamp(n(clip.start)/duration*100,0,99.5);
    const width=Math.max(.8,Math.min(100-left,(n(clip.end)-n(clip.start))/duration*100));
    const heat=(.22+metrics.total/100*.78).toFixed(2);
    return `<button type="button" class="attention-segment" data-clip-id="${escapeHtml(clip.id)}" style="left:${left.toFixed(2)}%;width:${width.toFixed(2)}%;--heat:${heat}" title="${escapeHtml(clip.title||'Corte')} · ${Math.round(metrics.total)}/100"></button>`;
  }

  function installClipAttention(project,clip,metrics){
    const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);
    if(!card||card.querySelector('.attention-score-card'))return;
    const target=card.querySelector('.cc-mode-details')||card;
    const level=tier(metrics.total);
    const panel=document.createElement('section');
    panel.className='attention-score-card';
    panel.innerHTML=`<header><strong>ATTENTION AI</strong><span class="attention-score-total">${Math.round(metrics.total)}/100</span></header><div class="attention-metrics">${metric('Gancho',metrics.hook)}${metric('Retenção',metrics.retention)}${metric('Conclusão',metrics.conclusion)}${metric('Emoção',metrics.emotion)}${metric('Sem contexto',metrics.standalone)}${metric('Visual',metrics.visual)}</div><div class="attention-reason ${level.className}">${level.label} · ${attentionReason(metrics)}</div>`;
    target.prepend(panel);
  }

  function metric(label,value){return `<div class="attention-metric"><small>${label}</small><b>${Math.round(value)}</b></div>`;}
  function attentionReason(m){
    const entries=[['gancho',m.hook],['retenção',m.retention],['fechamento',m.conclusion],['emoção',m.emotion],['independência',m.standalone],['visual',m.visual]].sort((a,b)=>b[1]-a[1]);
    return `pontos fortes: ${entries[0][0]} e ${entries[1][0]}`;
  }

  function jumpToClip(project,id){
    const card=document.querySelector(`.clip-card[data-clip="${id}"]`);
    if(!card)return;
    selectClip(project,id);
    card.scrollIntoView({behavior:'smooth',block:'center'});
    card.animate([{boxShadow:'0 0 0 0 rgba(199,163,90,0)'},{boxShadow:'0 0 0 4px rgba(199,163,90,.45)'},{boxShadow:'0 0 0 0 rgba(199,163,90,0)'}],{duration:900});
  }

  function installSmartCamera(project){
    const view=document.querySelector('#projectView');
    if(!view||view.querySelector('.smart-camera-panel'))return;
    const attention=view.querySelector('.attention-ai-panel');
    const host=document.createElement('section');
    host.className='smart-camera-panel';
    host.innerHTML=`<div><span class="eyebrow">SMART CAMERA · IA LOCAL</span><h3>Câmera inteligente</h3><small class="text-secondary">Rosto, troca de locutor, cenas e trilha de movimento já são analisados pelo OpenCV local.</small></div><div class="smart-camera-actions"><button type="button" class="btn btn-gold" data-camera-batch>Analisar câmera dos selecionados</button><span data-camera-state>Selecione cortes na edição em massa acima.</span></div>`;
    attention?.after(host);
    host.querySelector('[data-camera-batch]').addEventListener('click',button=>analyzeSelectedCameras(project,button.currentTarget,host));
    project.clips.forEach(clip=>installClipCamera(project,clip));
  }

  function selectedIdsFromUi(){
    return [...document.querySelectorAll('.clip-card')].filter(card=>card.querySelector('[data-batch-select]')?.checked).map(card=>card.dataset.clip).filter(Boolean);
  }

  function cameraLabel(clip){
    if(clip.layoutMode==='split')return 'Split inteligente';
    if(clip.layoutMode==='blur')return 'Fundo seguro';
    if((clip.framingTrack||[]).length>1)return 'Tracking do locutor';
    return clip.faceTrackingAnalyzed?'Rosto principal':'Ainda não analisada';
  }

  function installClipCamera(project,clip){
    const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);
    if(!card||card.querySelector('.smart-camera-card'))return;
    const target=card.querySelector('.cc-mode-visual')||card.querySelector('.cc-mode-details')||card;
    const panel=document.createElement('section');
    panel.className='smart-camera-card';
    panel.innerHTML=`<header><div><strong>SMART CAMERA</strong><small data-camera-label>${escapeHtml(cameraLabel(clip))}</small></div><span>${clip.visualDirection?.analyzed?Math.round(n(clip.visualDirection.score,0))+'/100':'pendente'}</span></header><p>${escapeHtml(clip.visualDirection?.recommendation||'Analise o corte para escolher o melhor enquadramento.')}</p><div class="smart-camera-presets"><button type="button" data-camera-analyze>IA automática</button><button type="button" data-camera-mode="fill">Pessoa em foco</button><button type="button" data-camera-mode="split">Duas pessoas</button><button type="button" data-camera-mode="blur">Fundo seguro</button></div>`;
    target.prepend(panel);
    panel.querySelector('[data-camera-analyze]').addEventListener('click',event=>analyzeOneCamera(project,clip,event.currentTarget,panel));
    panel.querySelectorAll('[data-camera-mode]').forEach(button=>button.addEventListener('click',()=>setCameraMode(project,clip,button.dataset.cameraMode,panel)));
  }

  async function analyzeOneCamera(project,clip,button,panel){
    const original=button.textContent; button.disabled=true; button.textContent='Analisando…';
    try{
      const result=await api(`/api/projects/${project.id}/clips/${clip.id}/analyze-framing`,{method:'POST'});
      Object.assign(clip,result); updateCameraPanel(panel,clip); toast('✓ Câmera analisada com IA local');
    }catch(error){toast(error.message)}finally{button.disabled=false;button.textContent=original}
  }

  async function analyzeSelectedCameras(project,button,host){
    const ids=selectedIdsFromUi(); if(!ids.length)return toast('Selecione os cortes que deseja analisar');
    button.disabled=true; const state=host.querySelector('[data-camera-state]');
    try{
      let done=0;
      for(const id of ids){
        state.textContent=`Analisando ${done+1}/${ids.length}…`;
        const clip=project.clips.find(item=>item.id===id); if(!clip)continue;
        const result=await api(`/api/projects/${project.id}/clips/${id}/analyze-framing`,{method:'POST'}); Object.assign(clip,result); done++;
        const panel=document.querySelector(`.clip-card[data-clip="${id}"] .smart-camera-card`); if(panel)updateCameraPanel(panel,clip);
      }
      state.textContent=`✓ ${done} ${done===1?'corte analisado':'cortes analisados'}`; toast('Smart Camera concluída');
    }catch(error){state.textContent='Falha na análise';toast(error.message)}finally{button.disabled=false}
  }

  async function setCameraMode(project,clip,mode,panel){
    try{
      await api(`/api/projects/${project.id}/clips/${clip.id}`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({layoutMode:mode})});
      clip.layoutMode=mode; updateCameraPanel(panel,clip); toast(`Câmera: ${cameraLabel(clip)}`);
    }catch(error){toast(error.message)}
  }

  function updateCameraPanel(panel,clip){
    const label=panel.querySelector('[data-camera-label]'); if(label)label.textContent=cameraLabel(clip);
    const score=panel.querySelector('header>span'); if(score)score.textContent=clip.visualDirection?.analyzed?`${Math.round(n(clip.visualDirection.score,0))}/100`:'pendente';
    const description=panel.querySelector('p'); if(description)description.textContent=clip.visualDirection?.recommendation||'Enquadramento atualizado.';
  }

  window.AmadoJesusAttentionAi={metrics:attentionMetrics};
})();
