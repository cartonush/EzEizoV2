import { draftState } from "./access-state.js";
import "./sheet-data.js?v=1.6.3";
import { gate, revealApp, watchIssues, watchKeyLoads, getIssue, submitForReview, approveIssue, reopenIssue } from "./access-sync.js?v=1.6.3";
import { attachSignaturePad } from "./signature-canvas.js?v=1.6.3";
import { validateTreatment } from "./mechanic-validation.js?v=1.6.3";
import { attachTimeNow } from "./time-now.js?v=1.6.3";
import { issueTimeline } from "./lifecycle.js?v=1.6.3";
import { createEditLock } from "./edit-lock.js?v=1.6.3";

const managerClosedView = location.pathname.toLowerCase().endsWith("/metis/closed.html");
if (managerClosedView) document.querySelector('.sheet-save-toolbar')?.setAttribute('hidden','');
const currentUser = await gate(managerClosedView ? "manager" : "technician");
let cloudIssues = [];
let keyLoads = [];
let keyLoadError = "";
let issueLoadError = "";
let issuesReady = false;
let keyLoadsReady = false;
const selectedId = new URLSearchParams(window.location.search).get("id");
let remoteIssue = null;
if (selectedId) {
  try { remoteIssue = await getIssue(selectedId); }
  catch (error) { const list = document.querySelector("#mechanic-issues"); list.textContent = `טעינת התקלה נכשלה: ${error.message}`; list.dataset.state = "error"; }
}
const sheet = EzEizoData.normalizeSheet(remoteIssue?.sheet || EzEizoData.defaultSheet);
const selectedSheet = Boolean(remoteIssue && (managerClosedView ? ["closed", "pendingReview"].includes(remoteIssue.status) : (remoteIssue.assigneeUids || [remoteIssue.assigneeUid]).includes(currentUser.uid)));
document.querySelector("#sheet-form").hidden = !selectedSheet || (!managerClosedView && remoteIssue?.status === "open");
document.querySelector("#mechanic-back").hidden = !selectedSheet && !managerClosedView;
document.querySelector(".queue-card").hidden = selectedSheet;
const ACTIONS = sheet.actions;
const STORAGE_KEY = selectedSheet && sheet.id !== "example" ? `fault-sheet-v2:${sheet.id}` : "fault-sheet-v1";
const actionList = document.querySelector("#action-list");
const repairField = document.querySelector("#repair-description");
const unitsToggle = document.querySelector("#units-toggle");
const unitsArea = document.querySelector("#units-area");
let editLock = null;
if (selectedSheet && !managerClosedView && remoteIssue.status === "open") {
  const formSkeleton = document.createElement("section");
  formSkeleton.className = "card app-skeleton";
  formSkeleton.setAttribute("role", "status");
  formSkeleton.innerHTML = '<p>טוען טופס ונעילת עריכה…</p><div class="skeleton-card"><span class="skeleton-title"></span><span class="skeleton-line"></span><span class="skeleton-input"></span></div><div class="skeleton-card"><span class="skeleton-line"></span><span class="skeleton-input"></span></div>';
  document.querySelector("#sheet-form").before(formSkeleton);
  const notice = document.createElement("section");
  notice.className = "card edit-lock-notice";
  notice.setAttribute("role", "status");
  notice.setAttribute("aria-live", "polite");
  const message = document.createElement("strong");
  const retry = document.createElement("button"); retry.className = "secondary-button"; retry.type = "button"; retry.textContent = "בדוק שוב";
  retry.addEventListener("click", () => editLock?.acquire());
  notice.append(message, retry);
  document.querySelector("#sheet-form").before(notice);
  editLock = createEditLock({ kind: "issue", serialOrId: sheet.serial, formId: sheet.id, user: { uid: currentUser.uid, name: currentUser.name }, onChange: ({ held, message: info }) => {
    formSkeleton.hidden = held || !info.includes("בודק");
    notice.hidden = held || formSkeleton.hidden === false;
    message.textContent = info;
    document.querySelector("#sheet-form").hidden = !held;
    if (held) setupSignaturePads();
  } });
  window.addEventListener("pagehide", () => editLock?.dispose());
  void editLock.acquire();
}
const TABLES = {
  removed: [
    { key: "name", label: "שם יחידה", placeholder: "בחר יחידה", options: ["MLPRF", "PSP", "DMT"] },
    { key: "serial", label: "מס״ד", placeholder: "מס״ד" }
  ],
  installed: [
    { key: "name", label: "שם יחידה", placeholder: "בחר יחידה", options: ["MLPRF", "PSP", "DMT"] },
    { key: "serial", label: "מס״ד", placeholder: "מס״ד" }
  ],
  tools: [
    { key: "toolName", label: "שם כלי", placeholder: "שם כלי" },
    { key: "toolNumber", label: "מספר כלי", placeholder: "מספר כלי" },
    { key: "notes", label: "הערות", placeholder: "הערות", optional: true }
  ],
  taken: [
    { key: "description", label: "תיאור קשיח / נצרך", placeholder: "תיאור קשיח / נצרך" },
    { key: "removedFrom", label: "הוסר מרכיב / אזור", placeholder: "מרכיב / אזור" },
    { key: "quantityIn", label: "כמות בכניסה", placeholder: "כמות", type: "number" },
    { key: "quantityOut", label: "כמות ביציאה", placeholder: "כמות", type: "number" },
    { key: "notes", label: "הערות", placeholder: "הערות", optional: true }
  ],
  rejected: [
    { key: "description", label: "תיאור הקשיח", placeholder: "תיאור הקשיח" },
    { key: "quantity", label: "כמות", placeholder: "כמות", type: "number" },
    { key: "mascha", label: "מסחא", placeholder: "מסחא" },
    { key: "reason", label: "סיבת הפסילה", placeholder: "סיבת הפסילה" }
  ]
};
const defaultState = {
  repair: "", technicianNotes: [], actions: ACTIONS.map(() => false), action11Result: "", arrivalTime: "", completionTime: "", closedAt: "",
  unitsEnabled: false, removed: [], installed: [], constantsEnabled: false,
  toolsEnabled: false, tools: [], entryEnabled: false, interimEnabled: false, finalEnabled: false,
  takenEnabled: false, taken: [], rejectedEnabled: false, rejected: [],
  entryInspectorNumber: "", interimInspectorNumber: "", interimToolsName: "", finalInspectorNumber: "", finalLeadName: "", departureTime: "",
  takenFullName: "", takenPersonalNumber: "", takenRank: "",
  safetyTechnicianSignature: "", technicianDeclarationSignature: "",
  entryTechnicianSignature: "", entryInspectorSignature: "", interimTechnicianSignature: "", interimInspectorSignature: "", interimToolsSignature: "",
  finalTechnicianSignature: "", finalInspectorSignature: "", finalLeadSignature: "", takenSignature: ""
};
let state = loadState();
if (["closed", "pendingReview"].includes(remoteIssue?.status) && remoteIssue.closed?.state) {
  state = { ...defaultState, ...remoteIssue.closed.state };
} else if (remoteIssue?.status === "open" && remoteIssue.reopenedAt && state.reopenMarker !== remoteIssue.reopenedAt) {
  state = { ...defaultState, ...remoteIssue.closed.state, closedAt: "", reopenMarker: remoteIssue.reopenedAt };
  draftState.setItem(STORAGE_KEY, JSON.stringify(state));
}
state.completionTime = state.completionTime || "";
state.arrivalTime = state.arrivalTime || "";
if (!Array.isArray(state.actions)) state.actions = ACTIONS.map(() => false);
if (!Array.isArray(state.technicianNotes)) state.technicianNotes = [];
state.action11Result = ["done", "notRequired"].includes(state.action11Result) ? state.action11Result : state.actions[10] ? "done" : "";
state.actions[10] = Boolean(state.action11Result);
state.actions[1] = Boolean(state.arrivalTime) || Boolean(remoteIssue?.status === "closed" && state.actions[1]);
state.actions[ACTIONS.length - 1] = Boolean(state.completionTime);

