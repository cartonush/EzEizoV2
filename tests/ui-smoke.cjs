// Run on the Windows build runner with Edge. Exercises actual page navigation and core screens.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../wwwroot');
const types = { '.html':'text/html', '.js':'text/javascript', '.css':'text/css', '.svg':'image/svg+xml', '.json':'application/json', '.png':'image/png', '.webmanifest':'application/manifest+json' };
const server = http.createServer((req,res)=>{
  const target = path.resolve(root, '.' + decodeURIComponent(new URL(req.url,'http://localhost').pathname));
  if (!target.startsWith(root + path.sep) && target !== root) { res.writeHead(403).end(); return; }
  fs.readFile(target,(err,bytes)=>{
    if (err) { res.writeHead(404).end('Missing asset: ' + target); return; }
    res.writeHead(200,{'content-type':(types[path.extname(target)]||'application/octet-stream')+'; charset=utf-8'}).end(bytes);
  });
});
const mock = () => {
  const listeners=[];
  const issue={id:'test-issue',status:'open',assigneeUid:'mechanic-1',assigneeUids:['mechanic-1'],createdAt:'2026-11-21T08:00:00Z',sheet:{id:'test-issue',serial:'0000000001',squadron:'105',shelter:'14',location:'14/105',aircraft:'603',systemCode:'74A',systemName:'מכם',ewSystem:'גרניט',fault:'בדיקת ניווט',actions:[]}};
  const keyLoad={id:'test-key',serial:'K-000000001',status:'open',teamUids:['mechanic-1','second'],teamNames:['טכנאי','בודק'],date:'2026-11-21',time:'08:00',squadron:'105',shelter:'14',period:'11-12/26',aircraftIds:['603'],rows:[{aircraft:'603',blue:true,shach:true,notes:''}],dispatcherSignature:'data:image/png;base64,aGVsbG8=',createdAt:'2026-11-21T08:00:00Z'};
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(_name,listener){listeners.push(listener);},
    postMessage(request){
      let data;
      switch(request.action){
        case 'login': data={name:'רועי סויסה',role:new URLSearchParams(location.search).get('role')==='mechanic'?'mechanic':'manager'};break;
        case 'home': data={name:'רועי סויסה',uid:location.pathname.includes('/mechanic/')?'mechanic-1':'manager-1',email:'9862436',role:location.pathname.includes('/mechanic/')?'mechanic':'manager'};break;
        case 'offline.list': data=request.payload.collection==='issues'?[issue]:request.payload.collection==='keyLoads'?[keyLoad]:[];break;
        case 'offline.version': data=1;break;
        case 'offline.get': data=issue;break;
        case 'offline.drafts': data={};break;
        case 'offline.lock': data={held:true};break;
        case 'offline.technicians': data=[{uid:'mechanic-1',name:'צוק עינב',personalNumber:'1234567',email:'1234567',active:true},{uid:'second',name:'רועי סויסה',personalNumber:'7654321',email:'7654321',active:true}];break;
        case 'offline.constants': data=[];break;
        case 'offline.readLock': data=null;break;
        default: data={};
      }
      queueMicrotask(()=>listeners.forEach(listener=>listener({data:{id:request.id,ok:true,data}})));
    }
  };
};
(async()=>{
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin=`http://127.0.0.1:${server.address().port}`;
  let browser;
  try {
    browser=await chromium.launch(process.platform==='win32'?{channel:'msedge',headless:true}:{executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
    const page=await browser.newPage();
    await page.addInitScript(mock);
    const errors=[];
    page.on('request',request=>{if(/^https?:/.test(request.url()) && new URL(request.url()).origin!==origin)errors.push(`External request: ${request.url()}`);});
    page.on('pageerror',e=>errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)errors.push(`${r.status()} ${r.url()}`);});
    await page.goto(`${origin}/index.html?role=manager`);
    await page.locator('#number').fill('9862436');
    await page.locator('#password').fill('123456');
    await page.locator('#login-form button').click();
    await page.waitForURL('**/Metis/index.html');
    await page.locator('main:not([hidden])').waitFor();
    assert.match(await page.locator('body').innerText(),/פתיחת תקלה|ניהול/);
    await page.goto(`${origin}/Metis/index.html#new`);
    await page.locator('#view-new:not([hidden])').waitFor();
    assert.doesNotMatch(await page.locator('body').innerText(),/Firestore|Firebase/);
    await page.goto(`${origin}/Metis/index.html#reports`);
    await page.locator('main:not([hidden])').waitFor();
    await page.goto(`${origin}/Metis/index.html#users`);
    await page.locator('#users-list .issue-item').first().waitFor();
    await page.locator('#users-search').fill('1234567');
    assert.equal(await page.locator('#users-list .issue-item').count(),1);
    assert.match(await page.locator('#users-list').innerText(),/צוק עינב/);
    await page.locator('#users-search').fill('רועי');
    assert.equal(await page.locator('#users-list .issue-item').count(),1);
    assert.match(await page.locator('#users-list').innerText(),/רועי סויסה/);
    await page.goto(`${origin}/Metis/index.html#keys`);
    await page.locator('main:not([hidden])').waitFor();
    await page.goto(`${origin}/index.html?role=mechanic`);
    await page.locator('#number').fill('1234567');
    await page.locator('#password').fill('123456');
    await page.locator('#login-form button').click();
    await page.waitForURL('**/mechanic/index.html');
    await page.locator('main:not([hidden])').waitFor();
    assert.match(await page.locator('body').innerText(),/תקלות|מפתחות/);
    await page.locator('a[href*="test-issue"]').click();
    await page.locator('#mechanic-back:not([hidden])').waitFor();
    await page.locator('#sheet-form:visible').waitFor();
    await page.locator('#save-sheet:visible').waitFor();
    const toolbar=await page.locator('#sheet-form .sheet-save-toolbar').evaluate(el=>{
      const a=el.querySelector('button:first-child').getBoundingClientRect(), b=el.querySelector('button:last-child').getBoundingClientRect(), label=el.querySelector('span').getBoundingClientRect();
      return {saveX:a.x,resetX:b.x,saveWidth:a.width,resetWidth:b.width,labelX:label.x,labelRight:label.right,position:getComputedStyle(el).position};
    });
    assert.equal(toolbar.position,'sticky');
    assert.ok(toolbar.saveX>toolbar.resetX && toolbar.labelX>toolbar.resetX && toolbar.labelRight<toolbar.saveX);
    assert.ok(Math.abs(toolbar.saveWidth-toolbar.resetWidth)<2);
    await page.locator('#mechanic-back').click();
    await page.waitForURL('**/mechanic/index.html');
    await page.locator('a[href*="test-issue"]').waitFor();
    await page.locator('a[href*="test-key"]').click();
    await page.locator('#save-key-draft:visible').waitFor();
    assert.equal(await page.locator('#work-view .key-save-toolbar').evaluate(el=>getComputedStyle(el).position),'sticky');
    assert.ok(await page.locator('#reset-key-work:visible').count());
    await page.locator('#save-key-draft').click();
    await page.waitForURL('**/mechanic/index.html');
    assert.deepEqual(errors,[],`Browser errors: ${errors.join('\n')}`);
    console.log('Manager login, core pages, mechanic login and assets passed');
  } finally { await browser?.close();server.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
