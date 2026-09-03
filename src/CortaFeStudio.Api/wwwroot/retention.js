(function () {
  const baseHome = home;
  home = async function () { await baseHome(); installRetentionButton(); };

  function installRetentionButton() {
    const toolbar = document.querySelector('#projects')?.previousElementSibling;
    const refresh = toolbar?.querySelector('[data-action="refresh"]');
    if (!refresh || toolbar.querySelector('[data-retention]')) return;
    refresh.insertAdjacentHTML('beforebegin', '<button class="btn btn-outline-light me-2" data-retention>Retenção</button>');
    toolbar.querySelector('[data-retention]').onclick = openRetentionCenter;
  }

  async function openRetentionCenter() {
    clearInterval(poller);
    try {
      const preview = await api('/api/storage/retention/preview');
      render(preview);
    } catch (error) { toast(error.message); }
  }

  function render(preview) {
    const policy = preview.policy;
    app.innerHTML = `<div class="workspace-title"><button class="back-link" onclick="home()">← Biblioteca</button><span class="eyebrow">RETENÇÃO INTELIGENTE</span><h1>Espaço sob controle.</h1><p class="text-secondary">Remova automaticamente arquivos antigos sem tocar em favoritos, fixados ou trabalhos em andamento.</p></div>
      <section class="retention-layout"><form id="retentionForm" class="studio-panel retention-policy">
        <div class="retention-switch"><div><strong>Limpeza automática</strong><small>Executada diariamente quando o Studio estiver aberto.</small></div><div class="form-check form-switch"><input class="form-check-input" type="checkbox" name="enabled" ${policy.enabled ? 'checked' : ''}></div></div>
        <label class="form-label mt-4">Remover após</label><div class="input-group"><input class="form-control" name="retentionDays" type="number" min="1" max="365" value="${policy.retentionDays}"><span class="input-group-text">dias</span></div>
        <label class="form-label mt-4">Modo de limpeza</label><select class="form-select" name="mode"><option value="projectData" ${policy.mode === 'projectData' ? 'selected' : ''}>Seguro · excluir arquivos e manter projeto</option><option value="fullProject" ${policy.mode === 'fullProject' ? 'selected' : ''}>Definitivo · excluir projeto completo</option></select>
        <div class="retention-protection"><span>✓ Favoritos protegidos</span><span>✓ Fixados protegidos</span><span>✓ Processamentos protegidos</span></div>
        <button class="btn btn-gold w-100 mt-4" type="submit">Salvar política</button>
      </form><div class="studio-panel retention-preview"><div class="d-flex justify-content-between align-items-start gap-3"><div><span class="eyebrow">PRÉVIA SEGURA</span><h3>${preview.candidates.length} projeto(s) elegível(is)</h3><p>${bytesLabel(preview.estimatedBytes)} podem ser liberados agora.</p></div><button class="btn btn-outline-danger" id="runRetention" ${preview.candidates.length ? '' : 'disabled'}>Executar agora</button></div>
      <div class="retention-list">${preview.candidates.length ? preview.candidates.map(item => `<article><div><strong>${escapeHtml(item.name)}</strong><small>${new Date(item.referenceDate).toLocaleDateString('pt-BR')} · ${bytesLabel(item.estimatedBytes)}</small></div><span>${item.willDeleteProject ? 'Excluir projeto' : 'Preservar histórico'}</span></article>`).join('') : '<div class="empty">Nenhum projeto atingiu o prazo configurado.</div>'}</div></div></section>`;
    document.querySelector('#retentionForm').onsubmit = savePolicy;
    document.querySelector('#runRetention').onclick = () => runRetention(preview);
  }

  async function savePolicy(event) {
    event.preventDefault(); const form = new FormData(event.currentTarget); const mode = form.get('mode');
    if (mode === 'fullProject' && !confirm('O modo definitivo exclui projetos completos após o prazo. Favoritos e fixados continuam protegidos. Deseja salvar?')) return;
    try {
      await api('/api/storage/retention', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ enabled: form.get('enabled') === 'on', retentionDays: Number(form.get('retentionDays')), mode, protectFavorites: true, protectPinned: true }) });
      toast('Política de retenção salva'); openRetentionCenter();
    } catch (error) { toast(error.message); }
  }

  async function runRetention(preview) {
    const destructive = preview.policy.mode === 'fullProject';
    if (!confirm(`${destructive ? 'Excluir definitivamente' : 'Remover os arquivos pesados de'} ${preview.candidates.length} projeto(s) agora?`)) return;
    try { const result = await api('/api/storage/retention/run', { method: 'POST' }); toast(`${bytesLabel(result.freedBytes)} liberados de ${result.processed} projeto(s)`); openRetentionCenter(); } catch (error) { toast(error.message); }
  }

  setTimeout(installRetentionButton, 600);
})();

/* Editor reliability patch. Este arquivo carrega antes do Editor V4, por isso pode
   preservar as ações internas do editor de legendas que o V4 interceptava. */