function loadState() {
  try { return { ...defaultState, ...JSON.parse(draftState.getItem(STORAGE_KEY) || "{}") }; }
  catch { return { ...defaultState }; }
}

let pendingStateSave = Promise.resolve();
async function flushState() {
  if (saveState.timer) { clearTimeout(saveState.timer); saveState.timer = null; }
  await pendingStateSave;
  if (managerClosedView || state.closedAt) return;
  pendingStateSave = draftState.setItem(STORAGE_KEY, JSON.stringify(state));
  await pendingStateSave;
}
function saveState() {
  if (managerClosedView) return;
  if (state.closedAt) return;
  const status = document.querySelector("#save-state");
  status.textContent = "שומר במסד המקומי…";
  window.clearTimeout(saveState.timer);
  saveState.timer = window.setTimeout(() => {
    saveState.timer = null;
    pendingStateSave = draftState.setItem(STORAGE_KEY, JSON.stringify(state));
    pendingStateSave.then(() => { delete status.dataset.state; status.textContent = "הטיוטה נשמרה במסד המשותף"; })
      .catch((error) => { status.textContent = `שמירת הטיוטה נכשלה: ${error.message}`; status.dataset.state = "error"; });
  }, 400);
}

function renderSheetDetails() {
  if (managerClosedView) {
    const timeline = document.querySelector("#lifecycle-events");
    timeline.replaceChildren(...issueTimeline(remoteIssue).map(({ label, at }) => {
      const item = document.createElement("li");
      const name = document.createElement("strong"); name.textContent = label;
      const time = document.createElement("time"); time.dateTime = at; time.textContent = new Date(at).toLocaleString("he-IL");
      item.append(name, time); return item;
    }));
  }
  const times = document.querySelector("#closed-times");
  if (times) {
    times.hidden = !["closed", "pendingReview"].includes(remoteIssue?.status);
    if (!times.hidden) {
      const format = (value) => value ? new Date(value).toLocaleString("he-IL", { dateStyle: "short", timeStyle: "short" }) : "—";
      document.querySelector("#opened-at").textContent = format(remoteIssue.createdAt);
      document.querySelector("#closed-at").textContent = format(remoteIssue.reviewedAt || remoteIssue.closed?.state?.closedAt);
      document.querySelector("#closed-at").previousElementSibling.textContent = remoteIssue.status === "pendingReview" ? "שעת הגשת הטכנאי" : "שעת סגירה";
    }
  }
  document.querySelector("#workshop-detail").hidden = !sheet.workshop;
  document.querySelectorAll("[data-sheet-field]").forEach((element) => {
    element.textContent = sheet[element.dataset.sheetField] || "—";
  });
  document.querySelector("#fault-description").value = sheet.fault;
  document.querySelector("#dispatch-briefing").hidden = !sheet.dispatcherSignature;
  if (sheet.dispatcherSignature) document.querySelector("#dispatcher-signature-display").src = sheet.dispatcherSignature;
  const grid = document.querySelector("#constants-grid");
  grid.replaceChildren();
  Object.entries(sheet.constants).forEach(([groupName, entries]) => {
    const group = document.createElement("section");
    group.className = "constant-group";
    const heading = document.createElement("h3");
    heading.textContent = groupName;
    const list = document.createElement("dl");
    entries.forEach(([label, value]) => {
      const row = document.createElement("div");
      const term = document.createElement("dt");
      const description = document.createElement("dd");
      term.textContent = label;
      description.textContent = value;
      row.append(term, description);
      list.append(row);
    });
    group.append(heading, list);
    grid.append(group);
  });
}

