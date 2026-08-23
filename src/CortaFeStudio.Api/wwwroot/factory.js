let factoryPoller;

function factoryStatusLabel(value){return ({queued:'Na fila',analyzing:'Analisando',rendering:'Renderizando',awaitingApproval:'Aguardando aprovação',ready:'Pronto',scheduled:'Agendado',published:'Publicado',failed:'Falhou',cancelled:'Cancelado'})[value]||value}

async function factoryCenter(){
  clearInterval(poller); clearInterval(factoryPoller); template('#factoryTemplate');
  document.querySelector('#factoryView').innerHTML=`
    <div class="factory-hero"><div><button class="back-link" data-route="home">← Biblioteca</button><span class="eyebrow d-block mt-4">LINHA DE PRODUÇÃO LOCAL</span><h1>Modo fábrica.<br><em>Volume com controle.</em></h1><p>Transforme um vídeo longo em uma fila completa de cortes, revisão e calendário.</p></div><button class="btn btn-hero" id="newFactory">Nova produção →</button></div>
    <div id="factoryForm"></div><div class="section-head mt-5"><div><span class="eyebrow">OPERAÇÕES</span><h2>Produções recentes</h2></div></div><div id="factoryBatches" class="factory-grid"></div>`;
  bindCommon(); document.querySelector('#newFactory').onclick=showFactoryForm; await refreshFactory();
  factoryPoller=setInterval(refreshFactory,3000);
}

function showFactoryForm(){
  document.querySelector('#factoryForm').innerHTML=`<form id="factoryCreate" class="factory-console mt-4">
    <div class="factory-console-head"><div><span class="eyebrow">CONFIGURAÇÃO DA LINHA</span><h3>Nova produção automatizada</h3></div><span class="safety-badge">Publicação automática desligada</span></div>
    <div class="row g-3"><div class="col-lg-8"><label class="form-label">Link do YouTube</label><input class="form-control form-control-lg" name="url" type="url" required placeholder="https://youtube.com/watch?v=..."></div><div class="col-lg-4"><label class="form-label">Nome da produção</label><input class="form-control form-control-lg" name="name" placeholder="Campanha semanal"></div>
    <div class="col-md-4"><label class="form-label">Perfil editorial</label><select class="form-select" name="contentType"><option value="pregacao">Pregação</option><option value="louvor">Louvor</option><option value="podcast">Podcast</option><option value="aula">Aula</option><option value="motivacao">Motivação</option><option value="negocios">Negócios</option><option value="tecnologia">Tecnologia</option></select></div>
    <div class="col-md-4"><label class="form-label">Candidatos</label><input class="form-control" name="candidateCount" type="number" min="1" max="20" value="20"></div><div class="col-md-4"><label class="form-label">Vídeos finais</label><input class="form-control" name="finalVideoCount" type="number" min="1" max="20" value="10"></div>
    <div class="col-md-4"><label class="form-label">Nota social mínima</label><input class="form-control" name="minimumSocialScore" type="number" min="0" max="100" value="75"></div><div class="col-md-4"><label class="form-label">Posts por dia</label><input class="form-control" name="postsPerDay" type="number" min="1" max="12" value="2"></div><div class="col-md-4"><label class="form-label">Horários</label><input class="form-control" name="postingTimes" value="12:00, 19:00"></div>
    <div class="col-12"><div class="factory-switches"><label><input class="form-check-input" name="autoRender" type="checkbox"> Renderizar após aprovação</label><label><input class="form-check-input" name="autoApprove" type="checkbox"> Aprovar automaticamente</label><label><input class="form-check-input" name="autoSchedule" type="checkbox"> Criar agenda</label><label class="danger-switch"><input class="form-check-input" name="autoPublish" type="checkbox"> Autorizar publicação automática</label></div></div></div>
    <div class="d-flex justify-content-end mt-4"><button class="btn btn-gold" type="submit">Iniciar produção</button></div></form>`;
  document.querySelector('#factoryCreate').onsubmit=createFactory;
}

async function createFactory(event){
  event.preventDefault(); const form=new FormData(event.target), button=event.submitter; button.disabled=true;
  const settings={candidateCount:+form.get('candidateCount'),finalVideoCount:+form.get('finalVideoCount'),minimumSocialScore:+form.get('minimumSocialScore'),postsPerDay:+form.get('postsPerDay'),postingTimes:String(form.get('postingTimes')).split(',').map(x=>x.trim()),autoRender:form.has('autoRender'),autoApprove:form.has('autoApprove'),autoSchedule:form.has('autoSchedule'),autoPublish:form.has('autoPublish')};
  try{await api('/api/production',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({url:form.get('url'),name:form.get('name'),contentType:form.get('contentType'),settings})});toast('Produção adicionada à fila');document.querySelector('#factoryForm').innerHTML='';await refreshFactory()}catch(error){toast(error.message);button.disabled=false}
}

async function refreshFactory(){
  const root=document.querySelector('#factoryBatches'); if(!root)return; const batches=await api('/api/production');
  if(!batches.length){root.innerHTML='<div class="empty">Nenhuma linha de produção criada ainda.</div>';return}
  root.innerHTML=batches.map(batch=>`<article class="factory-batch"><div class="d-flex justify-content-between gap-3"><div><span class="eyebrow">${factoryStatusLabel(batch.status)}</span><h3>${escapeHtml(batch.name)}</h3></div><span class="factory-count">${batch.items.length}/${batch.settings.finalVideoCount}</span></div><p>${escapeHtml(batch.stage)}</p><div class="factory-progress"><i style="width:${batch.progress}%"></i></div><div class="factory-metrics"><span><b>${batch.progress}%</b> progresso</span><span><b>${batch.settings.minimumSocialScore}</b> nota mínima</span><span><b>${batch.settings.postsPerDay}</b> posts/dia</span></div>${batch.status==='awaitingApproval'?`<button class="btn btn-gold w-100 mt-3" onclick="approveFactory('${batch.id}')">Aprovar e renderizar ${batch.items.length}</button>`:''}${batch.error?`<p class="text-danger mt-3">${escapeHtml(batch.error)}</p>`:''}<a class="btn btn-link text-warning px-0 mt-2" href="#" onclick="openProject('${batch.projectId}');return false">Abrir projeto vinculado →</a></article>`).join('');
}

async function approveFactory(id){try{const batch=await api(`/api/production/${id}`),clipIds=batch.items.map(item=>item.clipId);await api(`/api/production/${id}/approve`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({clipIds,render:true,schedule:false})});toast('Cortes aprovados e renderizados');await refreshFactory()}catch(error){toast(error.message)}}

const factoryNav=document.createElement('button'); factoryNav.className='btn btn-outline-light'; factoryNav.dataset.action='factory'; factoryNav.textContent='Modo fábrica';
const navTarget=document.querySelector('[data-action="diagnostics"]'); if(navTarget){navTarget.before(factoryNav);factoryNav.onclick=factoryCenter}