(function(){
  const saveChains=new Map();
  const saveVersions=new Map();

  // Captura os botões reais das legendas antes do listener do Editor V4.
  // O V4 confundia o data-edit-mode do CARD com um botão de aba e exibia apenas "LEGENDAS aberto".
  document.addEventListener('click',event=>{
    const subtitleAction=event.target.closest('.subtitle-editor button[onclick]');
    if(subtitleAction){
      const handler=subtitleAction.onclick;
      if(typeof handler==='function'){
        event.preventDefault();event.stopImmediatePropagation();
        handler.call(subtitleAction,event);
      }
      return;
    }
    const captionsTool=event.target.closest('.cc-tool-rail [data-cc-mode="captions"]');
    if(captionsTool){
      setTimeout(()=>{
        const clipId=document.querySelector('.clip-card.active')?.dataset.clip||document.querySelector('#ccClipPicker')?.value;
        if(current&&clipId&&typeof prepareSubtitleWorkspaceClip==='function')prepareSubtitleWorkspaceClip(current,clipId);
      },0);
    }
  },true);

  // Salva a fotografia exata do texto no momento da edição. Respostas antigas não podem
  // redesenhar a tela nem apagar caracteres digitados depois.
  saveSubtitleTrackNow=async function(card,explicit=false){
    const clip=current?.clips.find(item=>item.id===card?.dataset.clip);if(!clip||!card)return;
    clearTimeout(subtitleAutosaveTimers.get(clip.id));
    const version=(saveVersions.get(clip.id)||0)+1;saveVersions.set(clip.id,version);
    const outgoing=collectSubtitleTrack(card,clip);
    subtitleSaveState(card,'Salvando…','saving');
    const previous=saveChains.get(clip.id)||Promise.resolve();
    const task=previous.catch(()=>{}).then(async()=>{
      const saved=await api(`/api/projects/${current.id}/clips/${clip.id}/subtitles`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(outgoing)});
      clip.subtitleTrack=saved;
      if(saveVersions.get(clip.id)===version){
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
    saveChains.set(clip.id,task);
    try{return await task}catch(error){subtitleSaveState(card,'Falha ao salvar','error');toast(error.message);throw error}finally{if(saveChains.get(clip.id)===task)saveChains.delete(clip.id)}
  };

  // Torna a edição em massa realmente seletiva: cada corte ganha seu próprio checkbox
  // no painel IA & Lote, sincronizado com os checkboxes internos já usados pelo motor de lote.
  const renderBase=renderProject;
  renderProject=function(project){
    renderBase(project);
    if(project.status!=='ready')return;
    requestAnimationFrame(()=>requestAnimationFrame(()=>installBatchPicker(project)));
  };

  function installBatchPicker(project){
    const suite=document.querySelector('.aj-productivity-suite');if(!suite||suite.querySelector('.aj-batch-picker'))return;
    const picker=document.createElement('section');picker.className='aj-batch-picker';
    picker.innerHTML=`<div class="aj-batch-picker-head"><div><strong>Escolha os cortes</strong><small>Marque só os vídeos que devem receber o mesmo Template/Brand Kit.</small></div><button type="button" data-batch-clear>Limpar seleção</button></div><div class="aj-batch-picker-list">${project.clips.map((clip,index)=>`<label><input type="checkbox" data-batch-clip="${clip.id}"><span><b>${String(index+1).padStart(2,'0')}</b>${escapeHtml(clip.title)}</span><em>${Math.round(clip.score)} pts</em></label>`).join('')}</div><p class="aj-batch-help"><b>O que faz:</b> aplica de uma vez estilo de legenda, composição, transição, velocidade, remoção de pausas e/ou identidade visual aos cortes marcados. Não altera o texto falado nem junta vídeos.</p>`;
    const controls=suite.querySelector('.aj-productivity-controls');controls?.before(picker);
    picker.querySelectorAll('[data-batch-clip]').forEach(input=>input.addEventListener('change',()=>syncBatchInput(input)));
    picker.querySelector('[data-batch-clear]').onclick=()=>{picker.querySelectorAll('[data-batch-clip]').forEach(input=>{input.checked=false;syncBatchInput(input)});const all=suite.querySelector('[data-batch-select-all]');if(all)all.checked=false;};
    const selectAll=suite.querySelector('[data-batch-select-all]');
    if(selectAll)selectAll.addEventListener('change',()=>setTimeout(()=>picker.querySelectorAll('[data-batch-clip]').forEach(input=>input.checked=selectAll.checked),0));
  }

  function syncBatchInput(input){
    const card=document.querySelector(`.clip-card[data-clip="${input.dataset.batchClip}"]`),hidden=card?.querySelector('[data-batch-select]');
    if(!hidden)return;hidden.checked=input.checked;hidden.dispatchEvent(new Event('change',{bubbles:true}));
  }
})();