function renderTechnicianNotes() {
  const box = document.querySelector("#combined-notes");
  let prefix = sheet.managerNotes ? `${sheet.managerNotes}\n\n` : "";
  box.value = prefix + state.technicianNotes.join("\n");
  box.updateManagerNotes = (notes) => {
    sheet.managerNotes = notes;
    prefix = notes ? `${notes}\n\n` : "";
    box.value = prefix + state.technicianNotes.join("\n");
  };
  box.readOnly = managerClosedView || Boolean(state.closedAt);
  box.addEventListener("beforeinput", (event) => {
    if (box.selectionStart < prefix.length) event.preventDefault();
  });
  box.addEventListener("input", () => {
    if (!box.value.startsWith(prefix)) { box.value = prefix + state.technicianNotes.join("\n"); box.setSelectionRange(box.value.length, box.value.length); return; }
    const notes = box.value.slice(prefix.length);
    state.technicianNotes = notes ? [notes] : [];
    saveState();
  });
  box.addEventListener("click", () => { if (box.selectionStart < prefix.length) box.setSelectionRange(prefix.length, prefix.length); });
}

function renderActions() {
  actionList.replaceChildren();
  ACTIONS.forEach((label, index) => {
    const row = document.createElement("li");
    row.className = `action-row${state.actions[index] ? " done" : ""}`;
    const number = document.createElement("span");
    number.className = "action-number";
    number.textContent = String(index + 1).padStart(2, "0");
    const text = document.createElement("span");
    text.className = "action-text";
    text.textContent = label;
    if (index === 10) {
      row.classList.add("has-choice");
      row.classList.toggle("not-required", state.action11Result === "notRequired");
      const choices = document.createElement("div");
      choices.className = "action-choices";
      choices.setAttribute("role", "radiogroup");
      choices.setAttribute("aria-label", "פעולה 11: בוצעה או לא נדרשה");
      [["done", "✓", "בוצעה"], ["notRequired", "X", "לא נדרשה"]].forEach(([value, symbol, description]) => {
        const option = document.createElement("label");
        option.className = `action-choice ${value === "notRequired" ? "choice-x" : "choice-done"}`;
        const radio = document.createElement("input");
        radio.type = "radio";
        radio.name = `action-11-${sheet.id}`;
        radio.value = value;
        radio.checked = state.action11Result === value;
        radio.setAttribute("aria-label", `פעולה 11 ${description}`);
        radio.addEventListener("change", () => {
          state.action11Result = value;
          state.actions[index] = true;
          row.classList.add("done");
          row.classList.toggle("not-required", value === "notRequired");
          updateProgress();
          saveState();
        });
        const mark = document.createElement("span"); mark.textContent = symbol;
        option.append(radio, mark); choices.append(option);
      });
      row.append(number, text, choices);
    } else if (index === 1 || index === ACTIONS.length - 1) {
      const isArrival = index === 1;
      const timePicker = document.createElement("input");
      timePicker.className = "time-picker";
      timePicker.type = "time";
      timePicker.step = "60";
      timePicker.value = isArrival ? state.arrivalTime : state.completionTime;
      timePicker.dir = "ltr";
      timePicker.setAttribute("aria-label", isArrival ? "בחר שעת הגעה לדת״ק" : "בחר שעת סיום תקלה");
      timePicker.addEventListener("change", () => {
        if (isArrival) state.arrivalTime = timePicker.value;
        else state.completionTime = timePicker.value;
        state.actions[index] = Boolean(timePicker.value);
        row.classList.toggle("done", state.actions[index]);
        updateProgress();
        saveState();
      });
      row.append(number, text, timePicker);
    } else {
      const checkbox = document.createElement("input");
      checkbox.className = "action-check";
      checkbox.type = "checkbox";
      checkbox.checked = Boolean(state.actions[index]);
      checkbox.setAttribute("aria-label", `סמן פעולה ${index + 1} כהושלמה`);
      checkbox.addEventListener("change", () => {
        state.actions[index] = checkbox.checked;
        row.classList.toggle("done", checkbox.checked);
        updateProgress();
        saveState();
      });
      row.append(number, text, checkbox);
    }
    actionList.append(row);
  });
  updateProgress();
}

