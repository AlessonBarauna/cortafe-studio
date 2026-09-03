// Prévia de legendas: usa o corte vertical limpo, nunca o vídeo-fonte horizontal como tela principal.
(function(){
  const preparing=new Map();

  function activeClipId(){
    return document.querySelector('.clip-card.active')?.dataset.clip||document.querySelector('#ccClipPicker')?.value||null;
  }

  function captionsOpen(){
    return document.querySelector('.cc-tool-rail [data-cc-mode="captions"].active')!==null||document.querySelector('.clip-card.active')?.dataset.editMode==='captions';
  }

  async function ensureVerticalPreview(clipId){
    if(!current||!clipId)return;
    const clip=current.clips?.find(item=>item.id===clipId),card=document.querySelector(`.clip-card[data-clip="${clipId}"]`);
    if(!clip||!card)return;
    const key=`${current.id}:${clipId}:${clip.lastPreviewFingerprint||''}`;
    if(preparing.has(key))return preparing.get(key);

    const task=(async()=>{
      try{
        subtitleSaveState(card,'Preparando prévia vertical…','saving');
        // O endpoint /preview usa o mesmo framing/layout do corte, mas sem queimar as legendas.
        // Isso permite editar o texto por cima de uma base visual fiel ao vídeo final.
        if(typeof prepareSubtitleWorkspaceClip==='function')await prepareSubtitleWorkspaceClip(current,clipId);
        else if(typeof ensureCleanSubtitlePreview==='function')await ensureCleanSubtitlePreview(card);

        const video=typeof activateLiveSubtitlePreview==='function'?activateLiveSubtitlePreview(clip):document.querySelector('#preview video');
        if(video&&typeof updateSubtitlePreview==='function'){
          updateSubtitlePreview(video,clip);
          if(typeof enableSubtitleDrag==='function')enableSubtitleDrag(card);
        }
        subtitleSaveState(card,'Legendas salvas · prévia vertical','saved');
        return video;
      }catch(error){
        subtitleSaveState(card,'Prévia vertical indisponível','editing');
        console.warn('Falha ao preparar prévia vertical de legendas',error);
        return null;
      }
    })();
    preparing.set(key,task);
    try{return await task}finally{preparing.delete(key)}
  }

  // A antiga regra escondia a legenda em qualquer vídeo que tivesse videoPath, inclusive
  // a prévia limpa vertical. Agora só evitamos sobreposição fora do modo Legendas quando
  // o player é realmente o MP4 final (que pode já ter a legenda queimada).
  updateSubtitlePreview=function(video,clip){
    const overlay=document.querySelector('#preview .subtitle-preview'),track=clip?.subtitleTrack;
    const editing=captionsOpen();
    const livePreview=!!(video?.dataset.sourcePreview||video?.dataset.clipPreview||video?.dataset.subtitlePreviewKind);
    if(video&&!editing&&!livePreview&&clip?.videoPath){if(overlay)overlay.textContent='';return;}
    if(!overlay||!track||(!track.enabled&&!editing)){if(overlay)overlay.textContent='';return;}
    const relative=video.currentTime-(video.dataset.sourcePreview?clip.start:0),offset=track.offsetSeconds||0;
    const active=(track.blocks||[]).find(block=>block.enabled!==false&&relative>=block.start+offset&&relative<=block.end+offset);
    overlay.textContent=active?.text||'';
    overlay.dataset.style=track.style||'impact';
    if(typeof applySubtitlePosition==='function')applySubtitlePosition(overlay,track);
    document.querySelectorAll(`[data-clip="${clip.id}"] .subtitle-block`).forEach(block=>block.classList.toggle('active',block.dataset.subtitleId===active?.id));
  };

  // Entrou em Legendas ou clicou em Editar legendas: aguarda a prévia vertical ficar pronta.
  document.addEventListener('click',event=>{
    const trigger=event.target.closest('.cc-tool-rail [data-cc-mode="captions"],[data-cc-edit-captions]');
    if(!trigger)return;
    const id=trigger.dataset.ccEditCaptions||activeClipId();
    setTimeout(()=>ensureVerticalPreview(id),160);
  },true);

  // Mudou de corte enquanto a aba Legendas está aberta.
  const selectBase=selectClip;
  selectClip=function(project,id){
    selectBase(project,id);
    if(captionsOpen())setTimeout(()=>ensureVerticalPreview(id),160);
  };

  window.AmadoJesusCaptionPreview={refresh:()=>ensureVerticalPreview(activeClipId())};
})();
