const pending=new Map();
let busyCount=0;
function showBusy(){
  let notice=document.querySelector('#offline-operation-status');
  if(!notice){ notice=document.createElement('div');notice.id='offline-operation-status';notice.setAttribute('role','status');notice.textContent='מעבד נתונים במסד המקומי…';document.body.append(notice); }
  notice.hidden=false;
}
window.chrome.webview.addEventListener('message',event=>{
  const response=event.data, request=pending.get(response.id);
  if(!request)return;
  pending.delete(response.id);
  clearTimeout(request.timer);
  clearTimeout(request.timeout);
  if(request.visible){busyCount=Math.max(0,busyCount-1);if(!busyCount)document.querySelector('#offline-operation-status')?.setAttribute('hidden','');}
  response.ok?request.resolve(response.data):request.reject(new Error(response.error||'הפעולה נכשלה'));
});
export function call(action,payload={}){
  return new Promise((resolve,reject)=>{
    const id=crypto.randomUUID();
    const request={resolve,reject,visible:false,timer:0,timeout:0};
    request.timer=setTimeout(()=>{if(!pending.has(id))return;request.visible=true;busyCount++;showBusy();},500);
    request.timeout=setTimeout(()=>{
      if(!pending.delete(id))return;
      clearTimeout(request.timer);
      if(request.visible){busyCount=Math.max(0,busyCount-1);if(!busyCount)document.querySelector('#offline-operation-status')?.setAttribute('hidden','');}
      reject(new Error('הפעולה מתארכת מעבר לצפוי. בדוק את קובץ Access ונסה שוב.'));
    },30000);
    pending.set(id,request);
    window.chrome.webview.postMessage({id,action,payload});
  });
}
export function observe(action,payload,change,error){
  let last=null,disposed=false,busy=false;
  const refresh=async()=>{if(disposed||busy)return;busy=true;try{
    const value=await call(action,payload), serialized=JSON.stringify(value);
    if(serialized!==last){last=serialized;change(value);}
  }catch(e){error?.(e);}finally{busy=false;}};
  void refresh();const timer=setInterval(()=>{if(!document.hidden)void refresh();},3500);
  const visible=()=>{if(!document.hidden)void refresh();};document.addEventListener('visibilitychange',visible);
  return()=>{disposed=true;clearInterval(timer);document.removeEventListener('visibilitychange',visible);};
}
