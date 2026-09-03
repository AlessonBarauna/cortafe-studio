// Autopilot V8 · canais remotos + pastas vigiadas locais
(function(){
  const homeBase = home;
  home = async function(){
    await homeBase();
    await renderAutopilotPanel();
  };

  const newProjectBase = newProject;
  newProject = function(){
    newProjectBase();
    const tab=document.querySelector('[data-source="url"]'); if(tab)tab.textContent='Link (YouTube / Twitch / Kick)';
    const field=document.querySelector('#urlSource textarea[name="url"]'); if(field)field.placeholder='Um link do YouTube, Twitch ou Kick por linha';
    const help=document.querySelector('#urlSource>small'); if(help)help.textContent='YouTube, Twitch e Kick usam o processamento local do yt-dlp. Um link cria um projeto; várias linhas entram na fila.';
  };

  async function renderAutopilotPanel(){
    const grid=document.querySelector('#projects'), section=grid?.previousElementSibling;
    if(!grid||document.querySelector('.autopilot-panel'))return;
    injectAutopilotStyles();
    let config;
    try{config=await api('/api/autopilot')}catch{return}
    const panel=document.createElement('section'); panel.className='autopilot-panel';
    panel.innerHTML=`<header><div><span class="eyebrow">AUTOPILOT · CULTOS, LIVES E PASTAS</span><h2>Do culto para a fila, automaticamente.</h2><p>Monitore canais do YouTube e pastas do Windows. Vídeos novos entram no Studio sem precisar criar projeto manualmente.</p></div><label class="autopilot-switch"><input type="checkbox" data-autopilot-enabled ${config.enabled?'checked':''}><span>${config.enabled?'Ativo':'Desligado'}</span></label></header><div class="autopilot-settings"><label>Verificar a cada<select class="form-select" data-autopilot-poll>${[5,10,15,30,60,120].map(value=>`<option value="${value}" ${Number(config.pollMinutes)===value?'selected':''}>${value} min</option>`).join('')}</select></label><div class="autopilot-actions"><button class="btn btn-outline-light" type="button" data-autopilot-add>+ Canal</button><button class="btn btn-outline-light" type="button" data-folder-add>+ Pasta</button><button class="btn btn-outline-light" type="button" data-autopilot-check>Verificar agora</button><button class="btn btn-gold" type="button" data-autopilot-save>Salvar Autopilot</button></div></div><div class="autopilot-block"><div class="autopilot-block-title"><strong>Canais do YouTube</strong><small>Detecta o próximo culto ou vídeo concluído.</small></div><div class="autopilot-sources" data-autopilot-sources>${(config.sources||[]).map(sourceRow).join('')}</div></div><div class="autopilot-block"><div class="autopilot-block-title"><strong>Pastas vigiadas</strong><small>Ideal para gravações locais, OBS e pastas sincronizadas pelo Google Drive para computador.</small></div><div class="autopilot-sources" data-folder-sources>${(config.watchedFolders||[]).map(folderRow).join('')}</div></div><div class="autopilot-footer"><span data-autopilot-status>${escapeHtml(config.lastMessage||'Nenhuma verificação executada ainda.')}</span>${config.lastCheckAt?`<small>Última verificação: ${new Date(config.lastCheckAt).toLocaleString('pt-BR')}</small>`:''}</div>`;
    section?.before(panel);
    panel.querySelector('[data-autopilot-enabled]').addEventListener('change',event=>{panel.querySelector('.autopilot-switch span').textContent=event.target.checked?'Ativo':'Desligado'});
    panel.querySelector('[data-autopilot-add]').addEventListener('click',()=>addSourceRow(panel));
    panel.querySelector('[data-folder-add]').addEventListener('click',()=>addFolderRow(panel));
    panel.querySelector('[data-autopilot-save]').addEventListener('click',event=>saveAutopilot(panel,event.currentTarget));
    panel.querySelector('[data-autopilot-check]').addEventListener('click',event=>checkAutopilot(panel,event.currentTarget));
    bindRemoveButtons(panel);
  }

  function editorOptions(prefix,source={}){
    return `<div class="autopilot-source-options"><select class="form-select" data-${prefix}-content>${[['pregacao','Pregação'],['louvor','Louvor'],['podcast','Podcast']].map(([value,label])=>`<option value="${value}" ${source.contentType===value?'selected':''}>${label}</option>`).join('')}</select><select class="form-select" data-${prefix}-model>${['base','small','medium','large-v3'].map(value=>`<option value="${value}" ${source.whisperModel===value?'selected':''}>Whisper ${value}</option>`).join('')}</select><input class="form-control" type="number" min="1" max="20" data-${prefix}-clips value="${Math.max(1,Math.min(20,Number(source.clipCount)||20))}" title="Quantidade de cortes"><input class="form-control" data-${prefix}-topic value="${escapeHtml(source.topic||'')}" placeholder="Tema opcional"></div>`;
  }

  function sourceRow(source={}){
    const id=source.id||crypto.randomUUID().replaceAll('-','').slice(0,10);
    return `<article class="autopilot-source" data-source-id="${escapeHtml(id)}"><div class="autopilot-source-head"><input class="form-control" data-source-name value="${escapeHtml(source.name||'Meu canal')}" aria-label="Nome do canal"><label><input type="checkbox" data-source-enabled ${source.enabled!==false?'checked':''}> monitorar</label><button type="button" data-source-remove aria-label="Remover">×</button></div><input class="form-control" data-source-url value="${escapeHtml(source.url||'')}" placeholder="https://www.youtube.com/@seucanal">${editorOptions('source',source)}${source.lastError?`<small class="autopilot-error">${escapeHtml(source.lastError)}</small>`:source.lastSeenAt?`<small>Último conteúdo visto: ${new Date(source.lastSeenAt).toLocaleString('pt-BR')}</small>`:''}</article>`;
  }

  function folderRow(folder={}){
    const id=folder.id||crypto.randomUUID().replaceAll('-','').slice(0,10);
    return `<article class="autopilot-source watched-folder" data-folder-id="${escapeHtml(id)}"><div class="autopilot-source-head"><input class="form-control" data-folder-name value="${escapeHtml(folder.name||'Pasta de vídeos')}" aria-label="Nome da pasta"><label><input type="checkbox" data-folder-enabled ${folder.enabled!==false?'checked':''}> vigiar</label><button type="button" data-folder-remove aria-label="Remover">×</button></div><input class="form-control" data-folder-path value="${escapeHtml(folder.path||'')}" placeholder="C:\\Videos\\Cultos ou G:\\Meu Drive\\Cultos"><label class="folder-subfolders"><input type="checkbox" data-folder-subfolders ${folder.includeSubfolders?'checked':''}> incluir subpastas</label>${editorOptions('folder',folder)}${folder.lastError?`<small class="autopilot-error">${escapeHtml(folder.lastError)}</small>`:folder.lastImportedFile?`<small>Último importado: ${escapeHtml(folder.lastImportedFile)}</small>`:folder.lastScanAt?`<small>Última leitura: ${new Date(folder.lastScanAt).toLocaleString('pt-BR')}</small>`:''}</article>`;
  }

  function addSourceRow(panel){
    panel.querySelector('[data-autopilot-sources]').insertAdjacentHTML('beforeend',sourceRow()); bindRemoveButtons(panel);
  }

  function addFolderRow(panel){
    panel.querySelector('[data-folder-sources]').insertAdjacentHTML('beforeend',folderRow()); bindRemoveButtons(panel);
  }

  function bindRemoveButtons(panel){
    panel.querySelectorAll('[data-source-remove],[data-folder-remove]').forEach(button=>{if(button.dataset.bound)return;button.dataset.bound='1';button.addEventListener('click',()=>button.closest('.autopilot-source')?.remove())});
  }

  function collectConfig(panel){
    const sources=[...panel.querySelectorAll('[data-source-id]')].map(row=>({id:row.dataset.sourceId,name:row.querySelector('[data-source-name]').value.trim(),url:row.querySelector('[data-source-url]').value.trim(),enabled:row.querySelector('[data-source-enabled]').checked,contentType:row.querySelector('[data-source-content]').value,whisperModel:row.querySelector('[data-source-model]').value,clipCount:+row.querySelector('[data-source-clips]').value,topic:row.querySelector('[data-source-topic]').value.trim()||null}));
    const watchedFolders=[...panel.querySelectorAll('[data-folder-id]')].map(row=>({id:row.dataset.folderId,name:row.querySelector('[data-folder-name]').value.trim(),path:row.querySelector('[data-folder-path]').value.trim(),enabled:row.querySelector('[data-folder-enabled]').checked,includeSubfolders:row.querySelector('[data-folder-subfolders]').checked,contentType:row.querySelector('[data-folder-content]').value,whisperModel:row.querySelector('[data-folder-model]').value,clipCount:+row.querySelector('[data-folder-clips]').value,topic:row.querySelector('[data-folder-topic]').value.trim()||null}));
    return {enabled:panel.querySelector('[data-autopilot-enabled]').checked,pollMinutes:+panel.querySelector('[data-autopilot-poll]').value,sources,watchedFolders};
  }

  async function saveAutopilot(panel,button){
    const original=button.textContent;button.disabled=true;button.textContent='Salvando…';
    try{const saved=await api('/api/autopilot',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(collectConfig(panel))});panel.querySelector('[data-autopilot-status]').textContent=saved.enabled?'Autopilot ativo. Canais e pastas continuarão sendo verificados em segundo plano.':'Configuração salva. Autopilot permanece desligado.';toast('✓ Autopilot salvo')}catch(error){toast(error.message)}finally{button.disabled=false;button.textContent=original}
  }

  async function checkAutopilot(panel,button){
    const original=button.textContent;button.disabled=true;button.textContent='Verificando fontes…';
    try{await api('/api/autopilot',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(collectConfig(panel))});const result=await api('/api/autopilot/check',{method:'POST'});panel.querySelector('[data-autopilot-status]').textContent=result.messages?.join(' · ')||'Verificação concluída.';toast(result.projectsQueued?`✓ ${result.projectsQueued} projeto(s) entrou(aram) na fila`:'Nenhum conteúdo novo encontrado')}catch(error){toast(error.message)}finally{button.disabled=false;button.textContent=original}
  }

  function injectAutopilotStyles(){
    if(document.querySelector('#autopilot-styles'))return;
    const style=document.createElement('style');style.id='autopilot-styles';style.textContent=`.autopilot-panel{margin:0 0 32px;padding:22px;border:1px solid rgba(199,163,90,.26);border-radius:20px;background:linear-gradient(135deg,rgba(26,20,13,.94),rgba(13,17,23,.94));display:grid;gap:16px}.autopilot-panel header{display:flex;justify-content:space-between;gap:18px;align-items:flex-start}.autopilot-panel h2{font-size:1.35rem;margin:.3rem 0}.autopilot-panel p{margin:0;max-width:760px;color:#aaa;font-size:.85rem}.autopilot-switch{display:flex;align-items:center;gap:8px;background:rgba(255,255,255,.04);padding:8px 12px;border-radius:999px;white-space:nowrap}.autopilot-settings{display:flex;justify-content:space-between;align-items:flex-end;gap:12px}.autopilot-settings>label{display:grid;gap:5px;font-size:.72rem;color:#aaa}.autopilot-actions{display:flex;gap:7px;flex-wrap:wrap}.autopilot-block{display:grid;gap:9px;padding-top:4px}.autopilot-block+.autopilot-block{border-top:1px solid rgba(255,255,255,.07);padding-top:14px}.autopilot-block-title{display:flex;justify-content:space-between;gap:12px;align-items:baseline}.autopilot-block-title strong{font-size:.82rem}.autopilot-block-title small{color:#8994a2;font-size:.68rem}.autopilot-sources{display:grid;gap:10px}.autopilot-source{padding:12px;border:1px solid rgba(255,255,255,.08);border-radius:14px;background:rgba(0,0,0,.18);display:grid;gap:8px}.watched-folder{border-color:rgba(100,220,190,.15);background:rgba(7,23,20,.3)}.autopilot-source-head{display:grid;grid-template-columns:minmax(120px,1fr) auto auto;gap:8px;align-items:center}.autopilot-source-head label,.folder-subfolders{font-size:.72rem;color:#aaa;white-space:nowrap}.autopilot-source-head button{border:0;background:transparent;color:#aaa;font-size:1.4rem}.autopilot-source-options{display:grid;grid-template-columns:1fr 1fr 90px 1.4fr;gap:8px}.autopilot-source small{color:#8f9aa8}.autopilot-error{color:#db9c9c!important}.autopilot-footer{display:flex;justify-content:space-between;gap:12px;color:#a7b1bf;font-size:.75rem}.autopilot-footer small{white-space:nowrap}@media(max-width:800px){.autopilot-panel header,.autopilot-settings,.autopilot-footer,.autopilot-block-title{flex-direction:column;align-items:stretch}.autopilot-source-options{grid-template-columns:1fr 1fr}.autopilot-actions{width:100%}.autopilot-actions button{flex:1}}`;
    document.head.append(style);
  }
})();
