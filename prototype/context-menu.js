/* Shared context-menu owner for the design prototype. Explorer commands are previews only. */
(() => {
  const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const shapes={eye:'M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12 M15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0',open:'M3 7h7l2 2h9l-3 11H3V5h7l2 2',extract:'M4 15v6h16v-6M12 3v12m-5-5 5 5 5-5',copy:'M8 8h13v13H8zM16 8V3H3v13h5',path:'m9 15 6-6M8 16l-2 2a4 4 0 0 1-6-6l4-4a4 4 0 0 1 6 0m4 8a4 4 0 0 0 6 0l4-4a4 4 0 0 0-6-6l-2 2',info:'M12 11v6M12 7h.01M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0',check:'m5 12 4 4L19 6',up:'m5 14 7-7 7 7',all:'M3 3h7M3 3v7M21 3h-7M21 3v7M3 21h7M3 21v-7M21 21h-7M21 21v-7M8 8h8v8H8z',sort:'M5 4v16m-3-3 3 3 3-3M11 5h10M11 11h7M11 17h4',archive:'M3 3h18v5H3zM5 8v13h14V8M10 12h4',shield:'M12 2 3 6v6c0 5 9 10 9 10s9-5 9-10V6z m-5 9 3 3 5-6'};
  const svg=name=>`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${shapes[name]||shapes.info}"/></svg>`;
  const item=(id,label,icon,extra={})=>({id,label,icon,...extra});
  const sep={separator:true};
  function model(kind,{name='项目素材.7z',count=1,isText=false,isImage=false,isRoot=true,sort='name',hasRows=true}={}){
    if(kind==='app-multi')return {caption:`已选择 ${count} 项`,items:[item('extract',`解压选中的 ${count} 项…`,'extract'),sep,item('copy-name','复制文件名','copy'),item('copy-path','复制包内路径','path'),sep,item('properties','查看所选属性','info')]};
    if(kind==='app-blank')return {caption:'当前压缩包',items:[item('extract-all','全部解压…','extract'),item('test','测试完整性','shield'),sep,item('up','返回上一级','up',{disabled:isRoot,hint:isRoot?'当前为根目录':''}),item('select-all','全选','all',{disabled:!hasRows,hint:!hasRows?'当前列表为空':''}),sep,item('sort-name','按名称排序','sort',{checked:sort==='name'}),item('sort-size','按大小排序','sort',{checked:sort==='size'})]};
    if(kind.startsWith('app-')){
      const folder=kind==='app-folder';
      return {caption:name,items:[item(folder?'open':'preview',folder?'打开文件夹':'预览',folder?'open':'eye',{disabled:!folder&&!isText&&!isImage,hint:!folder&&!isText&&!isImage?'仅支持图片与纯文本':''}),item('extract','解压到…','extract'),sep,item('copy-name','复制文件名','copy'),item('copy-path','复制包内路径','path'),sep,item('properties','属性','info')]};
    }
    if(kind==='shell-archive')return {caption:'New7z',items:[item('shell-open','用 New7z 打开','open'),sep,item('shell-extract-named','解压到“项目素材\\”','extract'),item('shell-extract-to','解压到…','extract'),item('shell-extract-here','解压到当前文件夹','extract'),sep,item('shell-test','测试完整性','shield')]};
    if(kind==='shell-batch')return {caption:'New7z · 3 个压缩包',items:[item('shell-batch-extract','分别解压到各自文件夹','extract'),item('shell-extract-to','解压到…','extract'),sep,item('shell-batch-test','测试所选压缩包','shield')]};
    const mixed=kind==='shell-mixed';
    return {caption:mixed?'New7z · 混合选择':'New7z',items:[item('shell-create','新建压缩包…','archive'),sep,item('shell-pack-7z','压缩为“项目素材.7z”','archive'),item('shell-pack-zip','压缩为“项目素材.zip”','archive')]};
  }
  let popup,origin,onCommand,typeBuffer='',typeTimer;
  function ensure(){if(popup)return;popup=document.createElement('div');popup.id='context-menu';popup.className='context-menu';popup.setAttribute('popover','auto');popup.setAttribute('role','menu');document.body.append(popup);popup.addEventListener('keydown',e=>{
    const buttons=Array.from(popup.querySelectorAll('[data-menu-command]')).filter(b=>!b.disabled);const index=buttons.indexOf(document.activeElement);
    if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();const next=e.key==='Home'?0:e.key==='End'?buttons.length-1:(index+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;buttons[next]?.focus();}
    else if(e.key==='Escape'){e.preventDefault();e.stopPropagation();close(true);}
    else if(e.key==='Tab'){e.preventDefault();const from=origin;close(true);const all=Array.from(document.querySelectorAll('button,a[href],input,select,[tabindex="0"]')).filter(el=>!el.disabled&&el.getClientRects().length&&!popup.contains(el));const i=all.indexOf(from);all[i+(e.shiftKey?-1:1)]?.focus();}
    else if(e.key.length===1&&!e.ctrlKey&&!e.altKey&&!e.metaKey&&!e.isComposing){clearTimeout(typeTimer);typeBuffer+=e.key.toLocaleLowerCase('zh-CN');buttons.find(b=>b.dataset.label.toLocaleLowerCase('zh-CN').startsWith(typeBuffer))?.focus();typeTimer=setTimeout(()=>{typeBuffer='';},700);}
  });window.addEventListener('resize',()=>close(false));document.addEventListener('scroll',e=>{if(!popup.contains(e.target))close(false);},true);}
  function open({spec,x,y,anchor,command}){
    ensure();close(false);origin=anchor||document.activeElement;onCommand=command;popup.setAttribute('aria-label',spec.caption+' 的右键菜单');
    popup.innerHTML=`<div class="menu-caption" aria-hidden="true">${escape(spec.caption)}</div>`+spec.items.map(i=>i.separator?'<div class="menu-separator" role="separator"></div>':`<button onclick="new7zContextMenu.activate(event)" type="button" role="${i.checked!==undefined?'menuitemradio':'menuitem'}" ${i.checked!==undefined?'aria-checked="'+String(i.checked)+'"':''} tabindex="-1" data-menu-command="${i.id}" data-label="${escape(i.label)}" ${i.disabled?'disabled aria-disabled="true"':''} class="menu-item">${svg(i.icon)}<span class="menu-label">${escape(i.label)}${i.hint?'<small>'+escape(i.hint)+'</small>':''}</span><span class="menu-check" aria-hidden="true">${i.checked?svg('check'):''}</span></button>`).join('');
    popup.showPopover();const bounds=popup.getBoundingClientRect();popup.style.left=Math.max(8,Math.min(x,innerWidth-bounds.width-8))+'px';popup.style.top=Math.max(8,Math.min(y,innerHeight-bounds.height-8))+'px';popup.querySelector('button:not(:disabled)')?.focus({preventScroll:true});
  }
  function close(restore=false){if(!popup?.matches(':popover-open'))return;popup.hidePopover();clearTimeout(typeTimer);typeBuffer='';if(restore&&origin?.isConnected)origin.focus({preventScroll:true});}
  function activate(e){const button=e.target.closest('[data-menu-command]');if(!button||button.disabled)return;const callback=onCommand;const id=button.dataset.menuCommand;close(true);callback?.(id);}
  window.new7zContextMenu={model,open,close,activate,svg};
})();
