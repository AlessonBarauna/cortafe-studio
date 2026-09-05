// Service Map V1 · entende a estrutura do culto sem modelo pesado adicional.
(function(){
  const WINDOW_SECONDS=24;
  const categories={
    pregacao:{label:'Pregação',icon:'✝',priority:1},
    louvor:{label:'Louvor',icon:'♫',priority:7},
    avisos:{label:'Avisos',icon:'⌁',priority:6},
    oferta:{label:'Oferta',icon:'◇',priority:8},
    oracao:{label:'Oração',icon:'🙏',priority:5},
    testemunho:{label:'Testemunho',icon:'◉',priority:4},
    recepcao:{label:'Recepção',icon:'⌂',priority:3}
  };
  const signals={
    louvor:['[música]','[musica]','vamos adorar','vamos louvar','te adoramos','te exaltamos','aleluia','santo santo','glória a deus','gloria a deus','cantar','canção','cancao','banda','ministério de louvor','ministerio de louvor'],
    avisos:['aviso','avisos','próximo domingo','proximo domingo','inscrição','inscricao','evento','conferência','conferencia','agenda','qr code','link','site','acampamento','encontro','culto de','quarta-feira','sábado','sabado'],
    oferta:['oferta','dízimo','dizimo','contribuição','contribuicao','contribuir','pix','chave pix','generosidade','semeadura','ofertar','ofertório','ofertorio'],
    oracao:['vamos orar','oremos','vamos falar com deus','pai amado','senhor deus','querido deus','em nome de jesus','feche os olhos','curve sua cabeça','curve a cabeça','amém','amen'],
    testemunho:['testemunho','quero contar','aconteceu comigo','na minha vida','deus fez','eu estava','eu passei','me aconteceu','minha história','minha historia','fui curado','fui curada'],
    recepcao:['seja bem-vindo','sejam bem-vindos','bem-vindo','bem-vindos','bom dia igreja','boa noite igreja','que alegria receber','primeira vez','visitando','visitante','receber você','receber voces'],
    pregacao:['abra sua bíblia','abra sua biblia','palavra de deus','texto diz','versículo','versiculo','capítulo','capitulo','evangelho','escritura','vamos aprender','mensagem de hoje','quero ensinar','a bíblia','a biblia']
  };

  const renderBase=renderProject;
  renderProject=function(project){
    renderBase(project);
    if(project.status!=='ready')return;
    requestAnimationFrame(()=>requestAnimationFrame(()=>install(project)));
  };

  function normalize(value){return String(value||'').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/\s+/g,' ').trim();}
  function contains(text,term){return text.includes(normalize(term));}

  function analyze(project){
    const transcript=project.transcript||[];
    const duration=Math.max(1,Number(project.duration)||Math.max(1,...transcript.map(s=>Number(s.end)||0)));
    const windows=[];
    for(let start=0;start<duration;start+=WINDOW_SECONDS){
      const end=Math.min(duration,start+WINDOW_SECONDS);
      const parts=transcript.filter(s=>Number(s.end)>start&&Number(s.start)<end);
      const text=normalize(parts.map(s=>s.text).join(' '));
      windows.push({start,end,text,category:classify(text,start,duration)});
    }
    return smooth(merge(windows),duration);
  }

  function classify(text,start,duration){
    if(!text)return 'pregacao';
    const scores={pregacao:1,louvor:0,avisos:0,oferta:0,oracao:0,testemunho:0,recepcao:0};
    for(const [category,terms] of Object.entries(signals)){
      for(const term of terms)if(contains(text,term))scores[category]+=term.length>14?3:2;
    }
    if(start<Math.min(360,duration*.09))scores.recepcao+=1.2;
    if(text.includes('[musica]'))scores.louvor+=5;
    if(/\b(joao|mateus|marcos|lucas|romanos|salmos?|proverbios|corintios|efesios|filipenses|hebreus|genesis)\b/.test(text)&&/\b\d{1,3}\b/.test(text))scores.pregacao+=4;
    if(text.length>420)scores.pregacao+=1;
    return Object.entries(scores).sort((a,b)=>b[1]-a[1]||(categories[b[0]].priority-categories[a[0]].priority))[0][0];
  }

  function merge(windows){
    const result=[];
    for(const item of windows){
      const previous=result.at(-1);
      if(previous&&previous.category===item.category){previous.end=item.end;previous.text=`${previous.text} ${item.text}`.trim();}
      else result.push({...item});
    }
    return result;
  }

  function smooth(source,duration){
    let items=source.map(item=>({...item}));
    for(let i=1;i<items.length-1;i++){
      const current=items[i],previous=items[i-1],next=items[i+1];
      if(current.end-current.start<=WINDOW_SECONDS*1.1&&previous.category===next.category&&current.category!==previous.category)current.category=previous.category;
    }
    items=merge(items);
    return items.map(item=>({...item,start:+Math.max(0,item.start).toFixed(2),end:+Math.min(duration,item.end).toFixed(2)}));
  }

  function clipCategory(clip,map){
    let best='pregacao',bestOverlap=-1;
    for(const section of map){
      const overlap=Math.max(0,Math.min(Number(clip.end),section.end)-Math.max(Number(clip.start),section.start));
      if(overlap>bestOverlap){bestOverlap=overlap;best=section.category;}
    }
    return best;
  }

  function install(project){
    const view=document.querySelector('#projectView');if(!view)return;
    const map=analyze(project);project._serviceMap=map;
    annotateClips(project,map);
    let panel=view.querySelector('.service-map-panel');
    if(!panel){panel=document.createElement('section');panel.className='service-map-panel';const insights=view.querySelector('.cc-editor-insights-body');const target=insights||view.querySelector('.cc-workspace-v2')||view;target.prepend(panel);}
    panel.innerHTML=panelHtml(project,map);
    bindPanel(project,panel,map);
    applyFilter(project,panel.dataset.filter||'all');
  }

  function panelHtml(project,map){
    const duration=Math.max(1,Number(project.duration)||1);
    const present=[...new Set(map.map(item=>item.category))];
    const totals=Object.fromEntries(present.map(cat=>[cat,map.filter(i=>i.category===cat).reduce((sum,i)=>sum+i.end-i.start,0)]));
    return `<header class="service-map-head"><div><span class="eyebrow">SERVICE MAP · IA LOCAL</span><h3>Estrutura do culto</h3><small>Separa pregação, louvor, avisos, oferta, oração e testemunhos antes da seleção editorial.</small></div><button type="button" class="btn btn-outline-light btn-sm" data-service-refresh>Reanalisar</button></header><div class="service-map-track">${map.map(section=>`<button type="button" class="service-map-section cat-${section.category}" data-service-seek="${section.start}" style="left:${section.start/duration*100}%;width:${Math.max(.5,(section.end-section.start)/duration*100)}%" title="${categories[section.category].label} · ${time(section.start)}–${time(section.end)}"><span>${categories[section.category].icon}</span></button>`).join('')}</div><div class="service-map-legend">${present.map(cat=>`<button type="button" data-service-filter="${cat}"><span>${categories[cat].icon}</span><b>${categories[cat].label}</b><small>${time(totals[cat])}</small></button>`).join('')}<button type="button" class="active" data-service-filter="all"><b>Todos</b><small>${project.clips.length} cortes</small></button></div><div class="service-map-state" data-service-state>Filtro atual: todos os momentos.</div>`;
  }

  function annotateClips(project,map){
    for(const clip of project.clips){
      const category=clipCategory(clip,map);clip._serviceCategory=category;
      const info=categories[category];
      document.querySelectorAll(`.clip-card[data-clip="${clip.id}"],.cc-asset[data-cc-clip="${clip.id}"]`).forEach(element=>{
        element.dataset.serviceCategory=category;
        let badge=element.querySelector('.service-category-badge');
        if(!badge){badge=document.createElement('small');badge.className='service-category-badge';const host=element.classList.contains('cc-asset')?element.querySelector('span:nth-child(2)'):element.querySelector('.clip-score,header,.card-body')||element;host?.append(badge);}
        if(badge)badge.textContent=`${info.icon} ${info.label}`;
      });
    }
  }

  function bindPanel(project,panel,map){
    panel.querySelectorAll('[data-service-seek]').forEach(button=>button.onclick=()=>seekSource(project,+button.dataset.serviceSeek));
    panel.querySelectorAll('[data-service-filter]').forEach(button=>button.onclick=()=>{
      panel.querySelectorAll('[data-service-filter]').forEach(item=>item.classList.toggle('active',item===button));
      panel.dataset.filter=button.dataset.serviceFilter;applyFilter(project,button.dataset.serviceFilter);
    });
    panel.querySelector('[data-service-refresh]').onclick=()=>install(project);
  }

  function applyFilter(project,filter){
    const panel=document.querySelector('.service-map-panel');
    let visible=0;
    project.clips.forEach(clip=>{
      const show=filter==='all'||clip._serviceCategory===filter;
      document.querySelectorAll(`.cc-asset[data-cc-clip="${clip.id}"]`).forEach(el=>el.classList.toggle('service-filter-hidden',!show));
      const option=document.querySelector(`#ccClipPicker option[value="${clip.id}"]`);if(option)option.hidden=!show;
      if(show)visible++;
    });
    const current=project.clips.find(c=>c.id===(document.querySelector('#ccClipPicker')?.value));
    if(filter!=='all'&&current&&current._serviceCategory!==filter){const first=project.clips.find(c=>c._serviceCategory===filter);if(first)selectClip(project,first.id);}
    const state=panel?.querySelector('[data-service-state]');if(state)state.textContent=filter==='all'?`Todos os momentos · ${visible} cortes.`:`${categories[filter]?.label||filter} · ${visible} ${visible===1?'corte':'cortes'} encontrados.`;
  }

  function seekSource(project,seconds){
    if(typeof switchEditorTab==='function')switchEditorTab('source');
    setTimeout(()=>{
      const video=document.querySelector('#preview video');if(!video)return;
      video.currentTime=Math.max(0,seconds);video.play().catch(()=>{});
    },120);
  }

  function injectStyles(){
    if(document.querySelector('#service-map-styles'))return;
    const style=document.createElement('style');style.id='service-map-styles';style.textContent=`.service-map-panel{padding:15px;border:1px solid rgba(87,170,222,.18);border-radius:16px;background:rgba(8,16,23,.75);margin-bottom:12px}.service-map-head{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.service-map-head h3{font-size:1rem;margin:.2rem 0}.service-map-head small{font-size:.67rem;color:#81909f}.service-map-track{height:36px;position:relative;margin:13px 0 9px;border-radius:10px;overflow:hidden;background:#10141a}.service-map-section{position:absolute;top:0;bottom:0;border:0;border-right:1px solid rgba(0,0,0,.3);display:grid;place-items:center;color:white;opacity:.84}.service-map-section:hover{opacity:1;filter:brightness(1.2)}.cat-pregacao{background:#715db4}.cat-louvor{background:#9761a7}.cat-avisos{background:#4e7696}.cat-oferta{background:#9b7742}.cat-oracao{background:#477c70}.cat-testemunho{background:#9a5d62}.cat-recepcao{background:#536774}.service-map-legend{display:flex;gap:5px;flex-wrap:wrap}.service-map-legend button{border:1px solid rgba(255,255,255,.08);background:rgba(255,255,255,.025);border-radius:999px;padding:5px 8px;color:#aeb8c2;display:flex;gap:5px;align-items:center;font-size:.62rem}.service-map-legend button.active{border-color:rgba(199,163,90,.55);color:#f0dfbb;background:rgba(199,163,90,.09)}.service-map-legend small{color:#73808c}.service-map-state{font-size:.62rem;color:#72808e;margin-top:8px}.service-category-badge{display:block;color:#8ea0b1!important;font-size:.58rem!important;margin-top:2px}.service-filter-hidden{display:none!important}@media(max-width:760px){.service-map-head{flex-direction:column}.service-map-track{height:42px}}`;document.head.append(style);
  }

  injectStyles();
  window.AmadoJesusServiceMap={analyze,categoryForClip:(clip)=>clipCategory(clip,current?._serviceMap||analyze(current)),filter:(category)=>applyFilter(current,category)};
})();