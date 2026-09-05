// Sermon Memory V1 · busca local em todo o acervo + criação de corte a partir da resposta.
(function(){
  const synonyms={
    ansiedade:['ansiedade','ansioso','ansiosa','preocupacao','preocupação','aflicao','aflição','medo','inquietacao','inquietação'],
    familia:['familia','família','casamento','marido','esposa','filhos','pais','lar'],
    fe:['fe','fé','crer','creia','confiar','confie','crenca','crença'],
    proposito:['proposito','propósito','chamado','missao','missão','vocacao','vocação'],
    perdao:['perdao','perdão','perdoar','perdoe','reconciliacao','reconciliação'],
    graca:['graca','graça','favor','misericordia','misericórdia'],
    oracao:['oracao','oração','orar','ore','intercessao','intercessão'],
    espirito:['espirito santo','espírito santo','consolador','espirito','espírito'],
    cura:['cura','curado','curada','restauracao','restauração','restaurado','restaurada'],
    lideranca:['lideranca','liderança','lider','líder','discipulado','servir','servico','serviço'],
    jovens:['jovem','jovens','juventude','adolescente','adolescentes'],
    salvacao:['salvacao','salvação','salvo','salva','evangelho','arrependimento','novo nascimento']
  };
  let cache=null,cacheStamp='';

  const homeBase=home;
  home=async function(){await homeBase();setTimeout(()=>installHome(),40);};

  function norm(value){return String(value||'').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/[^a-z0-9\s:]/g,' ').replace(/\s+/g,' ').trim();}
  function tokens(value){return norm(value).split(' ').filter(token=>token.length>=3);}
  function expandQuery(query){
    const base=tokens(query),expanded=new Set(base);
    const normalized=norm(query);
    for(const [concept,terms] of Object.entries(synonyms))if(normalized.includes(norm(concept))||terms.some(term=>normalized.includes(norm(term))))for(const term of terms)for(const token of tokens(term))expanded.add(token);
    return [...expanded];
  }

  async function buildIndex(force=false){
    const projects=await api('/api/projects');
    const stamp=projects.map(p=>`${p.id}:${p.updatedAt||''}:${p.transcript?.length||0}`).join('|');
    if(!force&&cache&&cacheStamp===stamp)return cache;
    const chunks=[];
    for(const project of projects.filter(p=>p.status==='ready'&&(p.transcript||[]).length)){
      const segments=project.transcript||[];let group=[],start=null,end=null;
      const flush=()=>{
        if(!group.length)return;const text=group.map(s=>s.text).join(' ').trim();if(text)chunks.push({projectId:project.id,projectName:project.name,contentType:project.options?.contentType||'pregacao',duration:Number(project.duration)||0,start:Number(start)||0,end:Number(end)||0,text,normalized:norm(text),createdAt:project.createdAt});group=[];start=null;end=null;
      };
      for(const segment of segments){
        if(start==null)start=Number(segment.start)||0;end=Number(segment.end)||start;group.push(segment);
        if(end-start>=42||group.length>=6)flush();
      }
      flush();
    }
    cache=chunks;cacheStamp=stamp;return chunks;
  }

  function searchIndex(index,query,limit=18){
    const expanded=expandQuery(query),raw=tokens(query),phrase=norm(query);if(!expanded.length)return[];
    return index.map(chunk=>{
      let score=0,hits=0;
      for(const token of expanded){const occurrences=countToken(chunk.normalized,token);if(occurrences){score+=Math.min(4,occurrences)*(raw.includes(token)?7:3);hits++;}}
      if(phrase.length>5&&chunk.normalized.includes(phrase))score+=28;
      const coverage=hits/Math.max(1,expanded.length);score+=coverage*25;
      if(chunk.contentType==='pregacao')score+=3;
      const refs=window.AmadoJesusBibleIntelligence?.detect?.(chunk.text)||[];if(refs.length)score+=2;
      return {...chunk,score:+score.toFixed(2),coverage,refs};
    }).filter(item=>item.score>=6).sort((a,b)=>b.score-a.score||b.coverage-a.coverage).slice(0,limit);
  }

  function countToken(text,token){const re=new RegExp(`\\b${escapeRegex(token)}\\b`,'g');return (text.match(re)||[]).length;}
  function escapeRegex(value){return value.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');}

  function installHome(){
    const projects=document.querySelector('#projects');if(!projects||document.querySelector('.sermon-memory-home'))return;injectStyles();
    const panel=document.createElement('section');panel.className='sermon-memory-home';panel.innerHTML=`<div class="memory-title"><div><span class="eyebrow">SERMON MEMORY · ASK AMADO JESUS</span><h2>Pesquise tudo que já foi pregado.</h2><p>Digite um tema, pergunta ou referência. O Studio procura no acervo local e leva ao segundo exato.</p></div><span class="memory-local">100% local</span></div><form class="memory-search"><input class="form-control" data-memory-query placeholder="Ex.: o que já pregamos sobre ansiedade?" autocomplete="off"><button class="btn btn-gold" type="submit">Buscar no acervo</button></form><div class="memory-suggestions"><button type="button">ansiedade</button><button type="button">família</button><button type="button">fé</button><button type="button">propósito</button><button type="button">perdão</button></div><div class="memory-state" data-memory-state>O índice é criado sob demanda usando as transcrições já existentes.</div><div class="memory-results" data-memory-results></div>`;
    projects.parentElement.insertBefore(panel,projects.previousElementSibling);
    const form=panel.querySelector('form');form.onsubmit=event=>{event.preventDefault();runSearch(panel,form.querySelector('[data-memory-query]').value)};
    panel.querySelectorAll('.memory-suggestions button').forEach(button=>button.onclick=()=>{form.querySelector('[data-memory-query]').value=button.textContent;runSearch(panel,button.textContent)});
  }

  async function runSearch(panel,query){
    query=query.trim();if(!query)return toast('Digite o que você quer encontrar no acervo');const state=panel.querySelector('[data-memory-state]'),host=panel.querySelector('[data-memory-results]');state.textContent='Indexando transcrições locais…';host.innerHTML='';
    try{
      const index=await buildIndex(),results=searchIndex(index,query);state.textContent=`${index.length} trechos indexados · ${results.length} resultados relevantes para “${query}”.`;
      host.innerHTML=results.length?results.map((result,index)=>resultHtml(result,index)).join(''):'<div class="memory-empty">Não encontrei um trecho suficientemente relacionado. Tente outra expressão ou termo.</div>';
      host.querySelectorAll('[data-memory-open]').forEach(button=>button.onclick=()=>openResult(button.dataset.project,+button.dataset.start));
      host.querySelectorAll('[data-memory-create]').forEach(button=>button.onclick=()=>createClip(button,button.dataset.project,+button.dataset.start,+button.dataset.end,+button.dataset.duration));
    }catch(error){state.textContent='Falha ao pesquisar o acervo.';toast(error.message);}
  }

  function resultHtml(result,index){
    const score=Math.min(99,Math.round(48+result.coverage*36+Math.min(15,result.score/4))),refs=result.refs?.slice(0,2)||[];
    return `<article class="memory-result"><header><span>#${String(index+1).padStart(2,'0')}</span><div><strong>${escapeHtml(result.projectName)}</strong><small>${time(result.start)} · ${escapeHtml(result.contentType)}</small></div><b>${score}%</b></header><p>${escapeHtml(snippet(result.text))}</p>${refs.length?`<div class="memory-refs">${refs.map(ref=>`<span>${escapeHtml(ref.reference)}</span>`).join('')}</div>`:''}<footer><button type="button" data-memory-open data-project="${result.projectId}" data-start="${result.start}">▶ Ver contexto</button><button type="button" class="primary" data-memory-create data-project="${result.projectId}" data-start="${result.start}" data-end="${result.end}" data-duration="${result.duration}">+ Criar corte daqui</button></footer></article>`;
  }

  function snippet(text){const clean=String(text||'').replace(/\s+/g,' ').trim();return clean.length<=330?clean:`${clean.slice(0,327)}…`;}

  async function openResult(projectId,start){
    try{await openProject(projectId);if(typeof switchEditorTab==='function')switchEditorTab('source');setTimeout(()=>{const video=document.querySelector('#preview video');if(video){video.currentTime=Math.max(0,start);video.play().catch(()=>{});}},180);}catch(error){toast(error.message);}
  }

  async function createClip(button,projectId,hitStart,hitEnd,duration){
    const old=button.textContent;button.disabled=true;button.textContent='Criando…';
    try{
      const desired=65,center=(hitStart+hitEnd)/2;let start=Math.max(0,center-desired*.42),end=Math.min(duration||center+desired, start+desired);if(end-start<desired&&duration>=desired)start=Math.max(0,end-desired);start=Math.floor(start*10)/10;end=Math.ceil(end*10)/10;
      const clip=await api(`/api/projects/${projectId}/clips/manual`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({start,end})});
      await openProject(projectId);selectClip(current,clip.id);toast('✓ Corte criado a partir da memória do sermão');
    }catch(error){toast(error.message);button.disabled=false;button.textContent=old;}
  }

  function injectStyles(){
    if(document.querySelector('#sermon-memory-styles'))return;const style=document.createElement('style');style.id='sermon-memory-styles';style.textContent=`.sermon-memory-home{margin:0 0 30px;padding:22px;border:1px solid rgba(199,163,90,.2);border-radius:20px;background:linear-gradient(135deg,rgba(24,18,11,.82),rgba(12,15,20,.88))}.memory-title{display:flex;justify-content:space-between;gap:18px}.memory-title h2{font-size:1.45rem;margin:.3rem 0}.memory-title p{color:#9098a3;margin:0;font-size:.82rem}.memory-local{align-self:flex-start;border:1px solid rgba(115,205,165,.2);border-radius:999px;padding:5px 9px;color:#8bc9ae;font-size:.64rem}.memory-search{display:grid;grid-template-columns:1fr auto;gap:8px;margin-top:16px}.memory-search input{font-size:.86rem}.memory-suggestions{display:flex;gap:5px;flex-wrap:wrap;margin-top:8px}.memory-suggestions button{border:1px solid rgba(255,255,255,.07);background:transparent;color:#8d97a2;border-radius:999px;padding:4px 8px;font-size:.62rem}.memory-state{margin-top:10px;font-size:.65rem;color:#77828e}.memory-results{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:9px;margin-top:12px}.memory-result{padding:12px;border:1px solid rgba(255,255,255,.07);border-radius:13px;background:rgba(0,0,0,.17)}.memory-result header{display:grid;grid-template-columns:auto 1fr auto;gap:8px;align-items:center}.memory-result header>span{font-size:.6rem;color:#78694c}.memory-result header>div{display:grid}.memory-result header strong{font-size:.76rem}.memory-result header small{font-size:.58rem;color:#77818d}.memory-result header>b{font-size:.74rem;color:#c7a35a}.memory-result p{font-size:.69rem;line-height:1.45;color:#aab0b8;margin:9px 0}.memory-refs{display:flex;gap:4px;flex-wrap:wrap}.memory-refs span{font-size:.57rem;border:1px solid rgba(183,139,232,.17);color:#b9a2ce;border-radius:999px;padding:3px 6px}.memory-result footer{display:flex;gap:6px;margin-top:9px}.memory-result footer button{border:1px solid rgba(255,255,255,.08);background:transparent;color:#aab2bc;border-radius:7px;padding:5px 8px;font-size:.6rem}.memory-result footer button.primary{border-color:rgba(199,163,90,.35);color:#e0c483;background:rgba(199,163,90,.06)}.memory-empty{grid-column:1/-1;padding:18px;text-align:center;color:#7d8792;font-size:.72rem}@media(max-width:850px){.memory-results{grid-template-columns:1fr}.memory-title{flex-direction:column}.memory-search{grid-template-columns:1fr}}`;document.head.append(style);
  }
  injectStyles();
  window.AmadoJesusSermonMemory={buildIndex,search:async query=>searchIndex(await buildIndex(),query),refresh:()=>buildIndex(true)};
})();