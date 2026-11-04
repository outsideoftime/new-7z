/* 图片/文本预览交互使用受控示例资源；尚未读取真实归档。 */
const previewState={file:null,list:[],zoom:'fit',wrap:true,find:'',encoding:'UTF-8',sequence:0,phase:'idle',origin:null};
const previewText=`New7z · 项目素材
================

这份归档包含项目设计稿、产品图片和品牌素材。
你可以先预览图片与文本，再解压需要的文件。

目录说明
  设计稿/          产品界面与设计预览
  产品图片/        主视觉与产品实拍
  品牌素材/        品牌规范
  项目封面.png     项目封面图片
  README.txt       当前说明文件

设计约定
  平台：Windows 11
  风格：Fluent Design
  字体：Segoe UI Variable / Microsoft YaHei UI
  强调色：#0067c0

预览说明
  图片支持适应窗口与原始尺寸查看。
  文本支持查找、行号与自动换行；查看过程不会修改原文件。
  这一行用于检查长文本换行：归档中的文字无需先完整解压即可查看，长行可以自动换行，也可以关闭换行并使用水平滚动阅读，文件名、路径和代码始终保持原文。

示例配置（按纯文本显示，不执行标记）
  <archive name="项目素材.7z" mode="read-only" />

本内容是原型内置示例，不来自真实压缩包。
`;
function previewKind(file){
  if(/\.(png|jpe?g|webp|bmp|gif)$/i.test(file.name))return 'image';
  if(/\.(txt|md|log|csv|json|xml|ya?ml|ini|cfg|js|ts|css|cs|cpp|h|html?)$/i.test(file.name))return 'text';
  return 'unsupported';
}
function canPreviewSelection(){const all=state.folder?children[state.folder]:sample;return state.selected.size===1&&all.some(f=>state.selected.has(f.id)&&f.type!=='folder');}
function previewButton(file){return `<button onclick="handleClick(event)" class="btn" data-action="preview-file" data-id="${file.id}">${icon('eye')}预览文件</button>`;}
function openPreview(file){
  previewState.origin=document.activeElement;
  previewState.list=archiveRows().filter(f=>f.type!=='folder'&&previewKind(f)!=='unsupported');
  if(!previewState.list.some(f=>f.id===file.id))previewState.list=[file];
  showDialog(esc(file.name),'压缩包内预览 · 示例内容',`<div class="preview-location">${icon('archive')}<span>${esc(state.archiveName)} / ${state.folder?esc(sample.find(f=>f.id===state.folder).name)+' / ':''}<span id="preview-filename">${esc(file.name)}</span></span><span class="pill">只读</span></div>
    <div class="preview-toolbar"><div class="actions"><button onclick="handleClick(event)" type="button" class="icon-button" data-action="preview-prev" aria-label="预览上一个文件">${icon('arrow','flip')}</button><span id="preview-position" class="small muted"></span><button onclick="handleClick(event)" type="button" class="icon-button" data-action="preview-next" aria-label="预览下一个文件">${icon('arrow')}</button></div>
    <div id="image-tools" class="actions"><label class="small" for="preview-zoom">缩放</label><select id="preview-zoom" aria-label="图片缩放"><option value="fit">适应窗口</option><option value="25">25%</option><option value="50">50%</option><option value="100">100% · 原始尺寸</option><option value="200">200%</option></select></div>
    <div id="text-tools" class="actions" hidden><label class="small" for="preview-encoding">编码</label><select id="preview-encoding"><option>UTF-8</option><option>GB18030</option><option>UTF-16 LE</option></select><button onclick="handleClick(event)" type="button" class="btn" data-action="preview-wrap" aria-pressed="true">自动换行</button></div></div>
    <div id="preview-find-row" class="preview-find-row" hidden><div class="search">${icon('search')}<input id="preview-find" type="search" aria-label="查找文本内容" placeholder="查找文本内容"><button onclick="handleClick(event)" type="button" class="icon-button" data-action="preview-clear-find" aria-label="清空文本查找" hidden>${icon('close')}</button></div><span id="preview-matches" class="small muted" role="status" aria-live="polite"></span></div>
    <div id="preview-content" class="preview-content" aria-busy="true"></div><div class="preview-bottomline"><span id="preview-metadata"></span><button onclick="handleClick(event)" type="button" class="btn ghost" data-action="preview-fail">演示读取失败</button></div>`,
    `<span class="preview-footnote">仅读取所选文件 · 当前展示内置示例</span><button onclick="handleClick(event)" type="button" class="btn" data-action="close-dialog">关闭预览</button>`);
  dialog.classList.add('preview-dialog');
  $('#preview-zoom').addEventListener('change',e=>{previewState.zoom=e.target.value;applyPreviewZoom();});
  $('#preview-encoding').addEventListener('change',e=>{previewState.encoding=e.target.value;updatePreviewMetadata();});
  let composingFind=false;
  const finder=$('#preview-find');
  finder.addEventListener('compositionstart',()=>{composingFind=true;});
  finder.addEventListener('compositionend',()=>{composingFind=false;previewState.find=finder.value;renderPreviewText();});
  finder.addEventListener('input',()=>{if(!composingFind){previewState.find=finder.value;renderPreviewText();}});
  changePreviewFile(file);
}
function changePreviewFile(file){
  const focusBefore=document.activeElement;
  previewState.file=file;previewState.zoom='fit';previewState.find='';previewState.encoding='UTF-8';
  $('#dialog-title').textContent=file.name;$('#preview-filename').textContent=file.name;
  $('#preview-zoom').value='fit';$('#preview-find').value='';$('#preview-encoding').value='UTF-8';
  const index=previewState.list.findIndex(f=>f.id===file.id);
  $('#preview-position').textContent=`${index+1} / ${previewState.list.length}`;
  $('[data-action="preview-prev"]').disabled=index<=0;
  $('[data-action="preview-next"]').disabled=index>=previewState.list.length-1;
  const kind=previewKind(file);
  $('#image-tools').hidden=kind!=='image';$('#text-tools').hidden=kind!=='text';$('#preview-find-row').hidden=kind!=='text';
  // Switching image/text tools may hide the focused select or disable a pager.
  if(!focusBefore.getClientRects().length||focusBefore.disabled){
    const next=$('[data-action="preview-next"]'),prev=$('[data-action="preview-prev"]');
    (!next.disabled?next:!prev.disabled?prev:$('[data-action="close-dialog"]',dialog)).focus();
  }
  loadPreview();
}
function loadPreview(){
  const seq=++previewState.sequence;
  const kind=previewKind(previewState.file), content=$('#preview-content');
  content.setAttribute('aria-busy','true');previewState.phase='loading';
  content.innerHTML=`<div class="preview-empty" role="status">${icon('clock')}<h3>正在准备预览…</h3><p>当前为示例读取过程。</p></div>`;
  $('#preview-metadata').textContent='正在读取…';
  setTimeout(()=>{
    if(seq!==previewState.sequence||!dialog.open)return;
    if(kind==='image'&&previewState.file.bytes>32){previewState.phase='unsupported';$('#image-tools').hidden=true;content.setAttribute('aria-busy','false');content.innerHTML=`<div class="preview-empty">${icon('info')}<h3>图片超过快速预览限制</h3><p>此文件为 ${esc(previewState.file.size)}。快速预览默认限制为 32 MB，请先解压后查看。</p><button onclick="handleClick(event)" type="button" class="btn" data-action="preview-extract">解压这个文件</button></div>`;$('#preview-metadata').textContent='超过大小限制 · 原型按示例文件大小演示';return;}
    if(kind==='unsupported'){previewState.phase='unsupported';content.setAttribute('aria-busy','false');content.innerHTML=`<div class="preview-empty">${icon('info')}<h3>暂不支持这种文件的内置预览</h3><p>首版预览图片与纯文本。可以先解压这个文件，再使用对应应用打开。</p><button onclick="handleClick(event)" type="button" class="btn" data-action="preview-extract">解压这个文件</button></div>`;updatePreviewMetadata();return;}
    if(kind==='text'){previewState.phase='ready';content.setAttribute('aria-busy','false');renderPreviewText();return;}
    const image=new Image();
    const timeout=setTimeout(()=>{if(seq===previewState.sequence&&dialog.open)previewError('图片读取超时，请重新尝试。');},8000);
    image.onload=()=>{clearTimeout(timeout);if(seq!==previewState.sequence||!dialog.open)return;previewState.phase='ready';image.id='preview-image';image.alt=previewState.file.name+'，内置示例图片';content.replaceChildren(image);content.setAttribute('aria-busy','false');applyPreviewZoom();};
    image.onerror=()=>{clearTimeout(timeout);if(seq===previewState.sequence&&dialog.open)previewError('无法读取图片，请重新尝试。');};
    image.src='assets/preview-cover.svg';
  },180);
}
function applyPreviewZoom(){
  const image=$('#preview-image');if(!image)return;
  const fit=previewState.zoom==='fit';image.classList.toggle('fit',fit);
  image.style.width=fit?'':image.naturalWidth*Number(previewState.zoom)/100+'px';
  image.style.height=fit?'':image.naturalHeight*Number(previewState.zoom)/100+'px';
  updatePreviewMetadata();
}
function renderPreviewText(){
  if(previewState.phase!=='ready'||previewKind(previewState.file)!=='text')return;
  const query=previewState.find.toLocaleLowerCase('zh-CN');let count=0;
  const lines=previewText.split('\n').map((line,index)=>{
    let text='',start=0,pos;const lower=line.toLocaleLowerCase('zh-CN');
    while(query&&(pos=lower.indexOf(query,start))!==-1){text+=esc(line.slice(start,pos))+'<mark>'+esc(line.slice(pos,pos+query.length))+'</mark>';start=pos+query.length;count++;}
    text+=esc(line.slice(start));
    return `<div class="text-line"><span aria-hidden="true">${index+1}</span><code>${text||' '}</code></div>`;
  }).join('');
  $('#preview-content').innerHTML=`<div class="text-view ${previewState.wrap?'wrap':''}" role="region" tabindex="0" aria-label="只读文本内容">${lines}</div>`;
  $('[data-action="preview-clear-find"]').hidden=!query;
  $('#preview-matches').textContent=query?count?'找到 '+count+' 处匹配':'没有找到匹配内容':'';
  $('[data-action="preview-wrap"]').setAttribute('aria-pressed',String(previewState.wrap));
  updatePreviewMetadata();
}
function updatePreviewMetadata(){
  const kind=previewKind(previewState.file);
  $('#preview-metadata').textContent=previewState.phase==='error'?'读取失败 · 可重新尝试':kind==='image'?'1600 × 1000 · '+(previewState.zoom==='fit'?'适应窗口':previewState.zoom+'%')+' · 示例图片':kind==='text'?previewText.split('\n').length+' 行 · '+previewState.encoding+(previewState.encoding==='UTF-8'?'':'（编码切换示例）')+' · 只读文本':'未启用内置预览';
}
function previewError(message='无法读取文件内容。这是预览失败的演示状态。'){
  ++previewState.sequence;previewState.phase='error';const content=$('#preview-content');content.setAttribute('aria-busy','false');content.innerHTML=`<div class="preview-empty" role="status">${icon('info')}<h3>无法预览这个文件</h3><p>${esc(message)}</p><button onclick="handleClick(event)" type="button" class="btn" data-action="preview-retry">重新读取</button></div>`;updatePreviewMetadata();
}
function previewAction(action){
  if(action==='preview-prev'||action==='preview-next'){const i=previewState.list.findIndex(f=>f.id===previewState.file.id)+(action==='preview-next'?1:-1);if(previewState.list[i])changePreviewFile(previewState.list[i]);}
  else if(action==='preview-wrap'){previewState.wrap=!previewState.wrap;renderPreviewText();}
  else if(action==='preview-clear-find'){previewState.find='';$('#preview-find').value='';renderPreviewText();$('#preview-find').focus();}
  else if(action==='preview-fail')previewError();
  else if(action==='preview-retry')loadPreview();
  else if(action==='preview-extract'){state.selected=new Set([previewState.file.id]);closeDialog();render(false);extractDialog();}
}
dialog.addEventListener('close',()=>{++previewState.sequence;previewState.phase='idle';dialog.classList.remove('preview-dialog');if(previewState.origin?.isConnected)previewState.origin.focus({preventScroll:true});previewState.origin=null;});
dialog.addEventListener('keydown',e=>{if(!dialog.classList.contains('preview-dialog')||e.isComposing||!e.altKey)return;if(e.key==='ArrowRight'||e.key==='ArrowLeft'){e.preventDefault();previewAction(e.key==='ArrowRight'?'preview-next':'preview-prev');}});
