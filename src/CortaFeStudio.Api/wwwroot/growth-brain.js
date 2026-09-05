// Growth Brain V1 · aprende com o histórico real registrado no Studio.
(function(){
  let insightsCache=new Map();
  const renderBase=renderProject;
  renderProject=function(project){renderBase(project);if(project.status==='ready')setTimeout(()=>install(project,0),290);};
  const selectBase=selectClip;
  selectClip=function(project,id){selectBase(project,id);setTimeout(()=>renderClip(project,project.clips.find(c=>c.id===id),true),180);};

  async function insights(profile){
    const key=profile||'';if(insightsCache.has(key))return insightsCache.get(key);
    const data=await api(`/api/performance/insights${profile?`?profile=${encodeURIComponent(profile)}`:''}`);insightsCache.set(key,data);return data;
  }

  function clamp(value){return Math.max(0,Math.min(100,Math.round(Number(value)||0)));}
  function channelFit(clip,data){
    if(!data||!data.samples)return {score:50,learning:false,reasons:['Ainda não há métricas suficientes deste perfil.']};
    let score=55;const reasons=[];const duration=Math.max(1,Number(clip.end)-Number(clip.start));
    if(Number(data.preferredDuration)>0){const delta=Math.abs(duration-Number(data.preferredDuration)),durationFit=Math.max(0,1-delta/Math.max(12,Number(data.preferredDuration)));score+=durationFit*20;reasons.push(`Duração: ${duration.toFixed(0)}s · histórico prefere ~${Number(data.preferredDuration).toFixed(0)}s`);}
    if(data.bestSubtitleStyle){const match=String(clip.subtitleStyle||'').toLowerCase()===String(data.bestSubtitleStyle).toLowerCase();score+=match?12:-4;reasons.push(match?`Legenda ${clip.subtitleStyle} coincide com o melhor histórico`:`Histórico favorece legenda ${data.bestSubtitleStyle}`);}
    if(Number(data.recommendedHookScore)>0){const hook=Number(clip.socialScore?.hook)||0,threshold=Number(data.recommendedHookScore);score+=hook>=threshold?13:Math.max(-8,(hook-threshold)*.45);reasons.push(`Hook ${Math.round(hook)} · referência histórica ${Math.round(threshold)}`);}
    return {score:clamp(score),learning:true,reasons};
  }

  async function analyze(project,clip){
    const data=await insights(clip.editorialProfile||project.options?.contentType||null),fit=channelFit(clip,data);
    const attention=window.AmadoJesusAttentionAi?.metrics?.(clip)?.total??Number(clip.score)||50;
    const faithful=clip._faithful?.faithful??window.AmadoJesusFaithfulAi?.analyze?.(project,clip)?.faithful??75;
    const predicted=clamp(attention*.38+faithful*.32+fit.score*.30);
    return {fit:fit.score,predicted,attention:clamp(attention),faithful:clamp(faithful),learning:fit.learning,reasons:fit.reasons,insights:data};
  }

  async function install(project,attempt=0){
    const view=document.querySelector('#projectView');if(!view)return;const drawer=view.querySelector('.cc-editor-insights-body');if(!drawer){if(attempt<8)setTimeout(()=>install(project,attempt+1),90);return;}
    const profile=project.options?.contentType||null,data=await insights(profile);
    let panel=drawer.querySelector('.growth-brain-summary');if(!panel){panel=document.createElement('section');panel.className='growth-brain-summary';const faithful=drawer.querySelector('.faithful-ai-summary');faithful?.after(panel)||drawer.append(panel);}
    panel.innerHTML=`<header><div><span class="eyebrow">GROWTH BRAIN · APRENDIZADO LOCAL</span><h3>O que funciona neste canal</h3><small>Usa somente métricas registradas por você. Sem dados, o Studio não finge que aprendeu.</small></div><div class="growth-samples"><b>${data.samples||0}</b><span>amostras</span></div></header>${data.samples?`<div class="growth-insights"><span>Duração <b>~${Number(data.preferredDuration||0).toFixed(0)}s</b></span><span>Legenda <b>${escapeHtml(data.bestSubtitleStyle||'—')}</b></span><span>Horário <b>${data.bestPublishingHour==null?'—':String(data.bestPublishingHour).padStart(2,'0')+':00'}</b></span><span>Hook alvo <b>${Math.round(Number(data.recommendedHookScore)||0)}</b></span></div>`:'<p class="growth-empty">Registre métricas de conteúdos publicados para o Channel Fit deixar de usar o valor neutro.</p>'}`;
    for(const clip of project.clips)await renderClip(project,clip,false);
  }

  async function renderClip(project,clip,force=false){
    if(!clip)return;const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(!card)return;let panel=card.querySelector('.growth-brain-card');if(panel&&!force)return;if(!panel){panel=document.createElement('section');panel.className='growth-brain-card';const target=card.querySelector('.cc-mode-details')||card;target.prepend(panel);}
    try{
      const result=await analyze(project,clip);clip._growth=result;
      panel.innerHTML=`<header><div><strong>GROWTH BRAIN</strong><small>${result.learning?'personalizado com seu histórico':'aguardando histórico'}</small></div><b>${result.predicted}/100</b></header><div class="growth-metrics"><span>Channel Fit <b>${result.fit}</b></span><span>Atenção <b>${result.attention}</b></span><span>Fidelidade <b>${result.faithful}</b></span></div><details><summary>Por que esta nota?</summary>${result.reasons.map(reason=>`<p>→ ${escapeHtml(reason)}</p>`).join('')}<button type="button" data-growth-record>Registrar resultado deste corte</button></details>`;
      panel.querySelector('[data-growth-record]').onclick=()=>typeof recordPerformance==='function'?recordPerformance(project.id,clip.id):toast('Central de desempenho indisponível');
    }catch(error){panel.innerHTML=`<p class="growth-empty">Não foi possível calcular o aprendizado: ${escapeHtml(error.message)}</p>`;}
  }

  function injectStyles(){
    if(document.querySelector('#growth-brain-styles'))return;const style=document.createElement('style');style.id='growth-brain-styles';style.textContent=`.growth-brain-summary{padding:15px;border:1px solid rgba(88,171,231,.18);border-radius:16px;background:rgba(7,18,29,.68);margin-bottom:12px}.growth-brain-summary>header{display:flex;justify-content:space-between;gap:12px}.growth-brain-summary h3{font-size:1rem;margin:.2rem 0}.growth-brain-summary header small{font-size:.65rem;color:#7f93a4}.growth-samples{display:grid;text-align:right}.growth-samples b{font-size:1.35rem;color:#8ec8ef}.growth-samples span{font-size:.57rem;color:#718596}.growth-insights{display:grid;grid-template-columns:repeat(4,1fr);gap:5px;margin-top:9px}.growth-insights span,.growth-metrics span{display:grid;padding:6px;border:1px solid rgba(255,255,255,.05);border-radius:8px;font-size:.55rem;color:#718493}.growth-insights b,.growth-metrics b{font-size:.68rem;color:#c3d5e2}.growth-empty{font-size:.62rem;color:#7c8994;margin:9px 0 0}.growth-brain-card{padding:10px;border:1px solid rgba(88,171,231,.14);border-radius:11px;background:rgba(7,18,29,.45);margin-bottom:10px}.growth-brain-card>header{display:flex;justify-content:space-between}.growth-brain-card>header>div{display:grid}.growth-brain-card header strong{font-size:.67rem}.growth-brain-card header small{font-size:.55rem;color:#71899b}.growth-brain-card header>b{color:#94c9eb}.growth-metrics{display:grid;grid-template-columns:repeat(3,1fr);gap:5px;margin-top:7px}.growth-brain-card details{margin-top:7px;border-top:1px solid rgba(255,255,255,.05);padding-top:6px}.growth-brain-card summary{font-size:.6rem;color:#92a8b8;cursor:pointer}.growth-brain-card details p{font-size:.57rem;color:#7e8f9b;margin:5px 0}.growth-brain-card details button{border:1px solid rgba(255,255,255,.08);background:transparent;color:#a7b9c6;border-radius:7px;padding:5px 7px;font-size:.57rem}@media(max-width:760px){.growth-insights{grid-template-columns:repeat(2,1fr)}}`;document.head.append(style);
  }
  injectStyles();
  window.AmadoJesusGrowthBrain={analyze,refresh:()=>{insightsCache=new Map();if(current)install(current)}};
})();