function updateProgress() {
  const complete = state.actions.filter(Boolean).length;
  document.querySelector("#action-progress").textContent = `${complete} מתוך ${ACTIONS.length}`;
}

function renderIssueList() {
  const list = document.querySelector("#mechanic-issues");
  list.replaceChildren();
  delete list.dataset.state;
  if (managerClosedView) {
    if (!selectedSheet) list.textContent = "התקלה הסגורה לא נמצאה. חזור לרשימת התקלות הסגורות.";
    return;
  }
  if (!issuesReady || !keyLoadsReady) {
    list.innerHTML = '<div class="app-skeleton" role="status"><p>טוען את משימות הצוות…</p><div class="skeleton-card"><span class="skeleton-title"></span><span class="skeleton-line"></span></div><div class="skeleton-card"><span class="skeleton-title"></span><span class="skeleton-line"></span></div></div>';
    return;
  }
  const issues = cloudIssues.filter((item) => item.status === "open").map((item) => ({ type: "issue", createdAt: item.createdAt, data: item.sheet }));
  const loads = keyLoads.filter((item) => item.status === "open").map((item) => ({ type: "keys", createdAt: item.createdAt, data: item }));
  const tasks = [...issues, ...loads].sort((a, b) => (b.createdAt || "").localeCompare(a.createdAt || ""));
  if (!tasks.length) list.textContent = "אין משימות פתוחות כרגע.";
  tasks.forEach(({ type, data: item }) => {
    const card = document.createElement("a");
    card.className = "issue-item";
    card.href = type === "keys" ? `./keys.html#work/${encodeURIComponent(item.id)}` : `?id=${encodeURIComponent(item.id)}`;
    const title = document.createElement("strong");
    title.textContent = type === "keys" ? `הטענת מפתחות ${item.serial || ""} · טייסת ${item.squadron} · דת״ק ${item.shelter}` : `תקלה ${item.serial || "ללא מספר"} · מטוס ${item.aircraft || "—"} · ${item.location || "—"}`;
    const info = document.createElement("span");
    info.textContent = type === "keys" ? `מטוסים: ${item.aircraftIds.join(", ")} · ${item.date.split("-").reverse().join("/")} · פתוחה` : `${item.fault || "ללא תיאור"} · פתוחה`;
    card.append(title, info);
    list.append(card);
  });
  if (keyLoadError) {
    const warning = document.createElement("p");
    warning.className = "copy-status";
    warning.dataset.state = "error";
    warning.textContent = `לא ניתן לטעון הטענות מפתחות: ${keyLoadError}`;
    list.append(warning);
  }
  if (issueLoadError) {
    const warning = document.createElement("p"); warning.className = "copy-status";
    warning.dataset.state = "error"; warning.textContent = `לא ניתן לטעון תקלות: ${issueLoadError}`;
    list.append(warning);
  }
}

