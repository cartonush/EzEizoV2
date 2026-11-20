(() => {
  'use strict';
  const role = new URLSearchParams(location.search).get('role') === 'mechanic' ? 'mechanic' : 'manager';
  const app = document.querySelector('#app'), nav = document.querySelector('#nav');
  const back = document.querySelector('#back'), logout = document.querySelector('#logout');
  const pending = new Map();
  let account = null, page = 'login', previous = 'home', tickets = [], current = null;
  const actions = [
    'חתום על כלים רלוונטיים ושג לנקודת עבודה','זמן הגעה לדת״ק',
    'קבל אישור עבודה על המטוס מר״צ חימוש / מפקד דת״ק',
    'בצע ביקורת כלים עם ר״צ / מפקד תורן - קוד שכיח 310',
    'וודא מטוס בטוח לאחזקה לפי כרך בטיחות המטוס',
    'לפתרון התקלה פעל עפ״י תהליך מרכזי דרג א׳',
    'עדכן ר״צ / מפקד דת״ק בסטטוס המטוס',
    'בצע ביקורת כלים בסיום העבודה - קוד שכיח 310',
    'בצע בדיקה להיעדר גופים זרים במקום העבודה - קוד שכיח 3100',
    'זכה את הכלים שנלקחו מהגף',
    'הצמד תג בר תיקון למכלול / צב״ד תקול', 'זמן סיום התקלה'
  ];
  const aircraft = {
    '101':'101,502,503,506,511,512,514,516,519,520,522,523,527,528,530,531,524,536,538,539,542,543,546,547,551,554,557,558',
    '105':'601,603,610,615,619,624,628,630,633,638,642,647,648,651,652,656,664,666,667,673,676,678,682,687',
    '109':'020,022,023,027,034,036,039,041,045,046,050,055,057,061,063,069,072,074,075,079,083,088,612,637',
    'מנ״ט':'000'
  };
  function escape(value) { return String(value ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
  function call(action,payload={}) {
    return new Promise((resolve,reject)=>{
      const id = crypto.randomUUID(); pending.set(id,{resolve,reject});
      window.chrome.webview.postMessage({id,action,payload});
    });
  }
  window.chrome.webview.addEventListener('message', event=>{
    const result = event.data, request = pending.get(result.id);
    if (!request) return;
    pending.delete(result.id);
    result.ok ? request.resolve(result.data) : request.reject(new Error(result.error || 'הפעולה נכשלה'));
  });
  function loading(){ app.innerHTML='<section class="card"><div class="skeleton"></div><div class="skeleton short"></div><div class="skeleton"></div></section>'; }
  function message(error){ const box=app.querySelector('#message'); if(box){ box.textContent=error?.message || String(error); box.hidden=false; } }
  function heading(kicker,title){ return `<div class="section-heading"><div><p class="section-kicker">${kicker}</p><h2>${title}</h2></div><span class="step-badge">01</span></div>`; }
  function shell(target){ page=target; nav.hidden=role!=='manager'||!account; logout.hidden=!account; back.hidden=target==='login'||target==='home'||['new','open','closed','users'].includes(target)&&role==='manager';
    nav.querySelectorAll('button').forEach(button=>button.setAttribute('aria-current',button.dataset.page===target?'page':'false'));
  }
  function login(){ account=null; shell('login'); app.innerHTML=`<section class="card auth-card">${heading('גישה למערכת',role==='manager'?'כניסה למנהל':'כניסה לטכנאי')}<p>התחבר באמצעות מספר אישי בן 7 ספרות.</p><form id="login-form"><label class="field-label" for="number">מספר אישי</label><input id="number" inputmode="numeric" pattern="[0-9]{7}" maxlength="7" required autocomplete="off" autofocus><div class="toolbar"><button class="primary-button">התחבר</button></div><p class="status" id="message" hidden></p></form></section>`;
    app.querySelector('form').onsubmit=async e=>{e.preventDefault(); try{account=await call('login',{number:app.querySelector('#number').value}); location.href = role === 'manager' ? './Metis/' : './mechanic/';}catch(err){message(err);} };
  }
  async function home(){ shell('home');
    if(role==='mechanic'){await list('open');return;}
    loading(); try{ tickets=await call('faults'); const count=s=>tickets.filter(x=>x.status===s).length;
      app.innerHTML=`<section class="card manager-home-intro">${heading('מערכת ניהול','שלום '+escape(account.name))}<p>דף מנהל מקומי · הנתונים נשמרים בתיקיית data במחשב.</p></section><div class="manager-home-grid"><button class="manager-home-tile" data-go="new"><strong>פתיחת תקלה חדשה</strong><span>יצירת משימה והקצאת טכנאים</span></button><button class="manager-home-tile" data-go="open"><strong>תקלות פתוחות</strong><span>${count('open')} טפסים פתוחים</span></button><button class="manager-home-tile" data-go="closed"><strong>תקלות סגורות</strong><span>${count('closed')} טפסים סגורים · ${count('pending_review')} ממתינים לסגירה</span></button><button class="manager-home-tile" data-go="users"><strong>ניהול משתמשים</strong><span>יצירת חשבונות טכנאים</span></button></div>`;
      app.querySelectorAll('[data-go]').forEach(b=>b.onclick=()=>navigate(b.dataset.go));
    }catch(err){app.innerHTML=`<section class="card"><p class="status">${escape(err.message)}</p></section>`;}
  }
  async function users(){ shell('users'); loading(); try { const users=await call('users');
    app.innerHTML=`<section class="card">${heading('ניהול','משתמשים')}<p>המנהל הראשי יוצר חשבונות טכנאים. חשבון המנהל הקיים נשמר במסד הנתונים.</p><form id="user-form" class="form-grid"><label>שם הטכנאי<input name="name" maxlength="100" required></label><label>מספר אישי<input name="number" inputmode="numeric" pattern="[0-9]{7}" maxlength="7" required></label><div class="full-field"><button class="primary-button">צור טכנאי</button></div></form><p class="status" id="message" hidden></p></section><section class="card">${heading('צוות','טכנאים פעילים')}${users.length?users.map(x=>`<div class="issue-card"><strong>${escape(x.name)}</strong><span>מספר אישי: ${escape(x.personalNumber)}</span></div>`).join(''):'<p class="empty">אין טכנאים פעילים.</p>'}</section>`;
    app.querySelector('form').onsubmit=async e=>{e.preventDefault();try{await call('createUser',Object.fromEntries(new FormData(e.target)));await users();}catch(err){message(err);}};
  }catch(err){app.innerHTML=`<section class="card"><p class="status">${escape(err.message)}</p></section>`;} }
  async function newFault(){shell('new');loading();try{const users=await call('users');
    app.innerHTML=`<section class="card">${heading('דף חדש','פתיחת תקלה')}<form id="new-form"><div class="form-grid"><label>טייסת<select name="squadron" id="squadron" required><option value="">בחר טייסת</option>${Object.keys(aircraft).map(x=>`<option>${escape(x)}</option>`).join('')}</select></label><label>מספר מטוס<select name="aircraft" id="aircraft" required><option value="">ראשית בחר טייסת</option></select></label><label class="full-field">תיאור תקלה<textarea name="description" required></textarea></label></div><fieldset class="assignee-fieldset"><legend>הקצאה לטכנאים · לפחות אחד</legend>${users.map((x,i)=>`<label class="assignee"><input type="checkbox" name="assignee" value="${escape(x.id)}"><span>${i===0?'מוביל התקלה · ':''}${escape(x.name)}</span></label>`).join('')||'<p class="empty">צור טכנאי בניהול משתמשים כדי לפתוח תקלה.</p>'}</fieldset><button class="primary-button">פתח תקלה</button><p class="status" id="message" hidden></p></form></section>`;
    app.querySelector('#squadron').onchange=e=>{let options=(aircraft[e.target.value]||'').split(',').filter(Boolean); app.querySelector('#aircraft').innerHTML=`<option value="">בחר מטוס</option>`+options.map(x=>`<option>${x}</option>`).join('');};
    app.querySelector('form').onsubmit=async e=>{e.preventDefault();const f=new FormData(e.target), assignees=f.getAll('assignee');try{await call('createFault',{squadron:f.get('squadron'),aircraft:f.get('aircraft'),description:f.get('description'),assignees});await list('open');}catch(err){message(err);}};
  }catch(err){app.innerHTML=`<section class="card"><p class="status">${escape(err.message)}</p></section>`;} }
  async function list(kind){shell(kind);loading();try{tickets=await call('faults');const subset=tickets.filter(t=>role==='mechanic' ? t.status==='open' : kind==='open'?t.status==='open':t.status!=='open');
    app.innerHTML=`<section class="card queue-card">${heading('רשימת תקלות',role==='mechanic'?'תקלות שהתקבלו':kind==='open'?'תקלות פתוחות':'תקלות סגורות וממתינות לסגירה')}<p class="section-note">בחר טופס כדי לצפות בפרטים שלו.</p><label class="field-label" for="search">חיפוש לפי מספר סידורי</label><input id="search" inputmode="numeric" placeholder="מספר תקלה"><div id="results"></div></section>`;
    const draw=()=>{let q=app.querySelector('#search').value.replace(/^0+/,''), filtered=subset.filter(t=>!q||t.serial.replace(/^0+/,'').includes(q)); app.querySelector('#results').innerHTML=filtered.map(t=>`<button class="issue-card" data-id="${escape(t.id)}"><strong>תקלה ${escape(t.serial)} · מטוס ${escape(t.aircraft)} · ${escape(t.squadron)}</strong><span>${escape(t.description)} · ${t.status==='open'?'פתוח':t.status==='closed'?'סגור':'ממתין לסגירה'}</span></button>`).join('')||'<p class="empty">אין תקלות להצגה.</p>';
      app.querySelectorAll('[data-id]').forEach(b=>b.onclick=()=>showFault(subset.find(x=>x.id===b.dataset.id),kind));};
    app.querySelector('#search').oninput=draw; draw();
  }catch(err){app.innerHTML=`<section class="card"><p class="status">${escape(err.message)}</p></section>`;} }
  async function showFault(ticket,origin){ current=ticket; previous=origin; shell('fault'); loading();try{const outcomes=await call('outcomes',{id:ticket.id}); const editable=role==='mechanic'&&ticket.status==='open';
    app.innerHTML=`<section class="card">${heading('טופס תקלה', 'תקלה '+escape(ticket.serial))}<div class="details-grid"><div class="detail-item"><span>טייסת</span><strong>${escape(ticket.squadron)}</strong></div><div class="detail-item"><span>מטוס</span><strong>${escape(ticket.aircraft)}</strong></div><div class="detail-item"><span>סטטוס</span><strong>${ticket.status==='open'?'פתוח':ticket.status==='closed'?'סגור':'ממתין לסגירה'}</strong></div><div class="detail-item"><span>שעת פתיחה</span><strong>${escape(ticket.openedAt)}</strong></div></div></section><section class="card descriptions-card">${heading('תיאור','התקלה והתיקון')}<label class="field-label">תיאור תקלה <span class="locked-tag">לקריאה בלבד</span></label><textarea class="text-box locked" readonly>${escape(ticket.description)}</textarea><label class="field-label repair-label">תיאור תיקון</label><textarea class="text-box" id="repair" ${editable?'':'readonly'}>${escape(ticket.repair)}</textarea></section><section class="card">${heading('תהליך העבודה','פעולות לביצוע')}<ol class="action-list">${actions.map((a,i)=>`<li class="action-row"><span class="action-number">${String(i+1).padStart(2,'0')}</span><span class="action-text">${escape(a)}</span>${i===10?`<span class="action-choices"><label class="action-choice"><input type="radio" name="action11" value="V" ${outcomes[i]==='V'?'checked':''} ${editable?'':'disabled'}>V</label><label class="action-choice choice-x"><input type="radio" name="action11" value="X" ${outcomes[i]==='X'?'checked':''} ${editable?'':'disabled'}>X</label></span>`:`<input class="action-check" type="checkbox" data-action="${i}" ${outcomes[i]?'checked':''} ${editable?'':'disabled'}>`}</li>`).join('')}</ol></section><section class="card"><div class="toolbar">${editable?'<button class="secondary-button" id="save">שמור</button><button class="primary-button" id="close">שמירה וסגירה</button>':''}${role==='manager'&&ticket.status==='pending_review'?'<button class="primary-button" id="review">אשר סגירת תקלה</button>':''}</div><p class="status" id="message" hidden></p></section>`;
    const save=async close=>{try{const values=actions.map((_,i)=>i===10?(app.querySelector('input[name=action11]:checked')?.value||''):app.querySelector(`[data-action="${i}"]`).checked?'V':'');await call('saveFault',{id:ticket.id,repair:app.querySelector('#repair').value,outcomes:values,close});await list('open');}catch(err){message(err);}};
    if(editable){app.querySelector('#save').onclick=()=>save(false);app.querySelector('#close').onclick=()=>save(true);}
    if(role==='manager'&&ticket.status==='pending_review')app.querySelector('#review').onclick=async()=>{try{await call('reviewFault',{id:ticket.id});await list('closed');}catch(err){message(err);}};
  }catch(err){app.innerHTML=`<section class="card"><p class="status">${escape(err.message)}</p></section>`;} }
  async function navigate(target){ if(!account){login();return;} if(target==='home')return home(); if(target==='new')return newFault(); if(target==='users')return users(); if(target==='open'||target==='closed')return list(target); }
  nav.querySelectorAll('[data-page]').forEach(button=>button.onclick=()=>navigate(button.dataset.page));
  back.onclick=()=>current?list(previous):home();
  logout.onclick=async()=>{try{await call('logout');}finally{login();}};
  if(role==='mechanic'){document.querySelector('#header').classList.remove('manager-topbar');document.querySelector('#mark').textContent='ט';document.querySelector('#brand').textContent='גף אוויוניקה קרב';document.querySelector('#subtitle').textContent='דף טיפול בתקלה';}
  else document.querySelector('#header').classList.add('manager-topbar');
  login();
})();
