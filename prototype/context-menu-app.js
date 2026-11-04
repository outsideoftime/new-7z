/* Adapter: shared design commands act on the current prototype selection. */
(() => {
  function currentFiles(){const all=state.folder?children[state.folder]:sample;return archiveRows().filter(f=>state.selected.has(f.id));}
  async function copyText(text){try{await navigator.clipboard.writeText(text);notify('已复制到剪贴板。');}catch{showDialog('复制内容','浏览器未允许直接写入剪贴板，可手动复制。',`<div class="field"><label for="context-copy">复制内容</label><textarea id="context-copy" rows="5" readonly autofocus style="resize:none">${esc(text)}</textarea></div>`,`<button onclick="handleClick(event)" type="button" class="btn" data-action="close-dialog">关闭</button>`);$('#context-copy').select();}}
  function execute(id){const files=currentFiles(),file=files[0];
    if(id==='preview'&&file)openPreview(file);
    else if(id==='open'&&file){state.folder=file.id;state.selected.clear();state.query='';render();}
    else if(id==='extract')extractDialog();
    else if(id==='extract-all'){state.selected.clear();refreshTable();extractDialog();}
    else if(id==='test')addTask(state.archiveName,'测试完整性','仅读取 · 示例');
    else if(id==='copy-name')copyText(files.map(f=>f.name).join('\n'));
    else if(id==='copy-path')copyText(files.map(f=>state.archiveName+'!/'+(state.folder?sample.find(s=>s.id===state.folder).name+'/':'')+f.name).join('\n'));
    else if(id==='select-all'){state.selected=new Set(archiveRows().map(f=>f.id));refreshTable();$('#select-all')?.focus();}
    else if(id==='up'){state.folder='';state.query='';state.selected.clear();render();}
    else if(id==='sort-name'||id==='sort-size'){state.sort=id==='sort-name'?'name':'size';state.asc=true;refreshTable();}
    else if(id==='properties'){showDialog(files.length>1?'所选项目属性':'属性','示例归档中的只读属性',`<dl class="menu-properties"><dt>来源压缩包</dt><dd>${esc(state.archiveName)}</dd><dt>选中项目</dt><dd>${files.length} 项</dd><dt>名称</dt><dd>${files.map(f=>esc(f.name)).join('<br>')}</dd><dt>原始大小</dt><dd>${files.map(f=>esc(f.size)).join(' / ')}</dd></dl>`,`<button onclick="handleClick(event)" class="btn primary" type="button" data-action="close-dialog">关闭</button>`);}
  }
  function show({target,x,y,keyboard=false,button=false}){
    const row=target?.closest('#archive-table tbody tr'),id=row?.querySelector('[data-select]')?.dataset.select;
    if(id&&!state.selected.has(id)){state.selected=new Set([id]);refreshTable();}
    else if(!id&&!button){state.selected.clear();refreshTable();}
    const files=currentFiles(),file=files[0];
    const kind=files.length>1?'app-multi':file?file.type==='folder'?'app-folder':'app-file':'app-blank';
    const anchor=id?$(`[data-action="file"][data-id="${id}"]`):button?target:$('#select-all');
    if(keyboard){const rect=(anchor||$('#archive-table')).getBoundingClientRect();x=rect.left+16;y=rect.bottom+4;}
    new7zContextMenu.open({spec:new7zContextMenu.model(kind,{name:file?.name,count:files.length,isText:file?.type==='code'&&/\.txt$/i.test(file.name),isImage:file?.type==='image',isRoot:!state.folder,sort:state.sort,hasRows:archiveRows().length>0}),x,y,anchor,command:execute});
  }
  document.addEventListener('contextmenu',e=>{if(state.page!=='archive'||dialog.open||!e.target.closest('#archive-table'))return;e.preventDefault();show({target:e.target,x:e.clientX,y:e.clientY});});
  document.addEventListener('keydown',e=>{if(state.page!=='archive'||dialog.open||e.isComposing)return;if((e.shiftKey&&e.key==='F10')||e.key==='ContextMenu'){if(e.target.closest('#archive-table')){e.preventDefault();show({target:e.target,keyboard:true});}}});
  window.showArchiveContextMenu=button=>{const rect=button.getBoundingClientRect();show({target:button,x:rect.left,y:rect.bottom+5,keyboard:false,button:true});};
})();