function closureRecord() {
  return {
    id: sheet.id, serial: sheet.serial, location: sheet.location, aircraft: sheet.aircraft,
    systemCode: sheet.systemCode, systemName: sheet.systemName,
    technicians: sheet.technicians, fault: sheet.fault,
    managerNotes: sheet.managerNotes || "", technicianNotes: state.technicianNotes,
    closedAt: state.closedAt, arrivalTime: state.arrivalTime, completionTime: state.completionTime,
    repair: state.repair.slice(0, 1200),
    actionsComplete: state.actions.every(Boolean) && Boolean(state.action11Result),
    removedCount: state.removed.filter((row) => row.name || row.serial).length,
    installedCount: state.installed.filter((row) => row.name || row.serial).length,
    toolsCount: state.tools.filter((row) => row.toolName || row.toolNumber || row.notes).length,
    takenCount: state.taken.filter((row) => row.description).length,
    rejectedCount: state.rejected.filter((row) => row.description).length
  };
}

function showClosed() {
  if (!selectedSheet) return;
  document.querySelector('.sheet-save-toolbar')?.setAttribute('hidden','');
  document.querySelector("#sheet-form").classList.add("is-closed");
  document.querySelector("#sheet-form").querySelectorAll("input, textarea, select, button").forEach((element) => {
    if (element.closest("#review-panel")) return;
    element.disabled = true;
  });
  document.querySelectorAll("canvas[data-signature]").forEach((canvas) => { canvas.style.pointerEvents = "none"; });
  document.querySelector("#close-sheet").hidden = true;
  const message = document.querySelector("#close-message");
  delete message.dataset.state;
  message.textContent = remoteIssue?.status === "closed" ? `הדף נסגר ב־${new Date(remoteIssue.reviewedAt || state.closedAt).toLocaleString("he-IL")}. ${managerClosedView ? "המנהל יכול להחזיר אותו לטכנאי לעריכה." : "אי אפשר לערוך אותו."}` : "הדף הועבר לבדיקת בודק הדף וממתין לחתימתו.";
}

