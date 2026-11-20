const pending=new Map();
window.chrome.webview.addEventListener('message',event=>{
  const response=event.data, request=pending.get(response.id);
  if(!request)return;
  pending.delete(response.id);
  response.ok?request.resolve(response.data):request.reject(new Error(response.error||'הפעולה נכשלה'));
});
export function call(action,payload={}){
  return new Promise((resolve,reject)=>{
    const id=crypto.randomUUID();pending.set(id,{resolve,reject});
    window.chrome.webview.postMessage({id,action,payload});
  });
}
export function observe(action,payload,change,error){
  let last=null,disposed=false,busy=false;
  const refresh=async()=>{if(disposed||busy)return;busy=true;try{
    const value=await call(action,payload), serialized=JSON.stringify(value);
    if(serialized!==last){last=serialized;change(value);}
  }catch(e){error?.(e);}finally{busy=false;}};
  void refresh();const timer=setInterval(refresh,1800);
  return()=>{disposed=true;clearInterval(timer);};
}
