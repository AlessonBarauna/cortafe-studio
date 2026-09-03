// Persistência V2: captura a edição no instante do save e nunca deixa resposta antiga sobrescrever texto novo.
(function(){
  const chains=new Map();
  const versions=new Map();

  saveSubtitleTrackNow=async function(card,explicit=false){
    const clip=current?.clips.find(item=>item.id===card?.dataset.clip);if(!clip||!card)return;
    clearTimeout(subtitleAutosaveTimers.get(clip.id));
    const version=(versions.get(clip.id)||0)+1;versions.set(clip.id,version);
    const outgoing=collectSubtitleTrack(card,clip); // snapshot agora, antes de qualquer resposta anterior redesenhar a UI
    subtitleSaveState(card,'Salvando…','saving');
    const previous=chains.get(clip.id)||Promise.resolve();
    const task=previous.catch(()=>{}).then(async()=>{
      const saved=await api(`/api/projects/${current.id}/clips/${clip.id}/subtitles`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(outgoing)});
      clip.subtitleTrack=saved;
      if(versions.get(clip.id)===version){
        // Autosave não redesenha os blocos enquanto o usuário digita. O save explícito confirma com o estado retornado pelo servidor.
        if(explicit)redrawSubtitleBlocks(card,saved.blocks||[]);
        const enabled=card.querySelector('[name="subtitlesEnabled"]');if(enabled)enabled.checked=saved.enabled!==false;
        const style=card.querySelector('[name="subtitleTrackStyle"]');if(style)style.value=saved.style||style.value;
        const offset=card.querySelector('[name="subtitleOffset"]'),range=card.querySelector('[name="subtitleOffsetRange"]');
        if(offset&&document.activeElement!==offset)offset.value=saved.offsetSeconds||0;if(range&&document.activeElement!==range)range.value=saved.offsetSeconds||0;
        const x=card.querySelector('[name="subtitlePositionX"]'),y=card.querySelector('[name="subtitlePositionY"]');
        if(x&&document.activeElement!==x)x.value=saved.positionX||50;if(y&&document.activeElement!==y)y.value=saved.positionY||72;
        subtitleSaveState(card,explicit?'Salvo no projeto':'Salvo automaticamente','saved');
        const video=activateLiveSubtitlePreview(clip);if(video)updateSubtitlePreview(video,clip);
      }
      return saved;
    });
    chains.set(clip.id,task);
    try{return await task}catch(error){subtitleSaveState(card,'Falha ao salvar','error');toast(error.message);throw error}finally{if(chains.get(clip.id)===task)chains.delete(clip.id)}
  };
})();
