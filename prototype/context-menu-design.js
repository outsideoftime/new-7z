(() => {
  const $=s=>document.querySelector(s);
  const escape=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const cases=[
    {id:'app-image',name:'图片 / 文本',title:'单个图片或文本',description:'优先预览，随后解压；复制与属性收在后半段。',kind:'app-file',scope:'右键未选中项时，仅选中这一项。',rule:'预览为只读，解压沿用当前选择。',files:[['项目封面.png','PNG 图片','4.2 MB'],['README.txt','文本文档','2.4 KB'],['设计稿','文件夹','—']],selected:[0],args:{name:'项目封面.png',isImage:true}},
    {id:'app-folder',name:'文件夹',title:'压缩包内文件夹',description:'“打开文件夹”替代“预览”，操作对象始终清楚。',kind:'app-folder',scope:'选择当前文件夹，解压范围包含其子项。',rule:'打开进入包内目录，不启动外部程序。',files:[['设计稿','文件夹','680 MB'],['产品图片','文件夹','412 MB'],['品牌素材','文件夹','126 MB']],selected:[0],args:{name:'设计稿'}},
    {id:'app-multi',name:'多选文件',title:'多个文件一起处理',description:'保留多选范围，用数量明确说明执行对象。',kind:'app-multi',scope:'在已选中的项目上右键，保留整个选择集。',rule:'不显示单文件预览，不把多选缩成单选。',files:[['项目封面.png','PNG 图片','4.2 MB'],['README.txt','文本文档','2.4 KB'],['产品设计说明.pdf','PDF 文档','8.6 MB']],selected:[0,1,2],args:{count:3}},
    {id:'app-blank',name:'列表空白区域',title:'当前压缩包与视图命令',description:'空白处显示整体操作和排序，不沿用隐含的选中项。',kind:'app-blank',scope:'清除文件选择，菜单作用于当前归档或视图。',rule:'根目录的“返回上一级”禁用并说明原因。',files:[['设计稿','文件夹','680 MB'],['项目封面.png','PNG 图片','4.2 MB'],['README.txt','文本文档','2.4 KB']],selected:[],args:{isRoot:true}},
    {id:'shell-archive',name:'单个压缩包',title:'资源管理器 · 压缩包',description:'New7z 子菜单只提供归档相关命令。',kind:'shell-archive',scope:'项目素材.7z；名称来自真实选中文件。',rule:'推荐解压到同名文件夹；冲突默认保留两者。',files:[['项目素材.7z','7z 压缩包','600 MB'],['产品发布.zip','ZIP 压缩包','128 MB'],['周报.docx','Word 文档','1.4 MB']],selected:[0],args:{}},
    {id:'shell-files',name:'普通文件 / 文件夹',title:'资源管理器 · 打包文件',description:'快速 7z / ZIP 与自定义参数分别提供入口。',kind:'shell-files',scope:'项目素材文件夹；多选时合并为一个归档。',rule:'快速压缩使用标准级别；已有目标自动改名。',files:[['项目素材','文件夹','—'],['会议纪要.txt','文本文档','12 KB'],['海报.png','PNG 图片','4.2 MB']],selected:[0],args:{}},
    {id:'shell-batch',name:'多个压缩包',title:'资源管理器 · 批量解压',description:'明确“分别解压”，避免多份归档内容混在一起。',kind:'shell-batch',scope:'3 份归档分别生成任务，不跨包共享密码。',rule:'每包一个输出文件夹；逐项反馈成功与失败。',files:[['项目素材.7z','7z 压缩包','600 MB'],['产品发布.zip','ZIP 压缩包','128 MB'],['照片备份.rar','RAR 压缩包','2.4 GB']],selected:[0,1,2],args:{}},
    {id:'shell-mixed',name:'混合选择',title:'资源管理器 · 文件与归档混选',description:'把压缩包作为普通来源文件打包，不擅自展开。',kind:'shell-mixed',scope:'普通文件、文件夹和归档共同作为打包来源。',rule:'不提供“部分解压”，避免忽略混选内容。',files:[['项目素材.7z','7z 压缩包','600 MB'],['说明.txt','文本文档','12 KB'],['设计稿','文件夹','—']],selected:[0,1,2],args:{}}
  ];
  let current=cases[0];
  function render(){
    for(const [selector,shell] of [['#app-scenarios',false],['#shell-scenarios',true]])$(selector).innerHTML=cases.filter(c=>c.id.startsWith('shell')===shell).map(c=>`<button onclick="reviewMenu.select('${c.id}')" class="scenario ${current.id===c.id?'active':''}" type="button" data-scenario="${c.id}" ${current.id===c.id?'aria-current="true"':''}>${new7zContextMenu.svg(shell?'archive':c.id==='app-folder'?'open':c.id==='app-multi'?'all':c.id==='app-blank'?'sort':'eye')}${c.name}</button>`).join('');
    const shell=current.id.startsWith('shell');$('#scene-kind').textContent=shell?'Windows 资源管理器':'New7z 应用内';$('#scene-title').textContent=current.title;$('#scene-description').textContent=current.description;$('#scope-rule').textContent=current.scope;$('#default-rule').textContent=current.rule;
    $('#stage-brand').textContent=shell?'文件资源管理器':'New7z · 项目素材.7z';$('#stage-breadcrumb').textContent=shell?'此电脑　›　下载':'项目素材.7z　›　根目录';
    $('#stage-commandbar').innerHTML=shell?'<span>新建</span><span>排序</span><span>查看</span>':`<span>${new7zContextMenu.svg('extract')}全部解压</span><span>${new7zContextMenu.svg('eye')}预览</span><span>${new7zContextMenu.svg('shield')}测试完整性</span>`;
    $('#stage-rows').innerHTML=current.files.map((f,i)=>`<div class="stage-row ${current.selected.includes(i)?'selected':''}"><span>${new7zContextMenu.svg(f[1]==='文件夹'?'open':f[1].includes('图片')?'eye':'archive')}${escape(f[0])}</span><span>${escape(f[1])}</span><span>${escape(f[2])}</span></div>`).join('');
    $('#stage-hint').textContent=shell?'以下为 New7z 子菜单展开后的内容；系统主菜单由 Windows 绘制。':current.selected.length?'已选择 '+current.selected.length+' 项 · 只读归档':'未选择文件 · 当前目录共 3 项';
    document.title=current.title+' · New7z 菜单设计';
  }
  function open(x,y){
    const row=$('.stage-row'),rect=row.getBoundingClientRect(),blank=current.id==='app-blank';
    new7zContextMenu.open({spec:new7zContextMenu.model(current.kind,current.args),x:x??rect.left+Math.min(310,rect.width*.42),y:y??(blank?rect.bottom+25:rect.top+12),anchor:$('#show-menu'),command:id=>{
      const spec=new7zContextMenu.model(current.kind,current.args);const label=spec.items.find(i=>i.id===id)?.label;
      const result={preview:'打开只读预览窗口。',open:'进入所选包内目录。',extract:'打开解压设置并保留选择范围。','extract-all':'打开当前压缩包的全部解压设置。','copy-name':'按行复制所选名称。','copy-path':'按行复制归档虚拟路径，不假装是已解压的本地路径。',properties:'展示只读属性，不修改源归档。','shell-extract-named':'输出到下载\\项目素材\\；目标已存在时按冲突策略处理。','shell-extract-here':'输出到当前文件夹；不静默覆盖已有文件。','shell-batch-extract':'为每份归档创建独立输出文件夹与任务。'}[id]||'命令交由应用任务流程处理。';
      $('#command-result').textContent='设计演示：'+label+' — '+result+' 未操作真实文件。';
    }});
  }
  window.reviewMenu={select(id){new7zContextMenu.close(false);current=cases.find(c=>c.id===id)||cases[0];render();open();},open};
  $('#review-stage').addEventListener('contextmenu',e=>{e.preventDefault();open(e.clientX,e.clientY);});$('#show-menu').addEventListener('keydown',e=>{if((e.shiftKey&&e.key==='F10')||e.key==='ContextMenu'){e.preventDefault();open();}});
  $('#review-theme').addEventListener('change',e=>{new7zContextMenu.close(false);document.documentElement.dataset.theme=e.target.value;});
  render();open();
})();