if (managerClosedView && selectedSheet) {
  document.querySelector(".brand-copy h1").textContent = remoteIssue.status === "pendingReview" ? "Metis · תקלה לסגירה" : "Metis · דף תקלה סגורה";
  document.querySelector("#mechanic-back").href = remoteIssue.status === "pendingReview" ? "/Metis/index.html#pending" : "/Metis/index.html#closed";
  const panel = document.querySelector("#manager-edit-panel");
  panel.hidden = false;
  document.querySelector("#manager-edit-open").addEventListener("click", async (event) => {
    if (!window.confirm("להחזיר את התקלה לטכנאי לעריכה? כל הפרטים שמולאו יישמרו והוא יוכל לסגור שוב.")) return;
    const button = event.currentTarget; button.disabled = true;
    try { await reopenIssue(sheet.id, currentUser.uid); location.replace("/Metis/index.html#open"); }
    catch (error) { document.querySelector("#manager-edit-status").textContent = `ההחזרה נכשלה: ${error.message}`; button.disabled = false; }
  });
  const reviewPanel = document.querySelector("#review-panel");
  reviewPanel.hidden = remoteIssue.status !== "pendingReview" && !remoteIssue.reviewerSignature;
  if (!reviewPanel.hidden) {
    const isPending = remoteIssue.status === "pendingReview";
    const canvas = document.querySelector("#reviewer-signature");
    let signature = "";
    const clear = attachSignaturePad(canvas, {
      initial: isPending ? "" : remoteIssue.reviewerSignature || "",
      onChange: (value) => { signature = value; }
    });
    canvas.style.pointerEvents = isPending ? "auto" : "none";
    document.querySelector("#clear-reviewer-signature").hidden = !isPending;
    document.querySelector("#approve-sheet").hidden = !isPending;
    document.querySelector("#clear-reviewer-signature").addEventListener("click", clear);
    document.querySelector("#approve-sheet").addEventListener("click", async (event) => {
      if (!signature) { document.querySelector("#review-status").textContent = "יש לחתום כבודק הדף לפני הסגירה."; return; }
      const button = event.currentTarget; button.disabled = true;
      try { await approveIssue(sheet.id, currentUser.uid, signature); location.replace("/Metis/index.html#closed"); }
      catch (error) { document.querySelector("#review-status").textContent = `הסגירה נכשלה: ${error.message}`; button.disabled = false; }
    });
  }
}

async function submitTreatment() {
  if (managerClosedView) return;
  if (!editLock?.held) { document.querySelector("#close-message").textContent = "הטופס אינו נעול לעריכה עבורך. לחץ על בדוק שוב."; return; }
  if (document.querySelector("#close-sheet").disabled) return;
  const validation = validateTreatment(sheet, state, TABLES);
  if (validation) {
    const message = document.querySelector("#close-message");
    message.textContent = validation.message;
    message.dataset.state = "error";
    const target = document.querySelector(validation.selector);
    target?.scrollIntoView({ behavior: "smooth", block: "center" });
    target?.focus({ preventScroll: true });
    return;
  }
  const button = document.querySelector("#close-sheet");
  button.disabled = true;
  const closedAt = new Date().toISOString();
  const closingState = { ...state, closedAt };
  for (const [type, fields] of Object.entries(TABLES)) closingState[type] = compactRows(state[type], fields);
  const record = { ...closureRecord(), closedAt, actionsComplete: true };
  try {
    await flushState();
    await submitForReview(sheet.id, record, closingState, editLock.session);
    Object.assign(state, closingState);
    try { draftState.setItem(STORAGE_KEY, JSON.stringify(state)); } catch { /* The submitted form is already saved in Access. */ }
    showClosed();
    editLock.dispose();
    document.querySelector("#close-title").scrollIntoView({ behavior: "smooth", block: "center" });
  } catch (error) {
    const message = document.querySelector("#close-message");
    message.textContent = `השמירה נכשלה: ${error.message}. נסה שוב.`;
    message.dataset.state = "error";
    button.disabled = false;
  }
}
document.querySelector("#close-sheet").addEventListener("click", submitTreatment);
document.querySelector("#save-sheet")?.addEventListener("click", async (event) => {
  if (!editLock?.held || state.closedAt) return;
  const button=event.currentTarget;button.disabled=true;
  document.querySelector("#save-state").textContent="שומר את הטופס המשותף…";
  try {
    await flushState();
    await editLock.release();
    location.assign('/mechanic/index.html');
  } catch (error) {
    const status=document.querySelector("#save-state");status.textContent=`השמירה נכשלה: ${error.message}`;status.dataset.state="error";button.disabled=false;
  }
});
document.querySelector("#reset-sheet")?.addEventListener("click", async (event) => {
  if (!editLock?.held || state.closedAt || !confirm("למחוק את כל המילוי בטופס המשותף ולהתחיל מחדש?")) return;
  const button=event.currentTarget;button.disabled=true;
  try {
    state={...structuredClone(defaultState),actions:ACTIONS.map(()=>false)};
    await flushState();
    await editLock.release();
    location.reload();
  } catch(error) {
    const status=document.querySelector("#save-state");status.textContent=`האיפוס נכשל: ${error.message}`;status.dataset.state="error";button.disabled=false;
  }
});

