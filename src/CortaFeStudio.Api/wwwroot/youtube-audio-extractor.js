// Extração leve de áudio do YouTube: reaproveita a fila existente com processingMode=audioOnly.
(function(){
  const baseNewProject = newProject;
  const baseSubmitProject = submitProject;
  const baseRenderProject = renderProject;
  const baseHome = home;

  newProject = function(){
    baseNewProject();
    installAudioMode();
  };

  submitProject = async function(event){
    if(source !== 'audio') return baseSubmitProject(event);
    event.preventDefault();
    const button = event.submitter || event.target.querySelector('[type="submit"]');
    if(button){button.disabled=true;button.textContent='Adicionando à fila…';}
    try{
      const form = new FormData(event.target);
      const urls = String(form.get('audioUrls')||'').split(/\r?\n/).map(x=>x.trim()).filter(Boolean);
      if(!urls.length) throw new Error('Cole pelo menos um link do YouTube.');
      if(urls.length > 20) throw new Error('Envie no máximo 20 links por lote.');
      if(urls.some(url=>!isYouTubeUrl(url))) throw new Error('A extração de áudio aceita somente links do YouTube.');

      const options = {
        contentType: 'pregacao',
        clipCount: 1,
        minDuration: 60,
        maxDuration: 75,
        whisperModel: 'base',
        processingMode: 'audioOnly',
        audioFormat: form.get('audioFormat') || 'mp3',
        audioQuality: form.get('audioQuality') || 'high',
        audioOutputDirectory: String(form.get('audioOutputDirectory')||'').trim() || null,
        audioOrganizeByChannel: form.get('audioOrganizeByChannel') === 'on',
        audioDownloadPlaylist: form.get('audioDownloadPlaylist') === 'on',
        audioOrganizeByPlaylist: form.get('audioOrganizeByPlaylist') === 'on'
      };

      let result;
      if(urls.length > 1){
        result = await api('/api/projects/url-batch',{
          method:'POST',headers:{'Content-Type':'application/json'},
          body:JSON.stringify({urls,name:form.get('audioName'),options})
        });
        toast(`${result.length} extrações adicionadas à fila`);
        return home();
      }

      result = await api('/api/projects/url',{
        method:'POST',headers:{'Content-Type':'application/json'},
        body:JSON.stringify({url:urls[0],name:form.get('audioName'),options})
      });
      openProject(result.id);
    }catch(error){
      toast(error.message);
      if(button){button.disabled=false;button.textContent='Extrair áudio →';}
    }
  };

  renderProject = function(project){
    if(project?.status === 'ready' && project?.options?.processingMode === 'audioOnly'){
      renderAudioProject(project);
      return;
    }
    baseRenderProject(project);
  };

  home = async function(){
    await baseHome();
    for(const project of projects.filter(item=>item.options?.processingMode==='audioOnly')){
      const card=document.querySelector(`.project-card[data-id="${project.id}"]`);
      const eyebrow=card?.querySelector('.eyebrow');
      if(eyebrow) eyebrow.textContent='ÁUDIO · YOUTUBE';
    }
  };

  function installAudioMode(){
    const form=document.querySelector('#projectForm');
    const sourcePanel=form?.querySelector('.source-panel');
    const tabs=sourcePanel?.querySelector('.source-tabs');
    const aside=form?.querySelector('.settings-panel');
    if(!form||!sourcePanel||!tabs||!aside||tabs.querySelector('[data-source="audio"]')) return;

    const standardSettings=document.createElement('div');
    standardSettings.dataset.standardSettings='1';
    while(aside.firstChild) standardSettings.append(aside.firstChild);
    aside.append(standardSettings);

    const audioSettings=document.createElement('div');
    audioSettings.dataset.audioSettings='1';
    audioSettings.className='d-none';
    audioSettings.innerHTML=`
      <span class="panel-number">AUDIO</span><h3>Extrair somente áudio</h3>
      <p class="text-secondary small">Não roda Whisper, IA de cortes, tracking ou render vertical.</p>
      <label class="form-label">Nome da tarefa <span class="text-secondary">(opcional)</span></label>
      <input class="form-control" name="audioName" placeholder="Ex.: Pregações para ouvir">
      <div class="row g-2 mt-2"><div class="col-6"><label class="form-label">Formato</label><select class="form-select" name="audioFormat"><option value="mp3">MP3</option><option value="m4a">M4A</option><option value="wav">WAV</option></select></div><div class="col-6"><label class="form-label">Qualidade</label><select class="form-select" name="audioQuality"><option value="high">Alta</option><option value="medium">Média</option><option value="compact">Compacta</option></select></div></div>
      <label class="form-label mt-3">Pasta de destino</label>
      <input class="form-control" name="audioOutputDirectory" placeholder="Vazio = sua pasta Músicas\\AmadoJesus\\YouTube">
      <small class="text-secondary d-block mt-1">Você também pode informar um caminho absoluto, por exemplo C:\\Audios\\Pregacoes.</small>
      <div class="form-check mt-3"><input class="form-check-input" type="checkbox" name="audioOrganizeByChannel" id="audioByChannel" checked><label class="form-check-label" for="audioByChannel">Organizar por canal</label></div>
      <div class="form-check mt-2"><input class="form-check-input" type="checkbox" name="audioDownloadPlaylist" id="audioPlaylist"><label class="form-check-label" for="audioPlaylist">Baixar playlist inteira quando o link for de playlist</label></div>
      <div class="form-check mt-2"><input class="form-check-input" type="checkbox" name="audioOrganizeByPlaylist" id="audioByPlaylist" checked><label class="form-check-label" for="audioByPlaylist">Criar pasta da playlist</label></div>
      <small class="text-secondary d-block mt-2">Playlists são limitadas a 200 itens por segurança.</small>
      <button class="btn btn-hero w-100 mt-4" type="submit">Extrair áudio <span>→</span></button>`;
    aside.append(audioSettings);

    const audioTab=document.createElement('button');
    audioTab.type='button';audioTab.dataset.source='audio';audioTab.textContent='Extrair áudio';
    tabs.append(audioTab);

    const audioSource=document.createElement('div');
    audioSource.id='audioSource';audioSource.className='source-body d-none';
    audioSource.innerHTML=`<div class="d-flex justify-content-between align-items-center"><label class="form-label">Cole um ou vários links do YouTube</label><span class="badge rounded-pill text-bg-warning">LOTE · ATÉ 20</span></div><div class="url-field align-items-start"><span class="mt-2">♫</span><textarea class="form-control" name="audioUrls" rows="5" placeholder="Um link por linha"></textarea></div><small>Somente o áudio será salvo na pasta local escolhida.</small>`;
    sourcePanel.append(audioSource);

    const url=document.querySelector('#urlSource'),upload=document.querySelector('#uploadSource');
    const standardTabs=[...tabs.querySelectorAll('[data-source]')].filter(tab=>tab!==audioTab);
    audioTab.onclick=()=>{
      source='audio';
      tabs.querySelectorAll('[data-source]').forEach(tab=>tab.classList.toggle('active',tab===audioTab));
      url?.classList.add('d-none');upload?.classList.add('d-none');audioSource.classList.remove('d-none');
      standardSettings.classList.add('d-none');audioSettings.classList.remove('d-none');
    };
    standardTabs.forEach(tab=>tab.addEventListener('click',()=>{
      audioSource.classList.add('d-none');standardSettings.classList.remove('d-none');audioSettings.classList.add('d-none');
    }));
    form.onsubmit=submitProject;
  }

  function renderAudioProject(project){
    const root=document.querySelector('#projectView');if(!root)return;
    const files=Array.isArray(project.audioOutputFiles)?project.audioOutputFiles:[];
    root.innerHTML=`<div class="workspace-title"><button class="back-link" data-route="home">← Biblioteca</button><span class="eyebrow">EXTRAÇÃO DE ÁUDIO CONCLUÍDA</span><h1>${escapeHtml(project.name)}</h1><p class="text-secondary">${escapeHtml(project.stage)}</p></div><div class="studio-panel p-4"><div class="d-flex justify-content-between gap-3 flex-wrap align-items-center"><div><span class="eyebrow">ARQUIVOS SALVOS</span><h3>${files.length} ${files.length===1?'arquivo':'arquivos'}</h3></div><button class="btn btn-gold" data-new-audio>Nova extração</button></div><div class="audio-output-list mt-4">${files.length?files.map((path,index)=>`<article class="audio-output-row"><div><strong>${escapeHtml(fileName(path))}</strong><small>${escapeHtml(path)}</small></div><button class="btn btn-sm btn-outline-light" data-copy-audio="${index}">Copiar caminho</button></article>`).join(''):'<p class="text-secondary">Nenhum caminho de saída foi registrado.</p>'}</div><p class="text-secondary small mt-3 mb-0">Os arquivos ficam fora do armazenamento temporário do Studio; excluir esta tarefa da biblioteca não apaga seus áudios.</p></div>`;
    bindCommon();
    root.querySelector('[data-new-audio]').onclick=()=>{newProject();setTimeout(()=>document.querySelector('[data-source="audio"]')?.click(),0)};
    root.querySelectorAll('[data-copy-audio]').forEach(button=>button.onclick=async()=>{
      const path=files[Number(button.dataset.copyAudio)];
      try{await navigator.clipboard.writeText(path);toast('Caminho copiado')}catch{toast(path)}
    });
    injectStyles();
  }

  function isYouTubeUrl(value){
    try{const url=new URL(value),host=url.hostname.toLowerCase();return ['youtube.com','www.youtube.com','m.youtube.com','music.youtube.com','youtu.be'].includes(host)||host.endsWith('.youtube.com')}catch{return false}
  }
  function fileName(path){return String(path||'').split(/[\\/]/).pop()||path;}
  function injectStyles(){
    if(document.querySelector('#youtube-audio-extractor-styles'))return;
    const style=document.createElement('style');style.id='youtube-audio-extractor-styles';style.textContent=`.audio-output-list{display:grid;gap:8px}.audio-output-row{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 14px;border:1px solid rgba(255,255,255,.07);border-radius:10px;background:rgba(255,255,255,.025)}.audio-output-row>div{display:grid;min-width:0}.audio-output-row strong{font-size:.78rem}.audio-output-row small{font-size:.58rem;color:#7d838d;overflow-wrap:anywhere}@media(max-width:680px){.audio-output-row{align-items:flex-start;flex-direction:column}}`;
    document.head.append(style);
  }
  injectStyles();
})();
