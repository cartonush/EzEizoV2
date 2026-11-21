// Offline adapter for the original website UI. Every persisted change goes through
// the desktop host into the local Access database. There is no Firebase import.
import {call,observe} from './offline-bridge.js';
const iso=()=>new Date().toISOString();
const list=(collection,filter)=>call('offline.list',{collection,filter});
const update=(collection,id,operation,extra={})=>call('offline.update',{collection,id,operation,...extra});
const wrapLock=value=>value?{...value,expiresAt:{toMillis:()=>value.expiresAt}}:null;
let user=null;
export async function gate(role){
  const data=await call('home');
  if(data.role!==(role==='manager'?'manager':'mechanic'))throw new Error('אין הרשאה למסך זה.');
  user={uid:data.uid,name:data.name,email:data.email};
  const logout=document.createElement('button');logout.className='secondary-button auth-logout';logout.type='button';logout.textContent='התנתק';
  logout.onclick=async()=>{await call('logout');location.href=location.pathname.includes('/Metis/')?'../index.html?role=manager':'../index.html?role=mechanic';};
  document.querySelector('.header-actions')?.append(logout);
  return user;
}
export function revealApp(){document.querySelector('#app-skeleton')?.remove();document.querySelector('main').hidden=false;}
export function watchIssues(onChange,onError,technicianUid=null){return observe('offline.list',{collection:'issues'},items=>onChange(technicianUid?items.filter(x=>x.assigneeUid===technicianUid):items),onError);}
export function watchKeyLoads(onChange,onError,technicianUid=null){return observe('offline.list',{collection:'keyLoads'},items=>onChange(technicianUid?items.filter(x=>(x.teamUids||[]).includes(technicianUid)):items),onError);}
export function watchTechnicians(onChange,onError){return observe('offline.technicians',{},onChange,onError);}
export function watchAircraftConstants(onChange,onError){return observe('offline.constants',{},onChange,onError);}
export function watchKeyHistory(onChange,onError){return observe('offline.list',{collection:'keyLoads'},onChange,onError);}
export async function saveAircraftConstants(squadron,aircraft,constants){await call('offline.saveConstants',{squadron,aircraft,constants});}
export function getIssue(id){return call('offline.get',{collection:'issues',id});}
export function createAssignedIssues(sheet,assignees){return call('offline.createIssues',{sheet,assignees});}
export function createIssue(sheet,assignee){return createAssignedIssues(sheet,[assignee]);}
export function createKeyLoad(load){return call('offline.createKey',{load});}
export async function assignLegacyKeyLoadSerial(id){return (await update('keyLoads',id,'serial')).serial;}
export function closeKeyLoad(id,completedBy,work,session){return update('keyLoads',id,'complete',{work,session});}
export function saveKeyWorkDraft(id,uid,work,session){return update('keyLoads',id,'draft',{work,session});}
export function appendKeyLoadManagerNote(id,note){return update('keyLoads',id,'note',{note});}
export function reopenKeyLoad(id){return update('keyLoads',id,'reopen');}
export function submitForReview(id,record,state,session){return update('issues',id,'submit',{record,state,session});}
export function approveIssue(id,reviewerUid,signature){return update('issues',id,'approve',{signature});}
export function reopenIssue(id){return update('issues',id,'reopen');}
export function appendIssueManagerNote(id,note){return update('issues',id,'note',{note});}
export function assignIssue(id,uid,name){return update('issues',id,'assign',{uid,name});}
export function createTechnician(name,number,password){return call('offline.createTechnician',{name,number,password});}
export function changeTechnicianPassword(uid,password){return call('offline.changeTechnicianPassword',{uid,password});}
export function setTechnicianActive(uid,active){return call('offline.setTechnicianActive',{uid,active});}
export function deleteTechnicianPermanently(uid){return call('offline.deleteTechnician',{uid});}
export function watchEditLock(kind,serialOrId,onChange,onError){
  // The issue serial is shared by assignees; the lock is keyed by the form ID.
  const id=kind==='key'?serialOrId:location.search?new URLSearchParams(location.search).get('id'):serialOrId;
  return observe('offline.readLock',{id},lock=>onChange(wrapLock(lock)),onError);
}
export function acquireEditLock(kind,serialOrId,formId,user,session){return call('offline.lock',{collection:kind==='key'?'keyLoads':'issues',id:formId,session});}
export function releaseEditLock(kind,serialOrId,session){
  const id=kind==='key'?serialOrId:new URLSearchParams(location.search).get('id');
  return call('offline.releaseLock',{id,session});
}