watchIssues((issues) => {
  cloudIssues = issues;
  issuesReady = true;
  issueLoadError = "";
  renderIssueList();
  const current = issues.find((item) => item.id === selectedId);
  if (current?.status === "open" && current.sheet.managerNotes !== sheet.managerNotes) {
    document.querySelector("#combined-notes").updateManagerNotes?.(current.sheet.managerNotes || "");
  }
  if (current?.status === "open" && current.reopenedAt && state.closedAt) {
    location.reload();
    return;
  }
  if (["closed", "pendingReview"].includes(current?.status) && !state.closedAt) {
    state = { ...defaultState, ...current.closed.state };
    draftState.setItem(STORAGE_KEY, JSON.stringify(state));
    location.reload();
  }
}, (error) => { issuesReady = true; issueLoadError = error.message; renderIssueList(); if (error.code === "unavailable") document.dispatchEvent(new Event("ezeizo:connection-lost")); }, managerClosedView ? null : currentUser.uid);

if (!managerClosedView) watchKeyLoads((loads) => { keyLoads = loads; keyLoadsReady = true; keyLoadError = ""; renderIssueList(); }, (error) => { keyLoadsReady = true; keyLoadError = error.message; renderIssueList(); }, currentUser.uid);

function rowHasValue(item, fields) {
  return fields.some(({ key }) => String(item?.[key] ?? "").trim() !== "");
}

function compactRows(rows, fields) {
  return (Array.isArray(rows) ? rows : [])
    .filter((item) => rowHasValue(item, fields))
    .map((item) => Object.fromEntries(fields.map(({ key }) => [key, item[key] ?? ""])));
}

function normalizeRows(rows, fields, editable = true) {
  const filledRows = compactRows(rows, fields);
  return editable ? [...filledRows, Object.fromEntries(fields.map(({ key }) => [key, ""]))] : filledRows;
}

function renderRows(type, focusTarget = null) {
  const fields = TABLES[type];
  if (!fields) return;
  state[type] = normalizeRows(state[type], fields, !managerClosedView && !state.closedAt);
  const target = document.querySelector(`#${type}-rows`);
  const rows = state[type];
  target.replaceChildren();
  rows.forEach((item, index) => {
    const tr = document.createElement("tr");
    fields.forEach((field) => {
      const { key } = field;
      const td = document.createElement("td");
      const input = document.createElement(field.options ? "select" : "input");
      if (field.options) {
        input.append(new Option(field.placeholder, ""));
        field.options.forEach((name) => input.append(new Option(name, name)));
        if (item[key] && !field.options.includes(item[key])) input.append(new Option(`${item[key]} (ערך קודם)`, item[key]));
      } else input.type = field.type || "text";
      if (field.type === "number") input.step = "1";
      input.value = item[key] || "";
      input.placeholder = field.placeholder;
      input.dataset.rowIndex = String(index);
      input.dataset.field = key;
      input.setAttribute("aria-label", `${field.label}, שורה ${index + 1}`);
      input.addEventListener(field.options ? "change" : "input", () => {
        const wasFilled = rowHasValue(rows[index], fields);
        rows[index][key] = input.value;
        const isFilled = rowHasValue(rows[index], fields);
        const isLastRow = index === rows.length - 1;
        if ((isFilled && isLastRow) || (wasFilled && !isFilled && !isLastRow)) {
          const caret = field.options ? null : input.selectionStart;
          renderRows(type, { index, key, caret });
        }
        saveState();
      });
      td.append(input);
      tr.append(td);
    });
    const removeCell = document.createElement("td");
    const remove = document.createElement("button");
    remove.className = "remove-row";
    remove.type = "button";
    remove.textContent = "×";
    remove.setAttribute("aria-label", "הסר שורה");
    if (!rowHasValue(item, fields)) {
      remove.hidden = true;
      remove.tabIndex = -1;
    }
    remove.addEventListener("click", () => { rows.splice(index, 1); renderRows(type); saveState(); });
    removeCell.append(remove);
    tr.append(removeCell);
    target.append(tr);
  });
  if (focusTarget) {
    requestAnimationFrame(() => {
      const input = target.querySelector(`[data-row-index="${focusTarget.index}"][data-field="${focusTarget.key}"]`);
      if (!input) return;
      input.focus({ preventScroll: true });
      if (focusTarget.caret !== null) input.setSelectionRange(focusTarget.caret, focusTarget.caret);
    });
  }
}

