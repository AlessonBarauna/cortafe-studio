// AI Edit Command V1 · linguagem natural -> controles reais do editor.
(function(){
  const examples=[
    'deixe mais dinâmico, legenda clean, acompanhe o pregador e mantenha vertical',
    'preserve as pausas emocionais, câmera suave e legenda discreta',
    'podcast com duas pessoas, legenda podcast e transição editorial',
    'formato quadrado, sem marca d’água e velocidade normal'
  ];

  const renderBase=renderProject;
  renderProject=function(project){renderBase(project);if(project.status==='ready')setTimeout(()=>install(project),430);};
  const selectBase=selectClip;
  selectClip=function(project,id){selectBase(project,id);setTimeout(()=>renderPanel(project,project.clips.find(c=>c.id===id),true),270);};

  function norm(value){return String(value||'').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/[’']/g,"'").replace(/\s+/g,' ').trim();}
  function has(text,...terms){return terms.some(term=>text.includes(norm(term)));}

  function parse(command,clip,profile){
    const text=norm(command),changes={},actions={},reasons=[];
    if(has(text,'mais dinamico','mais dinâmica','dinamico','dinâmica','ritmo rapido','ritmo rápido')){changes.transitionStyle='dynamic';changes.playbackSpeed=profile==='louvor'?1:1.25;reasons.push('ritmo mais dinâmico');}
    if(has(text,'editorial','cinematografico','cinematográfico')){changes.transitionStyle='editorial';reasons.push('transição editorial');}
    if(has(text,'suave','mais calmo','mais calma')){changes.transitionStyle='smooth';changes.playbackSpeed=1;reasons.push('ritmo suave');}
    if(has(text,'velocidade normal','1x','sem acelerar')){changes.playbackSpeed=1;reasons.push('velocidade normal');}
    if(has(text,'1.25','1,25','acelere um pouco')){changes.playbackSpeed=profile==='louvor'?1:1.25;reasons.push(profile==='louvor'?'louvor preservado em 1x':'velocidade 1,25x');}
    if(has(text,'1.5','1,5','bem rapido','bem rápido')){changes.playbackSpeed=profile==='louvor'?1:1.5;reasons.push(profile==='louvor'?'louvor preservado em 1x':'velocidade 1,5x');}

    if(has(text,'preserve as pausas','preservar pausas','nao corte pausas','não corte pausas','sem cortar pausas','pausas emocionais')){changes.silenceTrimmingEnabled=false;reasons.push('pausas preservadas');}
    if(has(text,'remova pausas','remover pausas','corte silencios','corte silêncios','reduza pausas')){changes.silenceTrimmingEnabled=true;reasons.push('redução de pausas ativada');}

    if(has(text,'duas pessoas','split','lado a lado','podcast duas pessoas')){changes.layoutMode='split';reasons.push('layout para duas pessoas');}
    if(has(text,'fundo desfocado','blur','desfoque o fundo')){changes.layoutMode='blur';reasons.push('fundo desfocado');}
    if(has(text,'acompanhe o pregador','acompanhar pregador','pessoa em foco','locutor em foco','rosto em foco','centralize o pregador')){
      changes.layoutMode='fill';actions.analyzeFraming=true;reasons.push('análise visual e rastreamento real do locutor');
    }
    if(has(text,'centro','centralizado','centralizada')){changes.cropFocus='center';reasons.push('enquadramento central');}

    if(has(text,'vertical','9:16','reels','shorts','tiktok')){changes.outputPreset='vertical';reasons.push('formato vertical');}
    if(has(text,'quadrado','1:1')){changes.outputPreset='square';reasons.push('formato quadrado');}
    if(has(text,'horizontal','16:9','youtube horizontal')){changes.outputPreset='landscape';reasons.push('formato horizontal');}
    if(has(text,'4:5','retrato')){changes.outputPreset='portrait';reasons.push('formato 4:5');}

    if(has(text,'legenda clean','legenda limpa','legenda discreta','minimalista')){changes.subtitleStyle='clean';reasons.push('legenda clean');}
    if(has(text,'legenda impacto','legenda forte','impact')){changes.subtitleStyle='impact';reasons.push('legenda de impacto');}
    if(has(text,'legenda podcast')){changes.subtitleStyle='podcast';reasons.push('legenda podcast');}
    if(has(text,'legenda sermão','legenda sermao','pregacao impacto','pregação impacto')){changes.subtitleStyle='sermon';reasons.push('legenda de sermão');}
    if(has(text,'legenda louvor','worship')){changes.subtitleStyle='worship';reasons.push('legenda de louvor');}

    if(has(text,'sem marca dagua','sem marca d agua','sem marca d’água','remova marca','tirar marca')){changes.watermarkEnabled=false;reasons.push('marca d’água removida');}
    if(has(text,'com marca dagua','com marca d agua','mostrar marca','ative a marca')){changes.watermarkEnabled=true;reasons.push('marca d’água ativada');}
    if(has(text,'sem moldura','remova moldura')){changes.brandFrameEnabled=false;reasons.push('moldura removida');}
    if(has(text,'com moldura','ative moldura')){changes.brandFrameEnabled=true;reasons.push('moldura ativada');}

    return {changes,actions,reasons,recognized:Object.keys(changes).length>0||Object.keys(actions).length>0};
  }

  function install(project){injectStyles();project.clips.forEach(clip=>renderPanel(project,clip,false));}
  function renderPanel(project,clip,force=false){
    if(!clip)return;const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(!card)return;
    let panel=card.querySelector('.ai-edit-command-card');if(panel&&!force)return;
    if(!panel){panel=document.createElement('section');panel.className='ai-edit-command-card';const target=card.querySelector('.cc-mode-visual')||card.querySelector('.cc-mode-details')||card;target.prepend(panel);}
    panel.innerHTML=`<header><div><strong>AI EDIT COMMAND</strong><small>Descreva o estilo; o Studio mostra o que vai mudar antes de aplicar.</small></div><span>V1 local</span></header><div class="ai-edit-command-input"><textarea class="form-control" rows="2" data-ai-edit-input placeholder="Ex.: mais dinâmico, legenda clean, acompanhe o pregador, preserve pausas"></textarea><button type="button" class="btn btn-outline-light btn-sm" data-ai-edit-analyze>Interpretar</button></div><div class="ai-edit-examples">${examples.slice(0,3).map(example=>`<button type="button" data-ai-edit-example="${escapeHtml(example)}">${escapeHtml(short(example,52))}</button>`).join('')}</div><div class="ai-edit-plan" data-ai-edit-plan><small>Nenhuma instrução analisada.</small></div>`;
    panel.querySelectorAll('[data-ai-edit-example]').forEach(button=>button.onclick=()=>{panel.querySelector('[data-ai-edit-input]').value=button.dataset.aiEditExample;showPlan(project,clip,panel);});
    panel.querySelector('[data-ai-edit-analyze]').onclick=()=>showPlan(project,clip,panel);
  }

  function showPlan(project,clip,panel){
    const command=panel.querySelector('[data-ai-edit-input]').value.trim();if(!command)return toast('Escreva como você quer editar este corte');
    const result=parse(command,clip,project.options?.contentType||clip.editorialProfile);const host=panel.querySelector('[data-ai-edit-plan]');
    if(!result.recognized){host.innerHTML='<p class="ai-edit-warning">Não reconheci uma alteração segura. Tente mencionar ritmo, pausas, formato, câmera, legenda ou marca.</p>';return;}
    const changeItems=Object.entries(result.changes).map(([key,value])=>`<span><small>${label(key)}</small><b>${displayValue(key,value)}</b></span>`).join('');
    const actionItems=result.actions.analyzeFraming?'<span><small>Rastreamento</small><b>Analisar locutor</b></span>':'';
    host.innerHTML=`<div class="ai-edit-plan-list">${changeItems}${actionItems}</div><p>${result.reasons.map(reason=>`✓ ${escapeHtml(reason)}`).join(' · ')}</p><button type="button" class="btn btn-gold btn-sm" data-ai-edit-apply>Aplicar estas alterações</button>`;
    host.querySelector('[data-ai-edit-apply]').onclick=event=>apply(project,clip,panel,result,event.currentTarget);
  }

  async function apply(project,clip,panel,result,button){
    const old=button.textContent;button.disabled=true;button.textContent=result.actions.analyzeFraming?'Analisando câmera…':'Aplicando…';
    try{
      if(result.actions.analyzeFraming){
        const framed=await api(`/api/projects/${project.id}/clips/${clip.id}/analyze-framing`,{method:'POST'});
        Object.assign(clip,framed);
        button.textContent='Aplicando estilo…';
      }
      const payload={...result.changes};
      if(Object.keys(payload).length){
        const updated=await api(`/api/projects/${project.id}/clips/${clip.id}`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
        const serverClip=updated?.clips?.find?.(item=>item.id===clip.id);
        Object.assign(clip,serverClip||result.changes);
      }
      if(clip.videoPath)clip.renderOutdated=true;
      syncCardControls(clip);
      if(result.changes.subtitleStyle&&clip.subtitleTrack){
        const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(card){const style=card.querySelector('[name="subtitleTrackStyle"]');if(style)style.value=result.changes.subtitleStyle;clip.subtitleTrack.style=result.changes.subtitleStyle;if(typeof saveSubtitleTrackNow==='function')await saveSubtitleTrackNow(card,true);}
      }
      if(typeof window.AmadoJesusEditorAudit?.refresh==='function')window.AmadoJesusEditorAudit.refresh();
      toast(`✓ ${Object.keys(result.changes).length+(result.actions.analyzeFraming?1:0)} alterações aplicadas ao corte`);
      button.textContent='✓ Aplicado';setTimeout(()=>{button.disabled=false;button.textContent=old;},900);
    }catch(error){button.disabled=false;button.textContent=old;toast(error.message);}
  }

  function syncCardControls(clip){
    const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(!card)return;
    const map={layoutMode:clip.layoutMode,cropFocus:clip.cropFocus,outputPreset:clip.outputPreset,subtitleStyle:clip.subtitleStyle,transitionStyle:clip.transitionStyle,playbackSpeed:clip.playbackSpeed};
    for(const [name,value] of Object.entries(map)){const input=card.querySelector(`[name="${name}"]`);if(input&&value!=null)input.value=String(value);}
    const silence=card.querySelector('[name="silenceTrimmingEnabled"]');if(silence)silence.checked=clip.silenceTrimmingEnabled!==false;
    const watermark=card.querySelector('[name="watermarkEnabled"]');if(watermark)watermark.checked=clip.watermarkEnabled!==false;
    const frame=card.querySelector('[name="brandFrameEnabled"]');if(frame)frame.checked=clip.brandFrameEnabled!==false;
  }

  function label(key){return ({transitionStyle:'Transição',playbackSpeed:'Velocidade',silenceTrimmingEnabled:'Pausas',layoutMode:'Câmera',cropFocus:'Foco',outputPreset:'Formato',subtitleStyle:'Legenda',watermarkEnabled:'Marca d’água',brandFrameEnabled:'Moldura'})[key]||key;}
  function displayValue(key,value){if(typeof value==='boolean')return value?'Ligado':'Desligado';return ({dynamic:'Dinâmica',editorial:'Editorial',smooth:'Suave',split:'Duas pessoas',blur:'Fundo desfocado',fill:'Pessoa em foco',center:'Centro',vertical:'9:16',square:'1:1',landscape:'16:9',portrait:'4:5',clean:'Clean',impact:'Impacto',sermon:'Sermão',worship:'Louvor',podcast:'Podcast'})[String(value)]||String(value);}
  function short(value,max){return value.length<=max?value:`${value.slice(0,max-1)}…`;}

  function injectStyles(){if(document.querySelector('#ai-edit-command-styles'))return;const style=document.createElement('style');style.id='ai-edit-command-styles';style.textContent=`.ai-edit-command-card{padding:11px;border:1px solid rgba(123,127,238,.16);border-radius:12px;background:rgba(15,15,37,.48);margin-bottom:10px}.ai-edit-command-card>header{display:flex;justify-content:space-between;gap:8px}.ai-edit-command-card header>div{display:grid}.ai-edit-command-card header strong{font-size:.67rem}.ai-edit-command-card header small{font-size:.55rem;color:#8589aa}.ai-edit-command-card header>span{font-size:.54rem;color:#9c9fcd}.ai-edit-command-input{display:grid;grid-template-columns:1fr auto;gap:6px;margin-top:8px;align-items:end}.ai-edit-command-input textarea{resize:none;font-size:.65rem}.ai-edit-examples{display:flex;gap:4px;overflow:auto;margin-top:5px}.ai-edit-examples button{border:1px solid rgba(255,255,255,.06);background:transparent;color:#7d8198;border-radius:999px;padding:3px 6px;font-size:.52rem;white-space:nowrap}.ai-edit-plan{margin-top:8px}.ai-edit-plan>small{font-size:.56rem;color:#74788c}.ai-edit-plan-list{display:grid;grid-template-columns:repeat(3,1fr);gap:4px}.ai-edit-plan-list span{display:grid;padding:5px;background:rgba(255,255,255,.025);border-radius:7px}.ai-edit-plan-list small{font-size:.49rem;color:#767a91}.ai-edit-plan-list b{font-size:.61rem;color:#bec0e2}.ai-edit-plan>p{font-size:.54rem;color:#888da1;margin:6px 0}.ai-edit-warning{font-size:.58rem;color:#c3a16f;margin:0}@media(max-width:760px){.ai-edit-command-input{grid-template-columns:1fr}.ai-edit-plan-list{grid-template-columns:repeat(2,1fr)}}`;document.head.append(style);}
  injectStyles();window.AmadoJesusAiEdit={parse,applyCommand:async command=>{const clip=current?.clips?.find(c=>c.id===document.querySelector('#ccClipPicker')?.value);if(!clip)return;const result=parse(command,clip,current.options?.contentType);if(!result.recognized)throw new Error('Comando não reconhecido');const fake=document.createElement('button');fake.textContent='Aplicar';return apply(current,clip,document.querySelector(`.clip-card[data-clip="${clip.id}"] .ai-edit-command-card`),result,fake);}};
})();