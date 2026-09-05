// Transcript Editor V1 · editar texto = editar vídeo
(function(){
  const cutSaveTimers=new Map();
  const boundVideos=new WeakSet();

  const collectBase=collectSubtitleTrack;
  collectSubtitleTrack=function(card,clip){
    const track=collectBase(card,clip);
    track.videoCuts=Array.isArray(clip?.subtitleTrack?.videoCuts)?clip.subtitleTrack.videoCuts:[];
    return track;
  };

  const renderBase=renderProject;
  renderProject=function(project){
    renderBase(project);
    if(project.status==='ready')requestAnimationFrame(()=>requestAnimationFrame(()=>mountTranscriptEditor(project)));
  };

  const selectBase=selectClip;
  selectClip=function(project,id){
    selectBase(project,id);
    setTimeout(()=>{
      if(document.querySelector('.transcript-editor-drawer.open'))openTranscriptEditor(project,id,false);
      bindPreviewSkip(project.clips.find(clip=>clip.id===id));
    },120);
  };

  function mountTranscriptEditor(project){
    const workspace=document.querySelector('.cc-workspace-v2');
    if(!workspace||workspace.querySelector('[data-transcript-editor]'))return;
    injectStyles();
    const editButton=workspace.querySelector('.cc-tool-rail [data-cc-mode="cut"]');
    if(editButton){
      const button=document.createElement('button');
      button.type='button';button.dataset.transcriptEditor='1';button.innerHTML='<b>¶</b><span>Texto</span>';
      editButton.after(button);
      button.onclick=()=>openTranscriptEditor(project,activeClipId(),true);
    }
    const inspector=workspace.querySelector('.cc-properties-panel');
    if(inspector&&!inspector.querySelector('.transcript-editor-drawer')){
      const drawer=document.createElement('section');drawer.className='transcript-editor-drawer';
      drawer.innerHTML='<header><div><span class="eyebrow">TRANSCRIPT EDITOR</span><h3>Edite o vídeo pelo texto</h3><small>Remover uma frase corta o mesmo trecho do vídeo e do áudio.</small></div><button type="button" data-transcript-close>×</button></header><div class="transcript-editor-summary" data-transcript-summary></div><div class="transcript-editor-list" data-transcript-list></div><footer><button type="button" class="btn btn-outline-light" data-transcript-restore>Restaurar tudo</button><button type="button" class="btn btn-gold" data-transcript-save>Salvar cortes</button></footer>';
      inspector.append(drawer);
      drawer.querySelector('[data-transcript-close]').onclick=()=>closeTranscriptEditor();
      drawer.querySelector('[data-transcript-restore]').onclick=()=>restoreAll(project);
      drawer.querySelector('[data-transcript-save]').onclick=event=>saveCuts(project,event.currentTarget,true);
    }
    workspace.querySelectorAll('.cc-tool-rail [data-cc-mode]').forEach(button=>button.addEventListener('click',()=>closeTranscriptEditor(),true));
    bindPreviewSkip(project.clips.find(clip=>clip.id===activeClipId()));
  }

  async function openTranscriptEditor(project,clipId,announce){
    const clip=project?.clips?.find(item=>item.id===clipId);if(!clip)return;
    const drawer=document.querySelector('.transcript-editor-drawer');if(!drawer)return;
    document.querySelectorAll('.cc-tool-rail button').forEach(button=>button.classList.toggle('active',button.dataset.transcriptEditor==='1'));
    drawer.classList.add('open');
    drawer.querySelector('[data-transcript-list]').innerHTML='<div class="transcript-loading">Carregando transcript salvo…</div>';
    try{
      clip.subtitleTrack=await api(`/api/projects/${project.id}/clips/${clip.id}/subtitles`);
      renderTranscript(project,clip);
      bindPreviewSkip(clip);
      if(announce)toast('Editor por texto aberto');
    }catch(error){drawer.querySelector('[data-transcript-list]').innerHTML=`<div class="transcript-loading error">${escapeHtml(error.message)}</div>`;}
  }

  function closeTranscriptEditor(){
    document.querySelector('.transcript-editor-drawer')?.classList.remove('open');
    document.querySelector('[data-transcript-editor]')?.classList.remove('active');
  }

  function renderTranscript(project,clip){
    const drawer=document.querySelector('.transcript-editor-drawer'),list=drawer?.querySelector('[data-transcript-list]');if(!list)return;
    const track=clip.subtitleTrack||{},blocks=track.blocks||[],cuts=track.videoCuts||[];
    if(!blocks.length){list.innerHTML='<div class="transcript-loading">Este corte ainda não possui transcript temporizado.</div>';return;}
    list.innerHTML=blocks.map((block,index)=>{
      const removed=isRemoved(block,cuts);
      return `<article class="transcript-line ${removed?'removed':''}" data-transcript-block="${escapeHtml(block.id||String(index))}" data-start="${Number(block.start)||0}" data-end="${Number(block.end)||0}"><button type="button" class="transcript-time" data-transcript-seek title="Ouvir a partir daqui">${formatTime(Number(block.start)||0)}</button><p>${escapeHtml(block.text||'')}</p><button type="button" class="transcript-cut-toggle" data-transcript-cut>${removed?'Restaurar':'Remover do vídeo'}</button></article>`;
    }).join('');
    list.querySelectorAll('[data-transcript-seek]').forEach(button=>button.onclick=()=>seekLine(clip,button.closest('.transcript-line')));
    list.querySelectorAll('[data-transcript-cut]').forEach(button=>button.onclick=()=>toggleCut(project,clip,button.closest('.transcript-line')));
    updateSummary(clip);
  }

  function toggleCut(project,clip,row){
    const start=+row.dataset.start,end=+row.dataset.end,text=row.querySelector('p')?.textContent||'';
    clip.subtitleTrack.videoCuts=Array.isArray(clip.subtitleTrack.videoCuts)?clip.subtitleTrack.videoCuts:[];
    const existingIndex=clip.subtitleTrack.videoCuts.findIndex(cut=>sameRange(cut,start,end));
    if(existingIndex>=0)clip.subtitleTrack.videoCuts.splice(existingIndex,1);
    else clip.subtitleTrack.videoCuts.push({id:crypto.randomUUID().replaceAll('-','').slice(0,10),start:+start.toFixed(3),end:+end.toFixed(3),text});
    clip.subtitleTrack.videoCuts.sort((a,b)=>a.start-b.start);
    renderTranscript(project,clip);
    scheduleSave(project,clip);
    bindPreviewSkip(clip);
  }

  function scheduleSave(project,clip){
    clearTimeout(cutSaveTimers.get(clip.id));
    cutSaveTimers.set(clip.id,setTimeout(()=>saveCuts(project,null,false),700));
  }

  async function saveCuts(project,button,explicit){
    const clip=project?.clips?.find(item=>item.id===activeClipId());if(!clip)return;
    const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);
    const old=button?.textContent;if(button){button.disabled=true;button.textContent='Salvando…';}
    try{
      let outgoing;
      if(card){outgoing=collectSubtitleTrack(card,clip);}
      else outgoing=structuredClone(clip.subtitleTrack||{});
      outgoing.videoCuts=clip.subtitleTrack?.videoCuts||[];
      const saved=await api(`/api/projects/${project.id}/clips/${clip.id}/subtitles`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(outgoing)});
      clip.subtitleTrack=saved;
      clip.renderOutdated=!!clip.videoPath;
      clip.lastPreviewFingerprint=null;
      renderTranscript(project,clip);
      if(explicit)toast('✓ Cortes do transcript salvos no projeto');
      if(button){button.textContent='✓ Salvo';setTimeout(()=>{button.disabled=false;button.textContent=old},800);}
      return saved;
    }catch(error){if(button){button.disabled=false;button.textContent=old;}toast(error.message);throw error;}
  }

  async function restoreAll(project){
    const clip=project?.clips?.find(item=>item.id===activeClipId());if(!clip)return;
    if(!(clip.subtitleTrack?.videoCuts||[]).length)return toast('Não há cortes de texto para restaurar');
    clip.subtitleTrack.videoCuts=[];
    renderTranscript(project,clip);
    await saveCuts(project,null,true);
  }

  function seekLine(clip,row){
    const video=document.querySelector('#preview video');if(!video)return;
    const start=+row.dataset.start;
    video.currentTime=video.dataset.sourcePreview?clip.start+start:start;
    video.play().catch(()=>{});
  }

  function bindPreviewSkip(clip){
    const video=document.querySelector('#preview video');if(!video||!clip)return;
    video._transcriptClip=clip;
    if(boundVideos.has(video))return;
    boundVideos.add(video);
    video.addEventListener('timeupdate',()=>{
      const currentClip=video._transcriptClip;if(!currentClip)return;
      const cuts=currentClip.subtitleTrack?.videoCuts||[];if(!cuts.length)return;
      const relative=video.currentTime-(video.dataset.sourcePreview?currentClip.start:0);
      const cut=cuts.find(item=>relative>=Number(item.start)-.025&&relative<Number(item.end)-.015);
      if(cut){
        const next=Number(cut.end)+(video.dataset.sourcePreview?currentClip.start:0);
        if(Number.isFinite(next)&&Math.abs(video.currentTime-next)>.02)video.currentTime=next;
      }
    });
  }

  function updateSummary(clip){
    const output=document.querySelector('[data-transcript-summary]');if(!output)return;
    const cuts=clip.subtitleTrack?.videoCuts||[];
    const removed=mergedDuration(cuts);
    const original=Math.max(0,clip.end-clip.start),final=Math.max(0,original-removed);
    output.innerHTML=`<span><b>${cuts.length}</b> ${cuts.length===1?'trecho removido':'trechos removidos'}</span><span><b>${removed.toFixed(1)}s</b> cortados</span><span><b>~${formatTime(final)}</b> final</span>`;
  }

  function mergedDuration(cuts){
    const ordered=(cuts||[]).map(cut=>({start:+cut.start,end:+cut.end})).filter(cut=>Number.isFinite(cut.start)&&Number.isFinite(cut.end)&&cut.end>cut.start).sort((a,b)=>a.start-b.start);
    if(!ordered.length)return 0;
    const merged=[{...ordered[0]}];
    for(const cut of ordered.slice(1)){const last=merged.at(-1);if(cut.start<=last.end+.02)last.end=Math.max(last.end,cut.end);else merged.push({...cut});}
    return merged.reduce((sum,cut)=>sum+cut.end-cut.start,0);
  }

  function isRemoved(block,cuts){return (cuts||[]).some(cut=>sameRange(cut,+block.start,+block.end));}
  function sameRange(cut,start,end){return Math.abs((+cut.start)-start)<.04&&Math.abs((+cut.end)-end)<.04;}
  function activeClipId(){return document.querySelector('.clip-card.active')?.dataset.clip||document.querySelector('#ccClipPicker')?.value||null;}
  function formatTime(seconds){seconds=Math.max(0,+seconds||0);const min=Math.floor(seconds/60),sec=Math.floor(seconds%60);return `${String(min).padStart(2,'0')}:${String(sec).padStart(2,'0')}`;}

  function injectStyles(){
    if(document.querySelector('#transcript-editor-styles'))return;
    const style=document.createElement('style');style.id='transcript-editor-styles';style.textContent=`
      .cc-properties-panel{position:relative}.transcript-editor-drawer{position:absolute;inset:0;z-index:40;background:#0f0d13;border-left:1px solid rgba(199,163,90,.24);display:none;grid-template-rows:auto auto minmax(0,1fr) auto;min-height:100%;overflow:hidden}.transcript-editor-drawer.open{display:grid}.transcript-editor-drawer>header{display:flex;justify-content:space-between;gap:12px;padding:16px;border-bottom:1px solid rgba(255,255,255,.08)}.transcript-editor-drawer h3{font-size:1rem;margin:.2rem 0}.transcript-editor-drawer header small{font-size:.68rem;color:#8f98a7}.transcript-editor-drawer header>button{align-self:flex-start;border:0;background:transparent;color:#999;font-size:1.4rem}.transcript-editor-summary{display:grid;grid-template-columns:repeat(3,1fr);gap:6px;padding:10px 12px;border-bottom:1px solid rgba(255,255,255,.07)}.transcript-editor-summary span{display:grid;padding:7px;border-radius:9px;background:rgba(255,255,255,.035);font-size:.58rem;color:#8e98a5}.transcript-editor-summary b{font-size:.78rem;color:#eee}.transcript-editor-list{overflow:auto;padding:10px;display:grid;gap:6px;align-content:start}.transcript-line{display:grid;grid-template-columns:43px minmax(0,1fr) auto;gap:8px;align-items:start;padding:9px;border:1px solid rgba(255,255,255,.07);border-radius:10px;background:rgba(255,255,255,.025)}.transcript-line.removed{opacity:.58;background:rgba(174,66,66,.09);border-color:rgba(220,90,90,.18)}.transcript-line.removed p{text-decoration:line-through}.transcript-line p{margin:0;font-size:.74rem;line-height:1.4;color:#ddd}.transcript-time,.transcript-cut-toggle{border:0;background:transparent}.transcript-time{color:#c7a35a;font-size:.64rem;padding:1px}.transcript-cut-toggle{color:#b7bec8;font-size:.6rem;white-space:nowrap}.transcript-line:not(.removed) .transcript-cut-toggle:hover{color:#e39797}.transcript-line.removed .transcript-cut-toggle{color:#9fd9c2}.transcript-editor-drawer>footer{display:flex;justify-content:space-between;gap:8px;padding:12px;border-top:1px solid rgba(255,255,255,.08)}.transcript-loading{padding:24px;text-align:center;color:#8993a0;font-size:.75rem}.transcript-loading.error{color:#d89b9b}.cc-tool-rail [data-transcript-editor].active{color:#c7a35a;background:rgba(199,163,90,.08)}@media(max-width:760px){.transcript-line{grid-template-columns:40px 1fr}.transcript-cut-toggle{grid-column:2;justify-self:start}.transcript-editor-summary{grid-template-columns:1fr}}
    `;document.head.append(style);
  }

  window.AmadoJesusTranscriptEditor={open:()=>openTranscriptEditor(current,activeClipId(),true),save:()=>saveCuts(current,null,true)};
})();