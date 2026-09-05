// Bible Intelligence V1 · referências rastreáveis + dicionário local da igreja.
(function(){
  const STORAGE_KEY='amadoJesus.churchDictionary.v1';
  const books=[
    ['Gênesis',50,['genesis']],['Êxodo',40,['exodo']],['Levítico',27,['levitico']],['Números',36,['numeros']],['Deuteronômio',34,['deuteronomio']],['Josué',24,['josue']],['Juízes',21,['juizes']],['Rute',4,['rute']],
    ['1 Samuel',31,['1 samuel','primeiro samuel']],['2 Samuel',24,['2 samuel','segundo samuel']],['1 Reis',22,['1 reis','primeiro reis']],['2 Reis',25,['2 reis','segundo reis']],['1 Crônicas',29,['1 cronicas','primeiro cronicas']],['2 Crônicas',36,['2 cronicas','segundo cronicas']],
    ['Esdras',10,['esdras']],['Neemias',13,['neemias']],['Ester',10,['ester']],['Jó',42,['jo']],['Salmos',150,['salmos','salmo']],['Provérbios',31,['proverbios']],['Eclesiastes',12,['eclesiastes']],['Cantares',8,['cantares','cantico dos canticos']],
    ['Isaías',66,['isaias']],['Jeremias',52,['jeremias']],['Lamentações',5,['lamentacoes']],['Ezequiel',48,['ezequiel']],['Daniel',12,['daniel']],['Oséias',14,['oseias']],['Joel',3,['joel']],['Amós',9,['amos']],['Obadias',1,['obadias']],['Jonas',4,['jonas']],['Miquéias',7,['miqueias']],['Naum',3,['naum']],['Habacuque',3,['habacuque']],['Sofonias',3,['sofonias']],['Ageu',2,['ageu']],['Zacarias',14,['zacarias']],['Malaquias',4,['malaquias']],
    ['Mateus',28,['mateus']],['Marcos',16,['marcos']],['Lucas',24,['lucas']],['João',21,['joao']],['Atos',28,['atos','atos dos apostolos']],['Romanos',16,['romanos']],['1 Coríntios',16,['1 corintios','primeiro corintios']],['2 Coríntios',13,['2 corintios','segundo corintios']],['Gálatas',6,['galatas']],['Efésios',6,['efesios']],['Filipenses',4,['filipenses']],['Colossenses',4,['colossenses']],['1 Tessalonicenses',5,['1 tessalonicenses','primeiro tessalonicenses']],['2 Tessalonicenses',3,['2 tessalonicenses','segundo tessalonicenses']],['1 Timóteo',6,['1 timoteo','primeiro timoteo']],['2 Timóteo',4,['2 timoteo','segundo timoteo']],['Tito',3,['tito']],['Filemom',1,['filemom']],['Hebreus',13,['hebreus']],['Tiago',5,['tiago']],['1 Pedro',5,['1 pedro','primeiro pedro']],['2 Pedro',3,['2 pedro','segundo pedro']],['1 João',5,['1 joao','primeiro joao']],['2 João',1,['2 joao','segundo joao']],['3 João',1,['3 joao','terceiro joao']],['Judas',1,['judas']],['Apocalipse',22,['apocalipse']]
  ].map(([name,chapters,aliases])=>({name,chapters,aliases}));

  const renderBase=renderProject;
  renderProject=function(project){
    renderBase(project);
    if(project.status!=='ready')return;
    setTimeout(()=>install(project,0),190);
  };

  const selectBase=selectClip;
  selectClip=function(project,id){selectBase(project,id);setTimeout(()=>renderClipCard(project,project.clips.find(c=>c.id===id)),120);};

  function norm(value){return String(value||'').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/[.,;!?()[\]{}]/g,' ').replace(/\s+/g,' ').trim();}
  function escapeRegex(value){return value.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');}
  function dictionary(){try{return JSON.parse(localStorage.getItem(STORAGE_KEY)||'[]').filter(item=>item?.heard&&item?.canonical)}catch{return[]}}
  function saveDictionary(items){localStorage.setItem(STORAGE_KEY,JSON.stringify(items.slice(0,120)));}
  function applyDictionary(text){let value=norm(text);for(const item of dictionary()){const heard=norm(item.heard),canonical=norm(item.canonical);if(heard)value=value.replace(new RegExp(`\\b${escapeRegex(heard)}\\b`,'g'),canonical);}return value;}

  function detect(text){
    const normalized=applyDictionary(text),found=[];
    for(const book of books){
      const aliases=[...book.aliases].sort((a,b)=>b.length-a.length);
      const aliasPattern=aliases.map(alias=>escapeRegex(norm(alias))).join('|');
      const re=new RegExp(`\\b(${aliasPattern})\\b(?:\\s+(?:capitulo|cap\\.?))?\\s+(\\d{1,3})(?:(?:\\s*[:.]\\s*|\\s+(?:versiculo|versiculos|vers\\.?|v\\.?)\\s*)(\\d{1,3})(?:\\s*[-–]\\s*(\\d{1,3}))?)?`,'g');
      let match;
      while((match=re.exec(normalized))){
        const chapter=+match[2],verse=match[3]?+match[3]:null,endVerse=match[4]?+match[4]:null;
        const chapterValid=chapter>=1&&chapter<=book.chapters;
        const versePlausible=verse==null||(verse>=1&&verse<=176&&(!endVerse||endVerse>=verse&&endVerse<=176));
        found.push({book:book.name,chapter,verse,endVerse,reference:`${book.name} ${chapter}${verse?`:${verse}${endVerse?`-${endVerse}`:''}`:''}`,chapterValid,versePlausible,valid:chapterValid&&versePlausible,index:match.index,confidence:chapterValid&&verse?94:chapterValid?84:38});
      }
    }
    return unique(found);
  }

  function unique(items){const seen=new Set();return items.filter(item=>{const key=item.reference.toLowerCase();if(seen.has(key))return false;seen.add(key);return true;});}

  function clipReferences(clip){return detect(clip?.editedTranscript||clip?.transcript||'');}
  function projectReferences(project){
    const refs=[];
    for(const segment of project.transcript||[])for(const ref of detect(segment.text))refs.push({...ref,time:Number(segment.start)||0,text:segment.text});
    const seen=new Set();return refs.filter(ref=>{const key=`${ref.reference}:${Math.round(ref.time/8)}`;if(seen.has(key))return false;seen.add(key);return true;});
  }

  function install(project,attempt=0){
    const view=document.querySelector('#projectView');if(!view)return;
    const insights=view.querySelector('.cc-editor-insights-body');if(!insights){if(attempt<8)setTimeout(()=>install(project,attempt+1),90);return;}
    project._bibleReferences=projectReferences(project);
    let panel=insights.querySelector('.bible-intelligence-panel');if(!panel){panel=document.createElement('section');panel.className='bible-intelligence-panel';const service=insights.querySelector('.service-map-panel');service?.after(panel)||insights.prepend(panel);}
    renderPanel(project,panel);
    project.clips.forEach(clip=>renderClipCard(project,clip));
  }

  function renderPanel(project,panel){
    const refs=project._bibleReferences||[],valid=refs.filter(r=>r.valid).length;
    panel.innerHTML=`<header><div><span class="eyebrow">BIBLE INTELLIGENCE · RASTREÁVEL</span><h3>Referências bíblicas</h3><small>Reconhece livro/capítulo/versículo na fala sem inventar referências ausentes.</small></div><div class="bible-summary"><b>${refs.length}</b><span>detectadas</span></div></header><div class="bible-status"><span>✓ ${valid} com estrutura válida</span><span>ⓘ a V1 valida livro/capítulo e plausibilidade do número do versículo; não afirma conferir a citação textual.</span></div><div class="bible-ref-list">${refs.slice(0,12).map(ref=>`<button type="button" data-bible-seek="${ref.time}"><b>${escapeHtml(ref.reference)}</b><span>${ref.valid?'estrutura válida':'⚠ revisar referência'}</span><small>${time(ref.time)}</small></button>`).join('')||'<p>Nenhuma referência explícita detectada neste projeto.</p>'}</div><details class="church-dictionary"><summary>Dicionário da igreja</summary><p>Ensine nomes, termos e palavras que costumam ser transcritos errado. A correção é usada pela análise bíblica local.</p><div class="church-dictionary-add"><input class="form-control" data-dict-heard placeholder="Como o Whisper escreveu"><span>→</span><input class="form-control" data-dict-canonical placeholder="Forma correta"><button type="button" class="btn btn-gold btn-sm" data-dict-add>Adicionar</button></div><div data-dict-list>${dictionaryRows()}</div></details>`;
    panel.querySelectorAll('[data-bible-seek]').forEach(button=>button.onclick=()=>seek(+button.dataset.bibleSeek));
    panel.querySelector('[data-dict-add]').onclick=()=>addDictionary(panel,project);
    panel.querySelectorAll('[data-dict-remove]').forEach(button=>button.onclick=()=>removeDictionary(+button.dataset.dictRemove,panel,project));
  }

  function dictionaryRows(){const items=dictionary();return items.length?items.map((item,index)=>`<div class="dictionary-row"><span>${escapeHtml(item.heard)}</span><b>→</b><strong>${escapeHtml(item.canonical)}</strong><button type="button" data-dict-remove="${index}">×</button></div>`).join(''):'<small class="dictionary-empty">Nenhum termo personalizado ainda.</small>';}

  function addDictionary(panel,project){
    const heard=panel.querySelector('[data-dict-heard]').value.trim(),canonical=panel.querySelector('[data-dict-canonical]').value.trim();if(!heard||!canonical)return toast('Preencha o termo ouvido e a forma correta');
    const items=dictionary();items.push({heard,canonical});saveDictionary(items);project._bibleReferences=projectReferences(project);renderPanel(project,panel);project.clips.forEach(clip=>renderClipCard(project,clip,true));toast('✓ Dicionário atualizado');
  }
  function removeDictionary(index,panel,project){const items=dictionary();items.splice(index,1);saveDictionary(items);project._bibleReferences=projectReferences(project);renderPanel(project,panel);project.clips.forEach(clip=>renderClipCard(project,clip,true));}

  function renderClipCard(project,clip,force=false){
    if(!clip)return;const card=document.querySelector(`.clip-card[data-clip="${clip.id}"]`);if(!card)return;
    let panel=card.querySelector('.bible-clip-card');if(panel&&!force)return;if(!panel){panel=document.createElement('section');panel.className='bible-clip-card';const target=card.querySelector('.cc-mode-details')||card;target.prepend(panel);}
    const refs=clipReferences(clip);clip._bibleReferences=refs;
    panel.innerHTML=`<header><strong>BIBLE INTELLIGENCE</strong><span>${refs.length?`${refs.length} ref.`:'sem referência explícita'}</span></header>${refs.length?`<div>${refs.map(ref=>`<span class="bible-ref-chip ${ref.valid?'valid':'warning'}"><b>${escapeHtml(ref.reference)}</b><small>${ref.valid?'✓ estrutura válida':'⚠ revisar'}</small></span>`).join('')}</div>`:'<p>Nenhuma referência explícita encontrada neste corte. O Studio não adicionará uma por conta própria.</p>'}`;
  }

  function seek(seconds){if(typeof switchEditorTab==='function')switchEditorTab('source');setTimeout(()=>{const video=document.querySelector('#preview video');if(video){video.currentTime=Math.max(0,seconds);video.play().catch(()=>{});}},120);}

  function injectStyles(){
    if(document.querySelector('#bible-intelligence-styles'))return;const style=document.createElement('style');style.id='bible-intelligence-styles';style.textContent=`.bible-intelligence-panel{padding:15px;border:1px solid rgba(185,140,255,.18);border-radius:16px;background:rgba(22,13,31,.66);margin-bottom:12px}.bible-intelligence-panel>header{display:flex;justify-content:space-between;gap:12px}.bible-intelligence-panel h3{font-size:1rem;margin:.2rem 0}.bible-intelligence-panel header small{font-size:.66rem;color:#9587a4}.bible-summary{display:grid;text-align:right}.bible-summary b{font-size:1.35rem;color:#d2bcf2}.bible-summary span{font-size:.58rem;color:#8e819c}.bible-status{display:grid;gap:3px;margin:9px 0;color:#9ca6b0;font-size:.61rem}.bible-ref-list{display:grid;gap:5px;max-height:190px;overflow:auto}.bible-ref-list>button{display:grid;grid-template-columns:1fr auto auto;gap:7px;text-align:left;border:1px solid rgba(255,255,255,.07);background:rgba(255,255,255,.025);color:#c8c0d3;border-radius:9px;padding:7px 9px;font-size:.64rem}.bible-ref-list span{color:#88ad9f}.bible-ref-list small{color:#766e7f}.church-dictionary{margin-top:10px;border-top:1px solid rgba(255,255,255,.07);padding-top:8px}.church-dictionary summary{cursor:pointer;font-size:.7rem;color:#cab4e6}.church-dictionary p,.dictionary-empty{font-size:.61rem;color:#81778c;margin:7px 0}.church-dictionary-add{display:grid;grid-template-columns:1fr auto 1fr auto;gap:5px;align-items:center}.church-dictionary-add input{font-size:.68rem}.dictionary-row{display:grid;grid-template-columns:1fr auto 1fr auto;gap:5px;align-items:center;padding:5px 0;font-size:.63rem;border-bottom:1px solid rgba(255,255,255,.04)}.dictionary-row button{border:0;background:transparent;color:#9e8181}.bible-clip-card{padding:10px;border:1px solid rgba(184,139,236,.14);border-radius:11px;background:rgba(27,16,39,.45);margin-bottom:10px}.bible-clip-card header{display:flex;justify-content:space-between;font-size:.66rem}.bible-clip-card header span{color:#8f819f}.bible-clip-card>div{display:flex;gap:5px;flex-wrap:wrap;margin-top:7px}.bible-ref-chip{display:grid;border:1px solid rgba(255,255,255,.08);padding:4px 7px;border-radius:8px;font-size:.6rem}.bible-ref-chip.valid{border-color:rgba(88,190,145,.22)}.bible-ref-chip.warning{border-color:rgba(220,165,83,.26)}.bible-ref-chip small{font-size:.52rem;color:#8da99e}.bible-clip-card p{font-size:.62rem;color:#887e91;margin:7px 0 0}@media(max-width:760px){.church-dictionary-add{grid-template-columns:1fr}.bible-ref-list>button{grid-template-columns:1fr auto}}`;document.head.append(style);
  }
  injectStyles();
  window.AmadoJesusBibleIntelligence={detect,clipReferences,projectReferences,dictionary,applyDictionary};
})();