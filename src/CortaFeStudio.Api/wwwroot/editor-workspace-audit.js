// Auditoria do editor · layout, prévia fiel e modo foco.
(function(){
  const previewTimers=new Map();
  let resizeObserver=null;

  const renderBase=renderProject;
  renderProject=function(project){
    renderBase(project);
    if(project.status!=='ready')return;
    requestAnimationFrame(()=>requestAnimationFrame(()=>mountAudit(project)));
  };

  const selectBase=selectClip;
  selectClip=function(project,id){
    selectBase(project,id);
    requestAnimationFrame(()=>requestAnimationFrame(()=>{
      mountAudit(project);
      const clip=project.clips.find(item=>item.id===id);
      syncPreviewGeometry(clip);
      renderLiveBrand(clip);
    }));
  };

  function mountAudit(project){
    const workspace=document.querySelector('.cc-workspace-v2');
    if(!workspace)return;
    installInsightsDrawer(workspace);
    moveInsightPanels(workspace);
    installPreviewTools(project);
    bindEditorAuditEvents();
    const clip=activeClip(project);
    syncPreviewGeometry(clip);
    renderLiveBrand(clip);
  }

  function installInsightsDrawer(workspace){
    if(workspace.querySelector('.cc-editor-insights-drawer'))return;
    const drawer=document.createElement('aside');
    drawer.className='cc-editor-insights-drawer';
    drawer.innerHTML='<header class="cc-editor-insights-head"><div><strong>IA, lote e contexto</strong><small>Ferramentas avançadas sem reduzir o monitor.</small></div><button type="button" data-editor-insights-close aria-label="Fechar">×</button></header><div class="cc-editor-insights-body"></div>';
    workspace.append(drawer);
    drawer.querySelector('[data-editor-insights-close]').onclick=()=>toggleInsights(false);
    const actions=workspace.querySelector('.section-head>.d-flex');
    if(actions&&!actions.querySelector('[data-editor-insights]')){
      const button=document.createElement('button');
      button.type='button';button.className='btn btn-outline-light';button.dataset.editorInsights='1';button.textContent='IA & Lote';button.onclick=()=>toggleInsights();
      actions.prepend(button);
    }
  }

  function moveInsightPanels(workspace){
    const body=workspace.querySelector('.cc-editor-insights-body');if(!body)return;
    ['.aj-productivity-suite','.attention-ai-panel','.smart-camera-panel','.sermon-intelligence-summary'].forEach(selector=>{
      const panel=document.querySelector(selector);
      if(panel&&!body.contains(panel))body.append(panel);
    });
  }

  function toggleInsights(force){
    const drawer=document.querySelector('.cc-editor-insights-drawer');if(!drawer)return;
    const open=typeof force==='boolean'?force:!drawer.classList.contains('open');
    drawer.classList.toggle('open',open);
    document.querySelector('[data-editor-insights]')?.classList.toggle('active',open);
  }

  function installPreviewTools(project){
    const head=document.querySelector('.cc-canvas-stage .monitor-head');
    if(!head||head.querySelector('.editor-preview-tools'))return;
    const tools=document.createElement('div');tools.className='editor-preview-tools';
    tools.innerHTML='<span class="editor-preview-state" data-editor-preview-state>Prévia pronta</span><button type="button" data-editor-preview-refresh>Atualizar prévia</button><button type="button" data-editor-preview-focus>Ampliar</button>';
    head.append(tools);
    tools.querySelector('[data-editor-preview-refresh]').onclick=()=>refreshEditingPreview(project,true);
    tools.querySelector('[data-editor-preview-focus]').onclick=event=>{
      document.body.classList.toggle('cc-preview-focus');
      const focused=document.body.classList.contains('cc-preview-focus');event.currentTarget.textContent=focused?'Voltar editor':'Ampliar';
      requestAnimationFrame(()=>fitPreview(activeClip(project)));
    };
    observeStage();
  }

  function bindEditorAuditEvents(){
    const root=document.querySelector('.cc-workspace-v2');if(!root||root.dataset.auditBound)return;root.dataset.auditBound='1';
    root.addEventListener('change',event=>{
      const card=event.target.closest('.clip-card');
      if(card){
        const clip=current?.clips.find(item=>item.id===card.dataset.clip);if(!clip)return;
        if(event.target.closest('.cc-mode-visual')||event.target.closest('.editor-tools')){
          applyVisualValues(card,clip);syncPreviewGeometry(clip);setPreviewState('Alteração visual · atualizando','dirty');schedulePreviewRefresh(card.dataset.clip);
        }
        if(event.target.closest('.cc-mode-brand')||event.target.closest('.brand-editor')){
          applyBrandValues(card,clip);renderLiveBrand(clip);setPreviewState('Marca · atualizando prévia','dirty');schedulePreviewRefresh(card.dataset.clip);
        }
      }
    });
    root.addEventListener('input',event=>{
      const card=event.target.closest('.clip-card');if(!card)return;
      const clip=current?.clips.find(item=>item.id===card.dataset.clip);if(!clip)return;
      if(event.target.closest('.cc-mode-brand')||event.target.closest('.brand-editor')){
        applyBrandValues(card,clip);renderLiveBrand(clip);
        if(!isCleanPreview(clip))schedulePreviewRefresh(card.dataset.clip);
      }
      if(event.target.name==='outputPreset'){applyVisualValues(card,clip);syncPreviewGeometry(clip);}
    });
    root.addEventListener('click',event=>{
      const mode=event.target.closest('[data-cc-mode]')?.dataset.ccMode;
      if(mode==='visual'||mode==='brand'||mode==='captions')setTimeout(()=>{const clip=activeClip(current);syncPreviewGeometry(clip);renderLiveBrand(clip);},80);
      if(event.target.closest('[data-camera-mode],[data-camera-analyze]')){
        setPreviewState('Smart Camera · aguardando análise','busy');
        setTimeout(()=>{const clip=activeClip(current);if(clip)schedulePreviewRefresh(clip.id);},900);
      }
    });
    document.addEventListener('keydown',event=>{
      if(event.key==='Escape'&&document.body.classList.contains('cc-preview-focus')){
        document.body.classList.remove('cc-preview-focus');const button=document.querySelector('[data-editor-preview-focus]');if(button)button.textContent='Ampliar';requestAnimationFrame(()=>fitPreview(activeClip(current)));
      }
    });
  }

  function schedulePreviewRefresh(clipId){
    clearTimeout(previewTimers.get(clipId));
    previewTimers.set(clipId,setTimeout(()=>{if(activeClip(current)?.id===clipId)refreshEditingPreview(current,false);},650));
  }

  async function refreshEditingPreview(project,explicit){
    const clip=activeClip(project),card=activeCard();if(!clip||!card)return;
    const button=document.querySelector('[data-editor-preview-refresh]');if(button)button.disabled=true;
    setPreviewState('Gerando prévia leve…','busy');
    const before=document.querySelector('#preview video');
    const relative=before?.dataset.sourcePreview?Math.max(0,before.currentTime-clip.start):Math.max(0,before?.currentTime||0);
    try{
      await saveClip(project,card);
      const result=await api(`/api/projects/${project.id}/clips/${clip.id}/preview`,{method:'POST'});
      clip.previewPath=result.path;clip.renderOutdated=!!clip.videoPath;
      showCleanPreview(clip,relative);
      syncPreviewGeometry(clip);renderLiveBrand(clip);setPreviewState('Prévia atualizada','ok');
      if(explicit)toast('✓ Prévia atualizada com os ajustes atuais');
    }catch(error){setPreviewState('Falha na prévia','dirty');if(explicit)toast(error.message)}finally{if(button)button.disabled=false}
  }

  function showCleanPreview(clip,relative){
    if(typeof activateLiveSubtitlePreview==='function'){
      const video=activateLiveSubtitlePreview(clip);
      if(video){
        const restore=()=>{video.currentTime=Math.min(Number.isFinite(video.duration)?video.duration:clip.end-clip.start,relative)};
        video.readyState>=1?restore():video.addEventListener('loadedmetadata',restore,{once:true});
        if(typeof updateSubtitlePreview==='function')updateSubtitlePreview(video,clip);
      }
      return;
    }
    const preview=document.querySelector('#preview');if(!preview||!clip.previewPath)return;
    preview.innerHTML=`<video controls playsinline data-clip-preview="true" data-clip-id="${clip.id}" src="/api/projects/${current.id}/assets/${clip.previewPath}?v=${Date.now()}"></video>`;
  }

  function applyVisualValues(card,clip){
    const val=(name,fallback)=>card.querySelector(`[name="${name}"]`)?.value??fallback;
    clip.cropFocus=val('cropFocus',clip.cropFocus);clip.cropX=number(val('cropX',clip.cropX),clip.cropX);
    clip.layoutMode=val('layoutMode',clip.layoutMode);clip.splitLeftX=number(val('splitLeftX',clip.splitLeftX),clip.splitLeftX);clip.splitRightX=number(val('splitRightX',clip.splitRightX),clip.splitRightX);
    clip.outputPreset=val('outputPreset',clip.outputPreset||'vertical');clip.transitionStyle=val('transitionStyle',clip.transitionStyle);clip.playbackSpeed=number(val('playbackSpeed',clip.playbackSpeed||1),1);
    const silence=card.querySelector('[name="silenceTrimmingEnabled"]');if(silence)clip.silenceTrimmingEnabled=silence.checked;
  }

  function applyBrandValues(card,clip){
    const enabled=card.querySelector('[name="brandFrameEnabled"]'),watermark=card.querySelector('[name="watermarkEnabled"]'),theme=card.querySelector('[name="brandTheme"]'),text=card.querySelector('[name="watermarkText"]'),opacity=card.querySelector('[name="watermarkOpacity"]');
    if(enabled)clip.brandFrameEnabled=enabled.checked;if(watermark)clip.watermarkEnabled=watermark.checked;if(theme)clip.brandTheme=theme.value;if(text)clip.watermarkText=text.value;if(opacity)clip.watermarkOpacity=number(opacity.value,.82);
  }

  function isCleanPreview(clip){
    const video=document.querySelector('#preview video');
    return !!(video?.dataset.clipPreview||video?.dataset.sourcePreview||(clip?.previewPath&&video?.src?.includes(clip.previewPath)));
  }

  function renderLiveBrand(clip){
    const preview=document.querySelector('#preview');if(!preview||!clip)return;
    preview.querySelector('.editor-live-brand')?.remove();
    if(!isCleanPreview(clip))return;
    const card=activeCard();if(card)applyBrandValues(card,clip);
    if(clip.brandFrameEnabled===false&&clip.watermarkEnabled===false)return;
    preview.querySelector('.brand-preview')?.remove();
    const overlay=document.createElement('div');overlay.className=`editor-live-brand theme-${clip.brandTheme||'amado-jesus'}`;
    overlay.innerHTML=`${clip.brandFrameEnabled!==false?'<i class="brand-frame"></i><i class="brand-bottom"></i>':''}${clip.watermarkEnabled!==false?`<strong style="opacity:${Math.max(.1,Math.min(1,number(clip.watermarkOpacity,.82)))}">${escapeHtml(clip.watermarkText||'AJ  |  AMADO JESUS')}</strong>`:''}`;
    preview.append(overlay);
  }

  function syncPreviewGeometry(clip){if(!clip)return;const preview=document.querySelector('#preview');if(!preview)return;preview.dataset.outputPreset=clip.outputPreset||'vertical';requestAnimationFrame(()=>fitPreview(clip));}
  function fitPreview(clip){
    const stage=document.querySelector('.cc-canvas-stage'),preview=document.querySelector('#preview');if(!stage||!preview||!clip)return;
    const ratios={vertical:9/16,portrait:4/5,square:1,landscape:16/9},ratio=ratios[clip.outputPreset]||ratios.vertical;
    const head=stage.querySelector('.monitor-head')?.offsetHeight||34,transport=stage.querySelector('.transport')?.offsetHeight||44,help=stage.querySelector('.shortcut-help')?.offsetHeight||16;
    const maxWidth=Math.max(160,stage.clientWidth-44),maxHeight=Math.max(180,stage.clientHeight-head-transport-help-34);
    let width=maxWidth,height=width/ratio;if(height>maxHeight){height=maxHeight;width=height*ratio}
    preview.style.setProperty('width',`${Math.floor(width)}px`,'important');preview.style.setProperty('height',`${Math.floor(height)}px`,'important');preview.style.setProperty('aspect-ratio',String(ratio),'important');
  }

  function observeStage(){const stage=document.querySelector('.cc-canvas-stage');if(!stage||stage.dataset.auditResize)return;stage.dataset.auditResize='1';resizeObserver?.disconnect();resizeObserver=new ResizeObserver(()=>fitPreview(activeClip(current)));resizeObserver.observe(stage);}
  function activeClip(project){const id=document.querySelector('.clip-card.active')?.dataset.clip||document.querySelector('#ccClipPicker')?.value;return project?.clips?.find(item=>item.id===id)||project?.clips?.[0]||null;}
  function activeCard(){const id=activeClip(current)?.id;return id?document.querySelector(`.clip-card[data-clip="${id}"]`):null;}
  function setPreviewState(text,state=''){const el=document.querySelector('[data-editor-preview-state]');if(!el)return;el.textContent=text;el.className=`editor-preview-state ${state}`.trim();}
  function number(value,fallback){const n=Number(value);return Number.isFinite(n)?n:fallback;}

  window.AmadoJesusEditorAudit={refresh:()=>refreshEditingPreview(current,true),focus:()=>document.querySelector('[data-editor-preview-focus]')?.click(),insights:()=>toggleInsights()};
})();
