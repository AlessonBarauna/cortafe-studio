// Fluxo simples e persistente de legendas: backend é a fonte oficial.
(function(){
  let loadingClipId=null;

  async function loadSavedTrack(clipId,{announce=false}={}){
    if(!current||!clipId||loadingClipId===clipId)return;
    const clip=current.clips?.find(item=>item.id===clipId);if(!clip)return;
    loadingClipId=clipId;
    try{
      const saved=await api(`/api/projects/${current.id}/clips/${clipId}/subtitles`);
      clip.subtitleTrack=saved;
      const card=document.querySelector(`.clip-card[data-clip="${clipId}"]`);
      if(card){
        const enabled=card.querySelector('[name="subtitlesEnabled"]');if(enabled)enabled.checked=saved.enabled!==false;
        const style=card.querySelector('[name="subtitleTrackStyle"]');if(style)style.value=saved.style||style.value;
        const offset=card.querySelector('[name="subtitleOffset"]');if(offset)offset.value=saved.offsetSeconds||0;
        const offsetRange=card.querySelector('[name="subtitleOffsetRange"]');if(offsetRange)offsetRange.value=saved.offsetSeconds||0;
        const x=card.querySelector('[name="subtitlePositionX"]');if(x)x.value=Number.isFinite(+saved.positionX)?saved.positionX:50;
        const y=card.querySelector('[name="subtitlePositionY"]');if(y)y.value=Number.isFinite(+saved.positionY)?saved.positionY:72;
        redrawSubtitleBlocks(card,saved.blocks||[]);
        subtitleSaveState(card,'Salvo no projeto','saved');
      }
      const video=typeof activateLiveSubtitlePreview==='function'?activateLiveSubtitlePreview(clip):document.querySelector('#preview video');
      if(video&&typeof updateSubtitlePreview==='function')updateSubtitlePreview(video,clip);
      refreshCaptionBadges();
      if(announce)toast('✓ Legendas carregadas do projeto');
      return saved;
    }catch(error){
      if(announce)toast(`Não foi possível carregar as legendas: ${error.message}`);
      throw error;
    }finally{loadingClipId=null;}
  }

  function activeClipId(){return document.querySelector('.clip-card.active')?.dataset.clip||document.querySelector('#ccClipPicker')?.value||null;}
  function captionsOpen(){return document.querySelector('.cc-tool-rail [data-cc-mode="captions"].active')!==null||document.querySelector('.clip-card.active')?.dataset.editMode==='captions';}

  const originalSaveExplicit=window.saveSubtitlesExplicitly;
  window.saveSubtitlesExplicitly=async function(button){
    const card=button.closest('.clip-card'),clip=current?.clips.find(item=>item.id===card?.dataset.clip);if(!card||!clip)return;
    button.disabled=true;const old=button.textContent;button.textContent='Salvando…';
    try{
      const outgoing=collectSubtitleTrack(card,clip);
      outgoing.enabled=true;
      const saved=await api(`/api/projects/${current.id}/clips/${clip.id}/subtitles`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(outgoing)});
      clip.subtitleTrack=saved;
      const verified=await api(`/api/projects/${current.id}/clips/${clip.id}/subtitles`);
      clip.subtitleTrack=verified;
      redrawSubtitleBlocks(card,verified.blocks||[]);
      const enabled=card.querySelector('[name="subtitlesEnabled"]');if(enabled)enabled.checked=verified.enabled!==false;
      subtitleSaveState(card,'Salvo no projeto','saved');
      const video=activateLiveSubtitlePreview(clip);if(video)updateSubtitlePreview(video,clip);
      refreshCaptionBadges();
      button.textContent='✓ Salvo';toast('✓ Legendas salvas e confirmadas no projeto');
      setTimeout(()=>{button.disabled=false;button.textContent=old},900);
      return verified;
    }catch(error){button.disabled=false;button.textContent=old;subtitleSaveState(card,'Falha ao salvar','error');toast(error.message);throw error;}
  };

  window.saveAndRenderSubtitles=async function(button){
    const card=button.closest('.clip-card'),clipId=card?.dataset.clip,clip=current?.clips.find(item=>item.id===clipId);if(!card||!clip)return;
    button.disabled=true;const old=button.textContent;
    try{
      button.textContent='Salvando legendas…';
      const fake=document.createElement('button');fake.closest=()=>card;fake.textContent='Salvar legendas';fake.disabled=false;
      await window.saveSubtitlesExplicitly(fake);
      button.textContent='Gerando vídeo com legendas…';
      await api(`/api/projects/${current.id}/clips/${clipId}/render`,{method:'POST'});
      await openProject(current.id);
      selectClip(current,clipId);
      await loadSavedTrack(clipId);
      document.querySelector('.cc-tool-rail [data-cc-mode="captions"]')?.click();
      refreshCaptionBadges();
      toast('✓ Vídeo gerado com as legendas salvas');
    }catch(error){toast(error.message);}finally{button.disabled=false;button.textContent=old;}
  };

  document.addEventListener('click',event=>{
    const captions=event.target.closest('.cc-tool-rail [data-cc-mode="captions"],[data-cc-edit-captions]');
    if(captions){
      const id=captions.dataset.ccEditCaptions||activeClipId();
      setTimeout(async()=>{try{await loadSavedTrack(id);}catch{}},120);
      return;
    }
    const otherMode=event.target.closest('.cc-tool-rail [data-cc-mode]:not([data-cc-mode="captions"])');
    if(otherMode&&captionsOpen()){
      const id=activeClipId(),card=id?document.querySelector(`.clip-card[data-clip="${id}"]`):null;
      if(card&&typeof saveSubtitleTrackNow==='function')saveSubtitleTrackNow(card).catch(()=>{});
    }
  },true);

  const selectBase=selectClip;
  selectClip=function(project,id){
    selectBase(project,id);
    setTimeout(()=>{if(captionsOpen())loadSavedTrack(id).catch(()=>{});refreshCaptionBadges();},100);
  };

  const renderBase=renderProject;
  renderProject=function(project){
    renderBase(project);
    if(project.status==='ready')setTimeout(refreshCaptionBadges,80);
  };

  function refreshCaptionBadges(){
    document.querySelectorAll('.cc-asset').forEach(asset=>{
      const id=asset.dataset.ccClip,clip=current?.clips?.find(item=>item.id===id);if(!clip)return;
      const label=asset.querySelector('.cc-caption-ready');
      const saved=clip.subtitleTrack?.enabled&&clip.subtitleTrack?.blocks?.some(block=>block.enabled!==false&&String(block.text||'').trim());
      if(saved){
        if(label)label.textContent='✓ Legendas salvas';
        else asset.querySelector('span:nth-child(2)')?.insertAdjacentHTML('beforeend','<small class="cc-caption-ready">✓ Legendas salvas</small>');
      }else label?.remove();
    });
  }

  window.AmadoJesusSubtitleFlow={reload:()=>loadSavedTrack(activeClipId(),{announce:true})};
})();
