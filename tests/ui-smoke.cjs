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
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(_name,listener){listeners.push(listener);},
    postMessage(request){
      let data;
      switch(request.action){
        case 'login': data={name:'רועי סויסה',role:new URLSearchParams(location.search).get('role')==='mechanic'?'mechanic':'manager'};break;
        case 'home': data={name:'רועי סויסה',uid:'manager-1',email:'9862436',role:location.pathname.includes('/mechanic/')?'mechanic':'manager'};break;
        case 'offline.list': case 'offline.technicians': case 'offline.constants': data=[];break;
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
    browser=await chromium.launch({channel:'msedge',headless:true});
    const page=await browser.newPage();
    await page.addInitScript(mock);
    const errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    page.on('response',r=>{if(r.status()>=400)errors.push(`${r.status()} ${r.url()}`);});
    await page.goto(`${origin}/index.html?role=manager`);
    await page.locator('#number').fill('9862436');
    await page.locator('#password').fill('-1-1-1');
    await page.locator('#login-form button').click();
    await page.waitForURL('**/Metis/index.html');
    await page.locator('main:not([hidden])').waitFor();
    assert.match(await page.locator('body').innerText(),/פתיחת תקלה|ניהול/);
    await page.goto(`${origin}/Metis/index.html#reports`);
    await page.locator('main:not([hidden])').waitFor();
    await page.goto(`${origin}/Metis/index.html#keys`);
    await page.locator('main:not([hidden])').waitFor();
    await page.goto(`${origin}/index.html?role=mechanic`);
    await page.locator('#number').fill('1234567');
    await page.locator('#password').fill('123456');
    await page.locator('#login-form button').click();
    await page.waitForURL('**/mechanic/index.html');
    await page.locator('main:not([hidden])').waitFor();
    assert.match(await page.locator('body').innerText(),/תקלות|מפתחות/);
    assert.deepEqual(errors,[],`Browser errors: ${errors.join('\n')}`);
    console.log('Manager login, core pages, mechanic login and assets passed');
  } finally { await browser?.close();server.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
