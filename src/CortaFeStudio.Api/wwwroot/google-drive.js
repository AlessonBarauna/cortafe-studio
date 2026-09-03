// Google Drive V9 · OAuth no navegador, acesso somente leitura
(function(){
  const STORAGE_KEY='cortafe-google-drive-client-id';
  let accessToken=null;
  let tokenClient=null;
  let tokenExpiresAt=0;

  const homeBase=home;
  home=async function(){
    await homeBase();
    installDrivePanel();
  };

  function installDrivePanel(){
    const grid=document.querySelector('#projects'), anchor=grid?.previousElementSibling;
    if(!grid||document.querySelector('.drive-import-panel'))return;
    injectStyles();
    const clientId=localStorage.getItem(STORAGE_KEY)||'';
    const panel=document.createElement('section');
    panel.className='drive-import-panel';
    panel.innerHTML=`<header><div><span class="eyebrow">GOOGLE DRIVE · SOMENTE LEITURA</span><h2>Importe uma gravação sem baixar manualmente.</h2><p>O acesso acontece direto no navegador. O Studio não recebe sua senha do Google nem armazena client secret.</p></div><span class="drive-state" data-drive-state>desconectado</span></header><div class="drive-config"><input class="form-control" data-drive-client value="${escapeHtml(clientId)}" placeholder="Client ID OAuth do Google"><select class="form-select" data-drive-content><option value="pregacao">Pregação</option><option value="louvor">Louvor</option><option value="podcast">Podcast</option></select><select class="form-select" data-drive-model><option value="base">Whisper base</option><option value="small">Whisper small</option><option value="medium">Whisper medium</option><option value="large-v3">Whisper large-v3</option></select><input class="form-control" type="number" min="1" max="20" value="20" data-drive-clips title="Quantidade de cortes"><button type="button" class="btn btn-outline-light" data-drive-save>Salvar Client ID</button><button type="button" class="btn btn-gold" data-drive-connect>Conectar ao Drive</button></div><div class="drive-help">Use um OAuth Client ID do tipo <b>Web application</b> com a origem local do Studio autorizada. A permissão solicitada é <code>drive.readonly</code>.</div><div class="drive-files" data-drive-files><span class="text-secondary">Conecte sua conta para listar vídeos e áudios recentes.</span></div>`;
    anchor?.before(panel);
    panel.querySelector('[data-drive-save]').addEventListener('click',()=>saveClientId(panel));
    panel.querySelector('[data-drive-connect]').addEventListener('click',()=>connectDrive(panel));
  }

  function saveClientId(panel){
    const value=panel.querySelector('[data-drive-client]').value.trim();
    if(!value)return toast('Informe o Client ID OAuth do Google');
    localStorage.setItem(STORAGE_KEY,value);
    tokenClient=null; accessToken=null; tokenExpiresAt=0;
    toast('✓ Client ID salvo neste navegador');
  }

  async function connectDrive(panel){
    const clientId=panel.querySelector('[data-drive-client]').value.trim();
    if(!clientId)return toast('Informe e salve o Client ID OAuth do Google');
    localStorage.setItem(STORAGE_KEY,clientId);
    const button=panel.querySelector('[data-drive-connect]'), state=panel.querySelector('[data-drive-state]');
    button.disabled=true; button.textContent='Abrindo Google…';
    try{
      await loadGoogleIdentity();
      tokenClient=google.accounts.oauth2.initTokenClient({
        client_id:clientId,
        scope:'https://www.googleapis.com/auth/drive.readonly',
        callback:async response=>{
          if(response.error){state.textContent='falha na conexão';toast(response.error);button.disabled=false;button.textContent='Conectar ao Drive';return}
          accessToken=response.access_token;
          tokenExpiresAt=Date.now()+(Number(response.expires_in)||3600)*1000-30000;
          state.textContent='conectado · somente leitura';
          button.disabled=false;button.textContent='Reconectar';
          await loadDriveFiles(panel);
        }
      });
      tokenClient.requestAccessToken({prompt:accessToken?'':'consent'});
    }catch(error){button.disabled=false;button.textContent='Conectar ao Drive';state.textContent='indisponível';toast(error.message)}
  }

  function loadGoogleIdentity(){
    if(window.google?.accounts?.oauth2)return Promise.resolve();
    return new Promise((resolve,reject)=>{
      const existing=document.querySelector('script[data-google-drive-identity]');
      if(existing){existing.addEventListener('load',resolve,{once:true});existing.addEventListener('error',()=>reject(new Error('Não foi possível carregar o login do Google.')),{once:true});return}
      const script=document.createElement('script');
      script.src='https://accounts.google.com/gsi/client';script.async=true;script.defer=true;script.dataset.googleDriveIdentity='1';
      script.onload=resolve;script.onerror=()=>reject(new Error('Não foi possível carregar o login do Google. Verifique sua internet.'));
      document.head.append(script);
    });
  }

  async function loadDriveFiles(panel){
    if(!accessToken||Date.now()>tokenExpiresAt)return connectDrive(panel);
    const host=panel.querySelector('[data-drive-files]');
    host.innerHTML='<span class="text-secondary">Buscando mídias recentes…</span>';
    const query="trashed = false and (mimeType contains 'video/' or mimeType contains 'audio/')";
    const params=new URLSearchParams({q:query,pageSize:'40',orderBy:'modifiedTime desc',fields:'files(id,name,mimeType,size,modifiedTime,capabilities(canDownload))'});
    const response=await fetch(`https://www.googleapis.com/drive/v3/files?${params}`,{headers:{Authorization:`Bearer ${accessToken}`}});
    if(response.status===401){accessToken=null;return connectDrive(panel)}
    if(!response.ok)throw new Error('O Google Drive recusou a listagem de arquivos.');
    const data=await response.json(), files=(data.files||[]).filter(file=>file.capabilities?.canDownload!==false);
    if(!files.length){host.innerHTML='<span class="text-secondary">Nenhum vídeo ou áudio disponível para download foi encontrado.</span>';return}
    host.innerHTML=files.map(file=>driveFileCard(file)).join('');
    host.querySelectorAll('[data-drive-import]').forEach(button=>button.addEventListener('click',()=>importDriveFile(panel,files.find(file=>file.id===button.dataset.driveImport),button)));
  }

  function driveFileCard(file){
    const size=file.size?formatBytes(+file.size):'tamanho não informado';
    const modified=file.modifiedTime?new Date(file.modifiedTime).toLocaleString('pt-BR'):'';
    return `<article class="drive-file"><div><strong>${escapeHtml(file.name||'Arquivo')}</strong><small>${escapeHtml(file.mimeType||'mídia')} · ${size}${modified?' · '+modified:''}</small></div><button type="button" class="btn btn-outline-light btn-sm" data-drive-import="${escapeHtml(file.id)}">Importar</button></article>`;
  }

  async function importDriveFile(panel,file,button){
    if(!file)return;
    if(!accessToken||Date.now()>tokenExpiresAt)return connectDrive(panel);
    const original=button.textContent;button.disabled=true;button.textContent='Baixando…';
    try{
      const response=await fetch(`https://www.googleapis.com/drive/v3/files/${encodeURIComponent(file.id)}?alt=media`,{headers:{Authorization:`Bearer ${accessToken}`}});
      if(response.status===401){accessToken=null;throw new Error('A sessão do Google expirou. Conecte novamente.')}
      if(!response.ok)throw new Error('Não foi possível baixar este arquivo do Google Drive.');
      const blob=await response.blob();
      const form=new FormData();
      form.append('file',new File([blob],file.name||'drive-media',{type:file.mimeType||blob.type||'application/octet-stream'}));
      form.append('name',(file.name||'Google Drive').replace(/\.[^.]+$/,''));
      form.append('contentType',panel.querySelector('[data-drive-content]').value);
      form.append('whisperModel',panel.querySelector('[data-drive-model]').value);
      form.append('clipCount',panel.querySelector('[data-drive-clips]').value);
      button.textContent='Enviando ao Studio…';
      const project=await api('/api/projects/upload',{method:'POST',body:form});
      toast(`✓ “${project.name||file.name}” entrou na fila`);
      button.textContent='✓ Importado';
      setTimeout(()=>home(),800);
    }catch(error){toast(error.message);button.disabled=false;button.textContent=original}
  }

  function formatBytes(value){
    if(!Number.isFinite(value)||value<=0)return '0 B';
    const units=['B','KB','MB','GB','TB'];let index=0,number=value;
    while(number>=1024&&index<units.length-1){number/=1024;index++}
    return `${number.toFixed(index>1?1:0)} ${units[index]}`;
  }

  function injectStyles(){
    if(document.querySelector('#google-drive-styles'))return;
    const style=document.createElement('style');style.id='google-drive-styles';
    style.textContent=`.drive-import-panel{margin:0 0 28px;padding:20px;border:1px solid rgba(87,163,255,.22);border-radius:18px;background:linear-gradient(135deg,rgba(9,18,31,.94),rgba(13,17,23,.94));display:grid;gap:13px}.drive-import-panel header{display:flex;justify-content:space-between;gap:18px;align-items:flex-start}.drive-import-panel h2{font-size:1.2rem;margin:.25rem 0}.drive-import-panel p{margin:0;color:#9ca8b8;font-size:.8rem}.drive-state{border:1px solid rgba(87,163,255,.2);border-radius:999px;padding:6px 10px;color:#9fc7ff;font-size:.7rem;white-space:nowrap}.drive-config{display:grid;grid-template-columns:minmax(220px,2fr) 1fr 1fr 85px auto auto;gap:8px}.drive-help{font-size:.68rem;color:#8794a5}.drive-help code{color:#9fc7ff}.drive-files{display:grid;gap:7px;max-height:330px;overflow:auto}.drive-file{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:9px 11px;border:1px solid rgba(255,255,255,.07);border-radius:11px;background:rgba(0,0,0,.16)}.drive-file>div{min-width:0;display:grid}.drive-file strong{font-size:.76rem;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.drive-file small{font-size:.64rem;color:#8190a2}@media(max-width:1100px){.drive-config{grid-template-columns:1fr 1fr}.drive-config [data-drive-client]{grid-column:1/-1}}@media(max-width:700px){.drive-import-panel header{flex-direction:column}.drive-config{grid-template-columns:1fr}.drive-config [data-drive-client]{grid-column:auto}.drive-file{align-items:flex-start;flex-direction:column}.drive-file button{width:100%}}`;
    document.head.append(style);
  }
})();
