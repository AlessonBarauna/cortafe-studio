// Content Pack V1 · deriva múltiplos formatos do texto real do corte.
(function(){
  const KEY_PREFIX='amadoJesus.contentPack.v1.';
  const renderBase=renderProject;
  renderProject=function(project){renderBase(project);if(project.status==='ready')setTimeout(()=>install(project),390);};
  const selectBase=selectClip;
  selectClip=function(project,id){selectBase(project,id);setTimeout(()=>renderLauncher(project,project.clips.find(c=>c.id===id),true),240);};

  function sentences(text){return String(text||'').replace(/\s+/g,' ').trim().split(/(?<=[.!?])\s+/).map(s=>s.trim()).filter(s=>s.length>20);}
  function short(value,max=120){const text=String(value||'').trim();return text.length<=max?text:`${text.slice(0,max-1).trim()}…`;}
  function refs(clip){return window.AmadoJesusBibleIntelligence?.clipReferences?.(clip)||[];}
  function storageKey(project,clip){return `${KEY_PREFIX}${project.id}.${clip.id}`;}

  function build(project,clip){
    const text=clip.editedTranscript||clip.transcript||'',parts=sentences(text),reference=refs(clip),title=clip.title||'Uma mensagem para guardar';
    const quotes=(parts.length?parts:[text]).slice(0,7).map(item=>short(item,165));
    const scripture=reference.map(item=>item.reference).join(', ');
    const carousel=[`CAPA\n${title}`,...quotes.slice(0,4).map((quote,index)=>`SLIDE ${index+2}\n“${quote}”`),`FINAL\nQual parte mais falou com você?\nSalve e compartilhe esta mensagem.`].join('\n\n---\n\n');
    const devotional=[title,'',quotes[0]?`Trecho da mensagem:\n“${quotes[0]}”`:'',scripture?`Referência mencionada: ${scripture}`:'',quotes[1]?`Continue refletindo:\n“${quotes[1]}”`:'','Aplicação pessoal: qual decisão prática esta mensagem convida você a tomar hoje?','Oração: converse com Deus a partir do que você ouviu nesta mensagem.'].filter(Boolean).join('\n\n');
    const cell=[`TEMA: ${title}`,scripture?`REFERÊNCIA MENCIONADA: ${scripture}`:'',`1. O que mais chamou sua atenção neste trecho?`,`2. Como você explicaria a ideia principal com suas próprias palavras?`,`3. Em que situação da vida esta mensagem se aplica hoje?`,`4. Qual passo prático você pode assumir nesta semana?`,quotes[0]?`TRECHO PARA RELEMBRAR:\n“${quotes[0]}”`:''].filter(Boolean).join('\n\n');
    const whatsapp=[`*${title}*`,quotes[0]?`\n“${quotes[0]}”`:'',scripture?`\n📖 ${scripture}`:'',`\nAssista ao corte e compartilhe com alguém que precisa ouvir esta mensagem.`].join('\n');
    const stories=[`STORY 1\n${short(title,70)}`,quotes[0]?`STORY 2\n“${short(quotes[0],110)}”`:null,quotes[1]?`STORY 3\n“${short(quotes[1],110)}”`:`STORY 3\nVeja a mensagem completa e compartilhe.`].filter(Boolean).join('\n\n---\n\n');
    const post=[title,'',quotes.slice(0,2).join('\n\n'),scripture?`📖 ${scripture}`:'',clip.caption||'',(clip.hashtags||[]).slice(0,7).join(' ')].filter(Boolean).join('\n\n');
    const blog=[`# ${title}`,scripture?`**Referência mencionada:** ${scripture}`:'',...quotes.slice(0,5).map(q=>q),`## Para refletir\nQue implicações essa mensagem traz para sua vida hoje?`,`> Conteúdo derivado da transcrição original do corte ${clip.id}.`].filter(Boolean).join('\n\n');
    const chapters=project._serviceMap?.map(section=>`${time(section.start)} ${serviceLabel(section.category)}`).join('\n')||'O Service Map precisa estar disponível para gerar capítulos do culto.';
    return {carousel,devotional,cell,whatsapp,stories,post,blog,chapters};
  }

  function serviceLabel(category){return ({pregacao:'Pregação',louvor:'Louvor',avisos:'Avisos',oferta:'Oferta',oracao:'Oração',testemunho:'Testemunho',recepcao:'Recepção'})[category]||category;}
  function loadSaved(project,clip){try{return JSON.parse(localStorage.getItem(storageKey(project,clip))||'null')}catch{return null}}
  function save(project,clip,data){localStorage.setItem(storageKey(project,clip),JSON.stringify(data));}

  function install(project){injectStyles();project.clips.forEach(clip=>renderLauncher(project,clip,false));}
  function renderLauncher(project,clip,force=false){
    if(!clip)return;const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(!card)return;let panel=card.querySelector('.content-pack-launcher');if(panel&&!force)return;if(!panel){panel=document.createElement('section');panel.className='content-pack-launcher';const target=card.querySelector('.cc-mode-publish')||card.querySelector('.cc-mode-details')||card;target.prepend(panel);}
    const saved=!!loadSaved(project,clip);panel.innerHTML=`<div><strong>CONTENT PACK</strong><small>Reel → carrossel → devocional → célula → Stories → WhatsApp</small></div><button type="button" class="btn btn-gold btn-sm" data-content-pack>${saved?'Editar pacote':'Gerar pacote'}</button>`;panel.querySelector('[data-content-pack]').onclick=()=>open(project,clip);
  }

  function open(project,clip){
    document.querySelector('.content-pack-modal')?.remove();const base=build(project,clip),data={...base,...(loadSaved(project,clip)||{})};
    const modal=document.createElement('div');modal.className='content-pack-modal';modal.innerHTML=`<section><header><div><span class="eyebrow">CONTENT PACK · MODO FIEL</span><h2>${escapeHtml(clip.title||'Conteúdo do corte')}</h2><small>Todo conteúdo nasce da transcrição real. Edite à vontade antes de usar.</small></div><button type="button" data-pack-close>×</button></header><div class="pack-tabs">${tab('carousel','Carrossel',true)}${tab('devotional','Devocional')}${tab('cell','Célula')}${tab('whatsapp','WhatsApp')}${tab('stories','Stories')}${tab('post','Post')}${tab('blog','Blog')}${tab('chapters','Capítulos')}</div><div class="pack-editor"><textarea class="form-control" data-pack-text></textarea><div class="pack-note">O Studio não transforma este texto em “fala do pregador”. Frases entre aspas vêm do transcript do corte.</div></div><footer><button type="button" class="btn btn-outline-light" data-pack-reset>Restaurar derivação</button><div><button type="button" class="btn btn-outline-light" data-pack-copy>Copiar</button><button type="button" class="btn btn-gold" data-pack-save>Salvar conteúdo</button></div></footer></section>`;document.body.append(modal);
    let active='carousel';const textarea=modal.querySelector('[data-pack-text]');textarea.value=data[active];
    modal.querySelector('[data-pack-close]').onclick=()=>modal.remove();modal.addEventListener('click',event=>{if(event.target===modal)modal.remove()});
    modal.querySelectorAll('[data-pack-tab]').forEach(button=>button.onclick=()=>{data[active]=textarea.value;active=button.dataset.packTab;modal.querySelectorAll('[data-pack-tab]').forEach(item=>item.classList.toggle('active',item===button));textarea.value=data[active]||'';});
    modal.querySelector('[data-pack-save]').onclick=()=>{data[active]=textarea.value;save(project,clip,data);renderLauncher(project,clip,true);toast('✓ Content Pack salvo neste navegador');};
    modal.querySelector('[data-pack-copy]').onclick=async()=>{try{await navigator.clipboard.writeText(textarea.value);toast('Conteúdo copiado')}catch{textarea.select();document.execCommand('copy');toast('Conteúdo copiado')}};
    modal.querySelector('[data-pack-reset]').onclick=()=>{const fresh=build(project,clip);data[active]=fresh[active];textarea.value=fresh[active];};
  }

  function tab(key,label,active=false){return `<button type="button" class="${active?'active':''}" data-pack-tab="${key}">${label}</button>`;}

  function injectStyles(){
    if(document.querySelector('#content-pack-styles'))return;const style=document.createElement('style');style.id='content-pack-styles';style.textContent=`.content-pack-launcher{display:flex;justify-content:space-between;gap:8px;align-items:center;padding:10px;border:1px solid rgba(199,163,90,.18);border-radius:11px;background:rgba(31,23,12,.42);margin-bottom:10px}.content-pack-launcher>div{display:grid}.content-pack-launcher strong{font-size:.67rem}.content-pack-launcher small{font-size:.55rem;color:#8d8069}.content-pack-modal{position:fixed;inset:0;z-index:9999;background:rgba(0,0,0,.78);backdrop-filter:blur(6px);display:grid;place-items:center;padding:18px}.content-pack-modal>section{width:min(980px,96vw);height:min(780px,92vh);background:#0e0d11;border:1px solid rgba(199,163,90,.24);border-radius:18px;display:grid;grid-template-rows:auto auto minmax(0,1fr) auto;overflow:hidden}.content-pack-modal header{display:flex;justify-content:space-between;padding:17px 19px;border-bottom:1px solid rgba(255,255,255,.07)}.content-pack-modal h2{font-size:1.2rem;margin:.25rem 0}.content-pack-modal header small{font-size:.65rem;color:#8d929a}.content-pack-modal header>button{border:0;background:transparent;color:#aaa;font-size:1.5rem}.pack-tabs{display:flex;gap:4px;overflow:auto;padding:9px 12px;border-bottom:1px solid rgba(255,255,255,.06)}.pack-tabs button{border:1px solid rgba(255,255,255,.07);background:transparent;color:#8e959e;border-radius:999px;padding:5px 9px;font-size:.62rem;white-space:nowrap}.pack-tabs button.active{border-color:rgba(199,163,90,.45);color:#e3c987;background:rgba(199,163,90,.08)}.pack-editor{padding:12px;display:grid;grid-template-rows:minmax(0,1fr) auto;gap:7px;min-height:0}.pack-editor textarea{resize:none;height:100%;font-size:.78rem;line-height:1.55;background:#111116}.pack-note{font-size:.58rem;color:#727881}.content-pack-modal footer{display:flex;justify-content:space-between;gap:8px;padding:11px 13px;border-top:1px solid rgba(255,255,255,.07)}.content-pack-modal footer>div{display:flex;gap:6px}@media(max-width:700px){.content-pack-modal{padding:5px}.content-pack-modal>section{width:100%;height:96vh}.content-pack-modal footer{flex-direction:column}.content-pack-modal footer>div{display:grid;grid-template-columns:1fr 1fr}}`;document.head.append(style);
  }
  injectStyles();window.AmadoJesusContentPack={build,open:()=>{const clip=current?.clips?.find(item=>item.id===(document.querySelector('#ccClipPicker')?.value));if(clip)open(current,clip)}};
})();