function bindToggleInputs() {
  document.querySelectorAll("[data-toggle-state]").forEach((input) => {
    const key = input.dataset.toggleState;
    const target = document.getElementById(input.dataset.target);
    input.checked = Boolean(state[key]);
    target.hidden = !input.checked;
    input.addEventListener("change", () => {
      state[key] = input.checked;
      target.hidden = !input.checked;
      if (input.checked) setupSignaturePads(target);
      saveState();
    });
  });
}

function bindStateFields() {
  document.querySelectorAll("[data-state-field]").forEach((input) => {
    const key = input.dataset.stateField;
    input.value = state[key] || "";
    const eventName = input.type === "time" ? "change" : "input";
    input.addEventListener(eventName, () => {
      if (key.endsWith("InspectorNumber") || key === "takenPersonalNumber") input.value = input.value.replace(/[^0-9]/g, "").slice(0, 7);
      state[key] = input.value;
      saveState();
    });
  });
}

function setupSignaturePads(root = document) {
  root.querySelectorAll("canvas[data-signature]").forEach((canvas) => {
    if (canvas.dataset.initialized === "true" || canvas.clientWidth === 0) return;
    canvas.dataset.initialized = "true";
    const key = canvas.dataset.signature;
    const context = canvas.getContext("2d");
    const ratio = window.devicePixelRatio || 1;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
    context.scale(ratio, ratio);
    context.lineWidth = 2;
    context.lineCap = "round";
    context.lineJoin = "round";
    context.strokeStyle = "#18334a";

    if (state[key]) {
      const image = new Image();
      image.onload = () => context.drawImage(image, 0, 0, width, height);
      image.src = state[key];
    }

    let drawing = false;
    const point = (event) => {
      const rect = canvas.getBoundingClientRect();
      return { x: event.clientX - rect.left, y: event.clientY - rect.top };
    };
    canvas.addEventListener("pointerdown", (event) => {
      drawing = true;
      canvas.setPointerCapture(event.pointerId);
      const p = point(event);
      context.beginPath();
      context.moveTo(p.x, p.y);
      context.lineTo(p.x + 0.1, p.y + 0.1);
      context.stroke();
      event.preventDefault();
    });
    canvas.addEventListener("pointermove", (event) => {
      if (!drawing) return;
      const p = point(event);
      context.lineTo(p.x, p.y);
      context.stroke();
      event.preventDefault();
    });
    const finish = () => {
      if (!drawing) return;
      drawing = false;
      state[key] = canvas.toDataURL("image/png");
      saveState();
      if (key === "technicianDeclarationSignature" && !managerClosedView) submitTreatment();
    };
    canvas.addEventListener("pointerup", finish);
    canvas.addEventListener("pointercancel", finish);
    canvas.addEventListener("lostpointercapture", finish);
  });
}

document.querySelectorAll("[data-clear-signature]").forEach((button) => {
  button.addEventListener("click", () => {
    const key = button.dataset.clearSignature;
    state[key] = "";
    const canvas = document.querySelector(`canvas[data-signature="${key}"]`);
    if (canvas) canvas.getContext("2d").clearRect(0, 0, canvas.width, canvas.height);
    saveState();
  });
});

repairField.value = state.repair;
document.querySelector("#repair-count").textContent = `${state.repair.length} / 1200`;
repairField.addEventListener("input", () => {
  state.repair = repairField.value;
  document.querySelector("#repair-count").textContent = `${repairField.value.length} / 1200`;
  saveState();
});

unitsToggle.checked = Boolean(state.unitsEnabled);
unitsArea.hidden = !unitsToggle.checked;
unitsToggle.addEventListener("change", () => {
  state.unitsEnabled = unitsToggle.checked;
  unitsArea.hidden = !state.unitsEnabled;
  saveState();
});

renderSheetDetails();
if (managerClosedView) document.querySelector("#save-state").textContent = "דף סגור · קריאה בלבד";
renderTechnicianNotes();
renderActions();
Object.keys(TABLES).forEach((table) => renderRows(table));
bindToggleInputs();
bindStateFields();
attachTimeNow();
revealApp();
setupSignaturePads();

renderIssueList();
if (selectedSheet && state.closedAt) showClosed();

