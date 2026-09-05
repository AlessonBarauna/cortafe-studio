// Publication Safety Gate V1 · revisão humana antes de publicar conteúdo sensível ou inconsistente.
(function(){
  const privacyPatterns=[
    {label:'possível telefone',re:/\b(?:\+?55\s*)?(?:\(?\d{2}\)?\s*)?9?\d{4}[-\s]?\d{4}\b/},
    {label:'possível e-mail',re:/\b[\w.+-]+@[\w.-]+\.[a-z]{2,}\b/i},
    {label:'possível CPF',re:/\b\d{3}[.\s]?\d{3}[.\s]?\d{3}[-\s]?\d{2}\b/},
    {label:'possível chave PIX/dado financeiro',re:/\b(chave pix|meu pix|pix é|pix e|cpf|cnpj)\b/i},
    {label:'pedido de oração potencialmente privado',re:/\b(pedido de ora[cç][aã]o|ore pela minha|n[aã]o compartilhe|em segredo|confidencial)\b/i}
  ];

  const renderBase=renderProject;
  renderProject=function(project){renderBase(project);if(project.status==='ready')setTimeout(()=>install(project),340);};
  const selectBase=selectClip;
  selectClip=function(project,id){selectBase(project,id);setTimeout(()=>renderClip(project,project.clips.find(c=>c.id===id),true),210);};

  function analyze(project,clip){
    const blockers=[],warnings=[],passes=[];const text=String(clip.editedTranscript||clip.transcript||'');
    if(!clip.videoPath)blockers.push('O vídeo final ainda não foi renderizado.');else passes.push('Vídeo final disponível');
    if(clip.renderOutdated)blockers.push('O vídeo renderizado está desatualizado em relação às edições atuais.');else if(clip.videoPath)passes.push('Render corresponde ao estado salvo');
    const quality=clip.qualityReport;if(quality?.status==='blocked')blockers.push('Quality Gate técnico bloqueou este arquivo.');else if(quality?.status==='warning')warnings.push('Quality Gate encontrou alertas técnicos.');else if(quality?.status==='pass')passes.push('Quality Gate técnico aprovado');
    const faithful=clip._faithful||window.AmadoJesusFaithfulAi?.analyze?.(project,clip);
    if(project.options?.contentType==='pregacao'&&faithful?.status==='hold')blockers.push(`Faithful AI marcou risco contextual alto (${faithful.faithful}/100).`);
    else if(faithful?.status==='review')warnings.push(`Faithful AI recomenda revisão de contexto (${faithful.faithful}/100).`);
    else if(faithful?.status==='strong')passes.push(`Fidelidade contextual ${faithful.faithful}/100`);
    const refs=window.AmadoJesusBibleIntelligence?.clipReferences?.(clip)||[];const invalid=refs.filter(ref=>!ref.valid);if(invalid.length)warnings.push(`${invalid.length} referência(s) bíblica(s) precisam de revisão.`);else if(refs.length)passes.push(`${refs.length} referência(s) com estrutura válida`);
    const service=clip._serviceCategory||window.AmadoJesusServiceMap?.categoryForClip?.(clip);
    if(['avisos','oferta','oracao','recepcao'].includes(service))warnings.push(`Service Map classificou este trecho como ${service}, não como conteúdo editorial principal.`);
    if(service==='louvor'||project.options?.contentType==='louvor')warnings.push('Conteúdo musical: confirme os direitos da música e da gravação antes de publicar.');
    for(const pattern of privacyPatterns)if(pattern.re.test(text))warnings.push(pattern.label+'.');
    const subtitles=clip.subtitleTrack;if(subtitles?.enabled&&subtitles.blocks?.some(block=>String(block.text||'').trim()))passes.push('Legendas salvas');else warnings.push('O corte não possui legenda salva/ativada.');
    const status=blockers.length?'blocked':warnings.length?'review':'pass';return {status,blockers:[...new Set(blockers)],warnings:[...new Set(warnings)],passes:[...new Set(passes)]};
  }

  function install(project){project.clips.forEach(clip=>renderClip(project,clip,false));}
  function renderClip(project,clip,force=false){
    if(!clip)return;const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(!card)return;let panel=card.querySelector('.publication-safety-card');if(panel&&!force)return;if(!panel){panel=document.createElement('section');panel.className='publication-safety-card';const target=card.querySelector('.cc-mode-publish')||card.querySelector('.cc-mode-details')||card;target.prepend(panel);}const result=analyze(project,clip);clip._publicationSafety=result;
    const title={pass:'✓ PRONTO PARA REVISÃO FINAL',review:'⚠ REVISÃO HUMANA NECESSÁRIA',blocked:'⛔ PUBLICAÇÃO BLOQUEADA'}[result.status];
    panel.dataset.status=result.status;panel.innerHTML=`<header><strong>PUBLICATION SAFETY GATE</strong><span>${title}</span></header><div class="safety-checks">${result.blockers.map(item=>`<p class="blocked">⛔ ${escapeHtml(item)}</p>`).join('')}${result.warnings.map(item=>`<p class="warning">⚠ ${escapeHtml(item)}</p>`).join('')}${result.passes.slice(0,5).map(item=>`<p class="pass">✓ ${escapeHtml(item)}</p>`).join('')}</div><small>Este gate não decide questões pastorais ou jurídicas; ele sinaliza riscos detectáveis para revisão humana.</small>`;
  }

  if(typeof publishClip==='function'){
    const publishBase=publishClip;
    publishClip=async function(projectId,clipId,platform){
      const project=current,clip=project?.clips?.find(item=>item.id===clipId);if(!project||!clip)return publishBase(projectId,clipId,platform);
      const result=analyze(project,clip);clip._publicationSafety=result;renderClip(project,clip,true);
      if(result.status==='blocked'){toast(`Publicação bloqueada: ${result.blockers[0]}`);return;}
      if(result.status==='review'){
        const message=`Revisão necessária antes de publicar:\n\n${result.warnings.map(item=>`• ${item}`).join('\n')}\n\nVocê revisou estes pontos e deseja continuar?`;
        if(!confirm(message))return toast('Publicação cancelada para revisão');
      }
      return publishBase(projectId,clipId,platform);
    };
  }

  function injectStyles(){
    if(document.querySelector('#publication-safety-styles'))return;const style=document.createElement('style');style.id='publication-safety-styles';style.textContent=`.publication-safety-card{padding:10px;border:1px solid rgba(90,190,145,.15);border-radius:11px;background:rgba(8,23,18,.42);margin-bottom:10px}.publication-safety-card[data-status="review"]{border-color:rgba(225,169,72,.22);background:rgba(33,25,11,.4)}.publication-safety-card[data-status="blocked"]{border-color:rgba(218,82,82,.24);background:rgba(35,11,11,.42)}.publication-safety-card>header{display:grid;gap:3px}.publication-safety-card header strong{font-size:.66rem}.publication-safety-card header span{font-size:.57rem;color:#9bc7b4}.safety-checks{display:grid;gap:3px;margin-top:7px}.safety-checks p{margin:0;font-size:.59rem}.safety-checks .pass{color:#8dbba6}.safety-checks .warning{color:#d2ad72}.safety-checks .blocked{color:#d18a8a}.publication-safety-card>small{display:block;margin-top:7px;color:#6f7c76;font-size:.52rem}`;document.head.append(style);
  }
  injectStyles();window.AmadoJesusSafetyGate={analyze};
})();