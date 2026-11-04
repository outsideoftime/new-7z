const { chromium } = require('playwright');
const AxeBuilder = require('@axe-core/playwright').default;
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');

const root = path.resolve(__dirname, '..');
const output = path.join(root, 'docs', 'screenshots');
fs.mkdirSync(output, { recursive: true });
const records = [];
const server = http.createServer((req, res) => {
  let target;
  try { target = path.resolve(root, '.' + decodeURIComponent(new URL(req.url, 'http://localhost').pathname)); }
  catch { res.writeHead(400).end(); return; }
  if (!target.startsWith(root + path.sep)) { res.writeHead(403).end(); return; }
  const mime = { '.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.png':'image/png','.svg':'image/svg+xml','.md':'text/plain; charset=utf-8' };
  fs.readFile(target, (err, data) => { if (err) { res.writeHead(404).end(); return; } res.writeHead(200, { 'Content-Type':mime[path.extname(target)] || 'application/octet-stream' }); res.end(data); });
});
function installedBrowser() {
  if (fs.existsSync(chromium.executablePath())) return chromium.executablePath();
  const cache = path.join(process.env.LOCALAPPDATA || '', 'ms-playwright');
  if (fs.existsSync(cache)) {
    const dirs = fs.readdirSync(cache).filter(s => /^chromium-\d+$/.test(s)).sort((a,b)=>parseInt(b.split('-')[1])-parseInt(a.split('-')[1]));
    for (const dir of dirs) {
      const candidate = path.join(cache,dir,'chrome-win64','chrome.exe');
      if(fs.existsSync(candidate)) return candidate;
    }
  }
  return undefined;
}
(async()=>{
  await new Promise(resolve => server.listen(0,'127.0.0.1',resolve));
  const browser = await chromium.launch({headless:true,executablePath:installedBrowser()});
  const context = await browser.newContext({viewport:{width:1440,height:960},locale:'zh-CN',colorScheme:'light'});
  const page = await context.newPage();
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const action=(name)=>page.locator(`[data-action="${name}"]`).first();
  const shot=async(name)=>{await page.waitForTimeout(200);await page.screenshot({path:path.join(output,name+'.png'),fullPage:true,animations:'disabled',style:'#notification { visibility: hidden; }'});};
  const check=async(name,fn)=>{await fn();records.push({name,result:'pass'});};
  const axe=async(name)=>{const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();fs.writeFileSync(path.join(output,name+'-axe.json'),JSON.stringify(result.violations,null,2));assert.deepEqual(result.violations.map(v=>({id:v.id,impact:v.impact,targets:v.nodes.map(n=>n.target)})),[],name+' accessibility');records.push({name:name+' axe',result:'pass'});};
  try{
    await page.goto(`http://127.0.0.1:${server.address().port}/prototype/index.html`);
    await check('主页与资源加载',async()=>{await page.locator('h1').filter({hasText:'主页'}).waitFor();assert.equal(await page.locator('.recent-table tbody tr').count(),4);assert(await page.locator('.drop-graphic').evaluate(e=>e.complete&&e.naturalWidth>0));});
    await shot('01-主页');await axe('home-light');
    await action('open').click();await shot('02-压缩包浏览');await axe('archive-light');
    await check('排序、选择与目录范围',async()=>{
      await page.locator('[data-sort="size"]').click();assert.match(await page.locator('#archive-table tbody tr').first().innerText(),/README/);
      await page.locator('[data-select="pdf"]').check();assert.match(await page.locator('#extract-label').innerText(),/1/);
      await page.locator('[data-action="file"][data-id="design"]').click();assert.equal(await page.locator('#archive-table tbody tr').count(),2);assert.equal(await page.locator('[data-select]:checked').count(),0);await action('root').click();
    });
    await check('无结果、清除与中文输入法组合',async()=>{
      await page.locator('#file-search').fill('不存在');assert.equal(await page.locator('#archive-table tbody tr').count(),0);await action('clear-search').click();assert.equal(await page.locator('#archive-table tbody tr').count(),6);
      await page.locator('#file-search').evaluate(el=>{el.dispatchEvent(new CompositionEvent('compositionstart',{bubbles:true}));el.value='产品';el.dispatchEvent(new InputEvent('input',{bubbles:true,isComposing:true}));});assert.equal(await page.locator('#archive-table tbody tr').count(),6);
      await page.locator('#file-search').evaluate(el=>el.dispatchEvent(new CompositionEvent('compositionend',{bubbles:true})));assert.equal(await page.locator('#archive-table tbody tr').count(),2);await action('clear-search').click();
    });
    await check('图片只读预览与缩放',async()=>{
      await page.locator('[data-sort="name"]').click();
      await page.locator('[data-select="images"]').check();assert(await action('preview-selected').isDisabled());await page.locator('[data-select="images"]').uncheck();
      await page.locator('[data-action="file"][data-id="cover"]').click();
      await page.locator('#preview-image').waitFor();assert(await page.locator('#preview-image').evaluate(e=>e.naturalWidth===1600));
      await shot('11-图片预览');await axe('image-preview-light');
      await page.locator('#preview-zoom').selectOption('100');assert.equal(await page.locator('#preview-image').evaluate(e=>e.getBoundingClientRect().width),1600);
      await page.locator('#preview-zoom').selectOption('50');assert.equal(await page.locator('#preview-image').evaluate(e=>e.getBoundingClientRect().width),800);
      await page.locator('#preview-zoom').selectOption('fit');
    });
    await check('文本预览、查找、编码与纯文本安全显示',async()=>{
      await action('preview-next').click();await page.locator('.text-view').waitFor();
      assert.match(await page.locator('#dialog-title').innerText(),/README/);assert.equal(await page.locator('.text-view archive').count(),0);assert.match(await page.locator('.text-view').innerText(),/<archive name=/);
      await shot('12-文本预览');await axe('text-preview-light');
      await page.locator('#preview-find').fill('预览');assert(await page.locator('mark').count()>0);
      await page.locator('#preview-find').fill('完全不存在的内容');assert.match(await page.locator('#preview-matches').innerText(),/没有找到/);
      await action('preview-clear-find').click();assert.equal(await page.locator('#preview-find').inputValue(),'');
      await action('preview-wrap').click();assert.equal(await action('preview-wrap').getAttribute('aria-pressed'),'false');
      await page.locator('#preview-encoding').selectOption('GB18030');assert.match(await page.locator('#preview-metadata').innerText(),/GB18030/);
      await page.keyboard.press('Alt+ArrowLeft');await page.keyboard.press('Alt+ArrowRight');await page.locator('.text-view').waitFor();assert.match(await page.locator('#dialog-title').innerText(),/README/);
      await action('preview-fail').click();assert(await page.getByRole('heading',{name:'无法预览这个文件'}).isVisible());await action('preview-retry').click();await page.locator('.text-view').waitFor();
      await page.keyboard.press('Escape');assert.equal(await page.evaluate(()=>document.activeElement.dataset.id),'cover');
    });
    await check('预览搜索范围、不支持格式及过大图片',async()=>{
      await page.locator('#file-search').fill('README');await page.locator('[data-select="readme"]').check();await action('preview-selected').click();await page.locator('.text-view').waitFor();assert(await action('preview-next').isDisabled());assert(await action('preview-prev').isDisabled());await page.keyboard.press('Escape');await action('clear-search').click();
      await page.locator('[data-action="file"][data-id="pdf"]').click();await page.getByRole('heading',{name:'暂不支持这种文件的内置预览'}).waitFor();await action('preview-extract').click();assert.match(await page.locator('.source-summary').innerText(),/选中的 1/);await page.keyboard.press('Escape');
      await page.locator('[data-action="file"][data-id="images"]').click();await page.locator('[data-action="file"][data-id="hero"]').click();await page.getByRole('heading',{name:'图片超过快速预览限制'}).waitFor();await page.keyboard.press('Escape');await action('root').click();
    });
    await check('读取失败与恢复',async()=>{await action('load-error').click();assert(await page.getByRole('heading',{name:'无法读取压缩包'}).isVisible());await action('retry-load').click();assert.equal(await page.locator('#archive-table tbody tr').count(),6);});
    await action('create').click();await shot('03-新建压缩');await axe('create-dialog');
    await check('模态 Tab 隔离、Escape 与焦点恢复',async()=>{for(let i=0;i<18;i++){await page.keyboard.press('Tab');assert(await page.evaluate(()=>document.querySelector('dialog').contains(document.activeElement)));}await page.keyboard.press('Escape');assert.equal(await page.locator('dialog').evaluate(e=>e.open),false);assert.equal(await page.evaluate(()=>document.activeElement.dataset.action),'create');});
    await action('create').click();
    await check('表单错误、密码显示、无密码草稿恢复',async()=>{
      await page.locator('#archive-name').fill('');await page.locator('dialog button[type="submit"]').click();assert.equal(await page.locator('#archive-name').getAttribute('aria-invalid'),'true');
      await page.locator('#archive-name').fill('CON.7z');await page.locator('dialog button[type="submit"]').click();assert.match(await page.locator('#archive-name-error').innerText(),/保留/);
      await page.locator('#archive-name').fill('交互验收.7z');await page.locator('#level').selectOption('ultra');await page.locator('#password').fill('prototype-only');assert.equal(await page.locator('#encrypt-names').isDisabled(),false);await action('show-password').click();assert.equal(await page.locator('#password').getAttribute('type'),'text');await page.keyboard.press('Escape');await action('create').click();assert.equal(await page.locator('#archive-name').inputValue(),'交互验收.7z');assert.equal(await page.locator('#level').inputValue(),'ultra');assert.equal(await page.locator('#password').inputValue(),'');
    });
    await page.locator('dialog button[type="submit"]').click();
    await check('任务创建、暂停、继续、跨页面运行与取消',async()=>{
      assert.equal(await page.locator('.task-card').count(),1);await action('pause-task').click();const paused=await page.locator('[role="progressbar"]').getAttribute('aria-valuenow');await page.waitForTimeout(700);assert.equal(await page.locator('[role="progressbar"]').getAttribute('aria-valuenow'),paused);await action('pause-task').click();await page.locator('[data-page="home"]').click();await page.waitForTimeout(700);await page.locator('[data-page="tasks"]').click();assert(Number(await page.locator('[role="progressbar"]').getAttribute('aria-valuenow'))>Number(paused));await action('cancel-task').click();assert.equal(await page.evaluate(()=>document.activeElement.textContent.trim()),'返回');await page.keyboard.press('Escape');await action('cancel-task').click();await page.locator('dialog button[type="submit"]').click();assert.match(await page.locator('[data-task-status]').innerText(),/取消/);
    });
    await check('失败任务修改配置并重试',async()=>{await action('failure-task').click();await action('retry-task').click();await page.locator('#retry-destination').fill('D:\\工作\\归档');await page.locator('dialog button[type="submit"]').click();await action('pause-task').click();assert.match(await page.locator('.task-card').first().innerText(),/D:\\工作\\归档/);});
    await shot('05-任务中心');await axe('tasks-light');
    await page.locator('[data-page="archive"]').click();await action('extract').click();await shot('04-解压到');await axe('extract-dialog');
    await check('原生选择器键盘操作与解压校验',async()=>{await page.locator('#conflict-policy').focus();await page.keyboard.press('Alt+ArrowDown');await page.keyboard.press('Escape');await page.locator('#conflict-policy').selectOption('rename');await page.locator('#extract-destination').fill('');await page.locator('dialog button[type="submit"]').click();assert.equal(await page.locator('#extract-destination').getAttribute('aria-invalid'),'true');await page.locator('#extract-destination').fill('D:\\解压');});
    await action('conflict-demo').click();await shot('08-同名冲突');await axe('conflict-dialog');await page.locator('[data-choice="保留两者"]').click();await page.locator('dialog button[type="submit"]').click();assert.equal(await page.locator('.task-card').count(),3);
    await check('模拟任务完成',async()=>{await page.waitForFunction(()=>document.querySelector('[data-task="3"] [data-task-status]')?.textContent==='已完成',{},{timeout:18000});await page.locator('[data-action="task-result"]').first().click();assert(await page.getByRole('heading',{name:'演示已完成'}).isVisible());await action('close-dialog').click();});
    await page.locator('[data-page="settings"]').click();await page.locator('[name="theme"][value="light"]').check();await shot('06-外观与图标-浅色');await axe('settings-light');
    await check('图标导入、错误保留与恢复',async()=>{
      await page.locator('#icon-picker').setInputFiles(path.join(root,'图标','logo.png'));await page.waitForFunction(()=>document.querySelector('.brand-image').src.startsWith('blob:'));assert.equal(await action('reset-icon').isDisabled(),false);
      const before=await page.locator('.brand-image').first().getAttribute('src');await page.locator('#icon-picker').setInputFiles({name:'bad.png',mimeType:'image/png',buffer:Buffer.from('bad image')});await page.waitForFunction(()=>document.querySelector('#icon-error').textContent.length>0);assert.equal(await page.locator('.brand-image').first().getAttribute('src'),before);await action('reset-icon').click();assert.match(await page.locator('.brand-image').first().getAttribute('src'),/logo.png/);
    });
    await page.locator('[name="theme"][value="dark"]').check();await shot('07-外观与图标-深色');await axe('settings-dark');
    await page.locator('[data-page="archive"]').click();await shot('09-压缩包浏览-深色');await axe('archive-dark');
    await page.locator('[data-action="file"][data-id="readme"]').click();await page.locator('.text-view').waitFor();await shot('13-文本预览-深色');await axe('text-preview-dark');await page.keyboard.press('Escape');
    await check('窄视口各页面及短视口弹窗',async()=>{
      await page.setViewportSize({width:390,height:844});for(const p of ['home','archive','tasks','settings']){await page.locator(`[data-page="${p}"]`).click();assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));}await shot('10-窄视口');
      await page.locator('[data-page="home"]').click();await action('create').click();await page.setViewportSize({width:390,height:500});const footer=await page.locator('.dialog-footer').boundingBox();assert(footer.y+footer.height<=500);await page.keyboard.press('Escape');
      await page.locator('[data-page="archive"]').click();await page.locator('[data-action="file"][data-id="cover"]').click();await page.locator('#preview-image').waitFor();const previewFooter=await page.locator('.dialog-footer').boundingBox();assert(previewFooter.y+previewFooter.height<=500);await page.keyboard.press('Escape');
    });
    await check('减少动态效果与全局滚动条',async()=>{await page.emulateMedia({reducedMotion:'reduce'});assert.equal(await page.locator('.page').evaluate(e=>getComputedStyle(e).animationName),'none');assert.notEqual(await page.locator('body').evaluate(e=>getComputedStyle(e).scrollbarColor),'auto');});
    await page.setViewportSize({width:1440,height:960});await page.locator('[data-page="home"]').click();await check('清空历史与空状态',async()=>{await action('clear-history').click();await page.locator('dialog button[type="submit"]').click();assert(await page.getByRole('heading',{name:'还没有最近记录'}).isVisible());});
    await check('本地直接打开与图库完整性',async()=>{
      const {pathToFileURL}=require('node:url');
      await page.goto(pathToFileURL(path.join(root,'prototype/index.html')).href);await action('open').click();await page.locator('[data-action="file"][data-id="cover"]').click();await page.locator('#preview-image').waitFor();await page.keyboard.press('Escape');
      await page.goto(pathToFileURL(path.join(root,'docs/原型总览.html')).href);await page.waitForFunction(()=>Array.from(document.images).every(i=>i.complete&&i.naturalWidth>0));assert.equal(await page.locator('figure').count(),13);
      const prior=JSON.parse(fs.readFileSync(path.join(root,'docs/local-preview-check.json'),'utf8'));fs.writeFileSync(path.join(root,'docs/local-preview-check.json'),JSON.stringify({...prior,directFilePreview:'pass',galleryImages:13},null,2));
    });
    assert.deepEqual(errors,[],'browser errors');records.push({name:'浏览器运行时错误',result:'pass'});
    fs.writeFileSync(path.join(output,'browser-results.json'),JSON.stringify({date:'2026-11-03',browser:await browser.version(),checks:records,screenshots:fs.readdirSync(output).filter(n=>n.endsWith('.png'))},null,2));
    console.log(JSON.stringify({checks:records.length,failed:0,browser:await browser.version(),screenshots:output},null,2));
  } catch(error){await page.screenshot({path:path.join(output,'failure.png'),fullPage:true});console.error(error);process.exitCode=1;}
  finally {await browser.close();server.close();}
})();
