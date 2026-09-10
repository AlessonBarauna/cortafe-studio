(function () {
  const AUDIO_EXTENSIONS = new Set(['mp3','ogg','wav','aac','m4a','wma','m4r']);
  const state = { playlist: [], localFiles: [], matches: [], musicDir: null, atpDir: null, sourceName: '' };

  const normalize = value => String(value || '')
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/\[[^\]]*\]|\([^)]*\)/g, ' ')
    .replace(/[^a-z0-9]+/g, ' ')
    .replace(/\s+/g, ' ').trim();

  const stop = new Set(['official','oficial','video','clipe','clip','lyrics','lyric','letra','live','vivo','audio','version','versao','music','musica','feat','ft','ao','at','home','culto']);
  const tokens = value => [...new Set(normalize(value).split(' ').filter(x => x.length > 1 && !stop.has(x)))];
  const score = (title, filename) => {
    const left = normalize(title), right = normalize(filename.replace(/\.[^.]+$/, ''));
    if (!left || !right) return 0;
    if (right.includes(left) || left.includes(right)) return .98;
    const a=tokens(left), b=tokens(right); if(!a.length || !b.length) return 0;
    const bs=new Set(b), inter=a.filter(x=>bs.has(x)).length, union=new Set([...a,...b]).size;
    return Math.round((((inter/union)*.45)+((inter/a.length)*.55))*1000)/1000;
  };

  const esc = value => window.escapeHtml ? window.escapeHtml(value) : String(value ?? '').replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const notify = msg => window.toast ? window.toast(msg) : alert(msg);
  const apiSupported = () => 'showDirectoryPicker' in window;
  const statusText = item => item.file ? `${Math.round(item.score * 100)}%` : 'Faltando';

  function injectCss(){ if(document.querySelector('link[data-worship-fm]')) return; const link=document.createElement('link'); link.rel='stylesheet'; link.href='/worship-fm.css?v=20260910.1'; link.dataset.worshipFm='1'; document.head.append(link); }

  function parseCsv(text){
    const lines=text.replace(/^\uFEFF/,'').split(/\r?\n/).filter(Boolean); if(!lines.length) return [];
    const delimiter=(lines[0].match(/;/g)||[]).length >= (lines[0].match(/,/g)||[]).length ? ';' : ',';
    const split=line=>{const out=[];let cur='',q=false;for(let i=0;i<line.length;i++){const c=line[i];if(c==='"'){if(q&&line[i+1]==='"'){cur+='"';i++;}else q=!q;}else if(c===delimiter&&!q){out.push(cur);cur='';}else cur+=c;}out.push(cur);return out.map(x=>x.trim());};
    const headers=split(lines[0]).map(normalize); const rows=[];
    for(let i=1;i<lines.length;i++){const cols=split(lines[i]);const row={};headers.forEach((h,j)=>row[h]=cols[j]||'');rows.push(row);}
    return rows.map((r,i)=>({
      index:Number(r.ordem || r.order || r.index || i+1) || i+1,
      title:r.musica || r.titulo || r.titulo_youtube || r['titulo original no youtube'] || r.title || Object.values(r)[1] || `Faixa ${i+1}`,
      artist:r.artista || r.artistas || r['artista s sugerido s'] || r.canal || r.channel || '',
      url:r.url || r['url de referencia'] || r.url_de_referencia || ''
    })).sort((a,b)=>a.index-b.index);
  }

  async function importPlaylistCsv(){
    const input=document.createElement('input');input.type='file';input.accept='.csv,text/csv';input.onchange=async()=>{const file=input.files?.[0];if(!file)return;state.playlist=parseCsv(await file.text());state.sourceName=file.name;rematch();render();notify(`${state.playlist.length} faixas carregadas do CSV`)};input.click();
  }

  async function selectMusicFolder(){
    if(!apiSupported()) return notify('Use Chrome ou Edge atualizado: o navegador precisa permitir seleção de pasta local.');
    try{state.musicDir=await window.showDirectoryPicker({mode:'read'});state.localFiles=[];for await(const [name,handle] of state.musicDir.entries()){if(handle.kind!=='file')continue;const ext=name.split('.').pop().toLowerCase();if(!AUDIO_EXTENSIONS.has(ext))continue;const file=await handle.getFile();state.localFiles.push({name,size:file.size,handle});}state.localFiles.sort((a,b)=>a.name.localeCompare(b.name,'pt-BR'));rematch();render();notify(`${state.localFiles.length} áudios encontrados`)}catch(e){if(e.name!=='AbortError')notify(e.message)}
  }

  function rematch(){
    const used=new Set();state.matches=state.playlist.map(track=>{const ranked=state.localFiles.map(file=>({file,score:score(track.title,file.name)})).sort((a,b)=>b.score-a.score);const best=ranked.find(x=>x.score>=.42&&!used.has(x.file.name));if(best)used.add(best.file.name);return {track,file:best?.file||null,score:best?.score||0,candidates:ranked.slice(0,5)}});
  }

  async function chooseMatch(index){
    const match=state.matches.find(x=>x.track.index===index);if(!match)return;const options=match.candidates.map((x,i)=>`${i+1}. ${x.file.name} (${Math.round(x.score*100)}%)`).join('\n');const n=Number(prompt(`Escolha o arquivo para “${match.track.title}”:\n\n${options}\n\n0 = remover associação`,match.file?'1':''));if(n===0){match.file=null;match.score=0;return render();}const choice=match.candidates[n-1];if(choice){match.file=choice.file;match.score=choice.score;render();}
  }

  async function selectAtpFolder(){
    if(!apiSupported()) return notify('Use Chrome ou Edge atualizado.');
    try{const picked=await window.showDirectoryPicker({mode:'readwrite'});state.atpDir=picked;render();notify(`Destino selecionado: ${picked.name}`)}catch(e){if(e.name!=='AbortError')notify(e.message)}
  }

  function safeName(value){return String(value||'Faixa').replace(/[<>:"/\\|?*]/g,' ').replace(/\s+/g,' ').trim().replace(/[. ]+$/,'').slice(0,120)||'Faixa';}

  async function backupAndSync(){
    if(!state.playlist.length) return notify('Importe primeiro o CSV da playlist.');
    if(!state.musicDir) return notify('Selecione a pasta dos arquivos de áudio.');
    if(!state.atpDir) return notify('Selecione a pasta do canal ATP (a pasta 13).');
    const missing=state.matches.filter(x=>!x.file);if(missing.length) return notify(`Ainda faltam ${missing.length} arquivos para associar.`);
    if(!confirm(`Sincronizar ${state.matches.length} faixas com a pasta “${state.atpDir.name}”?\n\nO info.ini será preservado e trac.dat será removido para o ATP reescanear.`)) return;
    try{
      const backup=await state.atpDir.getDirectoryHandle(`backup-worship-${new Date().toISOString().replace(/[:.]/g,'-')}`,{create:true});
      for await(const [name,handle] of state.atpDir.entries()){
        if(handle.kind!=='file')continue;const ext=name.split('.').pop().toLowerCase();if(!AUDIO_EXTENSIONS.has(ext)&&name.toLowerCase()!=='trac.dat')continue;const file=await handle.getFile(), dest=await backup.getFileHandle(name,{create:true}), writer=await dest.createWritable();await writer.write(file);await writer.close();
      }
      const remove=[];for await(const [name,handle] of state.atpDir.entries()){if(handle.kind!=='file')continue;const ext=name.split('.').pop().toLowerCase();if(AUDIO_EXTENSIONS.has(ext)||name.toLowerCase()==='trac.dat'||name.toLowerCase().includes('worship fm test'))remove.push(name)}for(const name of remove)await state.atpDir.removeEntry(name);
      let seq=1;for(const item of state.matches.sort((a,b)=>a.track.index-b.track.index)){const source=await item.file.handle.getFile();const ext=item.file.name.split('.').pop().toLowerCase();const name=`${String(seq).padStart(3,'0')} - ${safeName(item.track.title)}.${ext}`;const dest=await state.atpDir.getFileHandle(name,{create:true});const writer=await dest.createWritable();await writer.write(source);await writer.close();seq++;}
      try{await state.atpDir.removeEntry('trac.dat')}catch{}
      notify(`WORSHIP FM sincronizada com ${state.matches.length} faixas. Abra o GTA para o ATP reescanear.`);render();
    }catch(e){notify(`Falha na sincronização: ${e.message}`)}
  }

  function render(){
    injectCss();const app=document.querySelector('#app');if(!app)return;
    const matched=state.matches.filter(x=>x.file).length, total=state.playlist.length, missing=Math.max(0,total-matched);
    app.innerHTML=`<section class="wfm-shell"><div class="wfm-head"><div><button class="back-link" data-wfm-home>← Biblioteca</button><span class="eyebrow">WORSHIP FM · ATP</span><h1>Monte sua rádio<br><em>com arquivos locais.</em></h1><p>Importe a lista da playlist, associe aos áudios que você possui e sincronize com o canal 13 do GTA. O Studio não baixa músicas comerciais do YouTube.</p></div><div class="wfm-summary"><b>${matched}/${total||0}</b><span>faixas prontas</span><small>${missing} pendentes</small></div></div>
      <div class="wfm-actions"><button class="btn btn-gold" data-wfm-csv>1. Importar CSV da playlist</button><button class="btn btn-outline-light" data-wfm-music>2. Selecionar pasta de músicas</button><button class="btn btn-outline-light" data-wfm-atp>3. Selecionar pasta ATP\\13</button><button class="btn btn-success" data-wfm-sync ${!total||missing?'disabled':''}>4. Sincronizar com GTA</button></div>
      <div class="wfm-status"><span>Playlist: <b>${esc(state.sourceName||'não carregada')}</b></span><span>Áudios locais: <b>${state.localFiles.length}</b></span><span>Destino: <b>${esc(state.atpDir?.name||'não selecionado')}</b></span></div>
      ${!total?'<div class="studio-panel p-4"><h3>Como começar</h3><p class="text-secondary mb-0">Use o arquivo <b>Worship_FM_Playlist.csv</b> que já geramos. Depois selecione <b>Worship_FM_Musicas</b> e a pasta <b>GTA San Andreas MOD\\atp\\13</b>.</p></div>':`<div class="wfm-table"><div class="wfm-row wfm-header"><span>#</span><span>Música</span><span>Arquivo local</span><span>Status</span></div>${state.matches.map(item=>`<div class="wfm-row"><span>${String(item.track.index).padStart(2,'0')}</span><span><b>${esc(item.track.title)}</b><small>${esc(item.track.artist||'')}</small></span><button class="wfm-file" data-wfm-match="${item.track.index}">${item.file?esc(item.file.name):'Escolher arquivo…'}</button><span class="wfm-pill ${item.file?'ok':'missing'}">${statusText(item)}</span></div>`).join('')}</div>`}
      <div class="wfm-note"><strong>Importante</strong><p>O módulo trabalha somente com arquivos que você possui ou está autorizado a usar. Ele não inclui função para extrair áudio protegido de serviços de streaming. A sincronização preserva o <code>info.ini</code> do canal e cria um backup dentro da própria pasta antes de substituir as faixas.</p></div></section>`;
    app.querySelector('[data-wfm-home]')?.addEventListener('click',()=>window.home?.());app.querySelector('[data-wfm-csv]')?.addEventListener('click',importPlaylistCsv);app.querySelector('[data-wfm-music]')?.addEventListener('click',selectMusicFolder);app.querySelector('[data-wfm-atp]')?.addEventListener('click',selectAtpFolder);app.querySelector('[data-wfm-sync]')?.addEventListener('click',backupAndSync);app.querySelectorAll('[data-wfm-match]').forEach(btn=>btn.addEventListener('click',()=>chooseMatch(Number(btn.dataset.wfmMatch))));
  }

  window.worshipFmCenter=render;
})();
