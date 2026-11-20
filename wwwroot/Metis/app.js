import "../sheet-data.js?v=1.6.3";
import { gate, revealApp, watchIssues, watchKeyLoads, watchTechnicians, createIssue, createAssignedIssues, createTechnician, setTechnicianActive, deleteTechnicianPermanently, assignIssue, appendIssueManagerNote } from "../firebase-sync.js?v=1.6.3";
import { SQUADRONS, SYSTEMS, WORKSHOPS, EW_SYSTEMS, validateCatalogSelection } from "./catalog.js?v=1.6.3";
import { resolveAssignees } from "./assignee-selection.js?v=1.6.3";
import { attachSignaturePad } from "../signature-canvas.js?v=1.6.3";
import { initKeyPages } from "../keys/app.js?v=1.6.3";
import { initReports, updateReportData, initAircraftConstants, constantsForAircraft, closeConstantsEditor } from "./management-pages.js?v=1.6.3";

const managerUser = await gate("manager");
document.querySelector(".brand-copy h1").textContent = "Metis · נימר";
let cloudIssues = [];
let technicians = [];
const DRAFT_KEY = "ezeizo-metis-draft-v1";
const PICKER_KEY = "ezeizo-metis-picker-v1";
let draft = loadDraft();
let selections = loadSelections();
const draftStatus = document.querySelector("#draft-status");

function setStatus(selector, message, error = false) {
  const element = document.querySelector(selector);
  element.textContent = message;
  if (error) element.dataset.state = "error";
  else delete element.dataset.state;
}

function loadDraft() {
  try {
    return EzEizoData.normalizeSheet(JSON.parse(localStorage.getItem(DRAFT_KEY))) || EzEizoData.normalizeSheet(EzEizoData.defaultSheet);
  } catch {
    return EzEizoData.normalizeSheet(EzEizoData.defaultSheet);
  }
}

function saveDraft() {
  localStorage.setItem(DRAFT_KEY, JSON.stringify(draft));
  localStorage.setItem(PICKER_KEY, JSON.stringify(selections));
  draftStatus.textContent = "הטיוטה נשמרה במכשיר הזה";
}

function loadSelections() {
  try { const value = JSON.parse(localStorage.getItem(PICKER_KEY) || "{}"); return value && typeof value === "object" && !Array.isArray(value) ? value : {}; }
  catch { return {}; }
}

document.querySelectorAll("[data-sheet-field]").forEach((input) => {
  if (input.closest(".search-picker") || input.readOnly) return;
  const key = input.dataset.sheetField;
  input.value = draft[key] || "";
  input.addEventListener("input", () => {
    draft[key] = input.value;
    saveDraft();
  });
});

const pickers = Object.fromEntries([...document.querySelectorAll("[data-picker]")].map((root) => [root.dataset.picker, {
  root, input: root.querySelector("input"), list: root.querySelector(".picker-list"), matches: [], activeIndex: 0
}]));

function availableFor(key) {
  if (key === "squadron") return Object.keys(SQUADRONS);
  if (key === "systemName") return SYSTEMS.map((system) => system.name);
  if (key === "workshop") return selections.shelter === "מוסך" ? WORKSHOPS : [];
  return Object.hasOwn(SQUADRONS, selections.squadron) ? SQUADRONS[selections.squadron][key === "shelter" ? "shelters" : "aircraft"] : [];
}

function locationForDraft() {
  return [draft.shelter === "מוסך" && draft.workshop ? draft.workshop : draft.shelter, draft.squadron].filter(Boolean).join("/");
}

function closePicker(key) {
  const picker = pickers[key];
  picker.list.hidden = true;
  picker.input.setAttribute("aria-expanded", "false");
  picker.input.removeAttribute("aria-activedescendant");
}

function renderOptions(key) {
  const picker = pickers[key];
  const search = picker.input.value.trim().toLocaleLowerCase("he");
  picker.matches = availableFor(key).filter((value) => value.toLocaleLowerCase("he").includes(search));
  picker.activeIndex = 0;
  picker.list.replaceChildren();
  if (!picker.matches.length) {
    const empty = document.createElement("p"); empty.className = "picker-empty"; empty.textContent = "אין תוצאות מתאימות";
    picker.list.append(empty);
  }
  picker.matches.forEach((value, index) => {
    const option = document.createElement("button");
    option.type = "button";
    option.id = `option-${key}-${index}`;
    option.className = "picker-option";
    option.setAttribute("role", "option");
    option.setAttribute("aria-selected", String(value === selections[key]));
    option.textContent = value;
    option.addEventListener("pointerdown", (event) => event.preventDefault());
    option.addEventListener("click", () => choosePicker(key, value));
    picker.list.append(option);
  });
  picker.list.hidden = false;
  picker.input.setAttribute("aria-expanded", "true");
  if (picker.matches.length) picker.input.setAttribute("aria-activedescendant", `option-${key}-0`);
}

function refreshPickers() {
  document.querySelector("#workshop-field").hidden = selections.shelter !== "מוסך";
  for (const [key, picker] of Object.entries(pickers)) {
    picker.input.disabled = key === "workshop" ? selections.shelter !== "מוסך" : (key === "shelter" || key === "aircraft") && !selections.squadron;
    picker.input.placeholder = picker.input.disabled ? "בחר טייסת תחילה" : key === "aircraft" ? "חפש ובחר מספר מטוס" : key === "shelter" ? "חפש ובחר דת״ק" : key === "workshop" ? "חפש ובחר מוסך" : key === "systemName" ? "חפש ובחר מערכת" : "חפש ובחר טייסת";
    picker.input.value = selections[key] || "";
    closePicker(key);
  }
  document.querySelector('[data-sheet-field="systemCode"]').value = draft.systemCode;
  const ewSelect = document.querySelector("#ew-system-select");
  document.querySelector("#ew-system-field").hidden = !selections.aircraft;
  ewSelect.disabled = !selections.aircraft;
  ewSelect.value = selections.aircraft ? (draft.ewSystem || "גרניט") : "";
}

function clearPicker(key) {
  selections[key] = "";
  draft[key] = "";
  if (key === "squadron") {
    selections.shelter = ""; selections.workshop = ""; selections.aircraft = "";
    draft.shelter = ""; draft.workshop = ""; draft.aircraft = ""; draft.ewSystem = "";
  }
  if (key === "shelter") { selections.workshop = ""; draft.workshop = ""; }
  if (key === "aircraft") draft.ewSystem = "";
  if (key === "systemName") draft.systemCode = "";
  draft.location = locationForDraft();
  refreshPickers();
  saveDraft();
}

function choosePicker(key, value) {
  if (!availableFor(key).includes(value)) return;
  const changedAircraft = key === "aircraft" && selections.aircraft !== value;
  if (key === "squadron" && selections.squadron !== value) {
    selections.shelter = ""; selections.workshop = ""; selections.aircraft = "";
    draft.shelter = ""; draft.workshop = ""; draft.aircraft = ""; draft.ewSystem = "";
  }
  if (key === "shelter" && selections.shelter !== value) { selections.workshop = ""; draft.workshop = ""; }
  selections[key] = value;
  draft[key] = value;
  if (changedAircraft) { draft.ewSystem = "גרניט"; draft.constants = constantsForAircraft(selections.squadron, value); renderConstants(); }
  if (key === "systemName") draft.systemCode = SYSTEMS.find((system) => system.name === value).code;
  draft.location = locationForDraft();
  refreshPickers();
  saveDraft();
  setStatus("#created-status", "");
}

for (const [key, picker] of Object.entries(pickers)) {
  picker.input.addEventListener("focus", () => renderOptions(key));
  picker.input.addEventListener("input", () => {
    const search = picker.input.value;
    clearPicker(key);
    picker.input.value = search;
    renderOptions(key);
  });
  picker.input.addEventListener("keydown", (event) => {
    if (event.key === "Escape") { closePicker(key); return; }
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      if (picker.list.hidden) renderOptions(key);
      if (!picker.matches.length) return;
      picker.activeIndex = (picker.activeIndex + (event.key === "ArrowDown" ? 1 : -1) + picker.matches.length) % picker.matches.length;
      picker.input.setAttribute("aria-activedescendant", `option-${key}-${picker.activeIndex}`);
      picker.list.children[picker.activeIndex].scrollIntoView({ block: "nearest" });
    }
    if (event.key === "Enter" && !picker.list.hidden && picker.matches.length) {
      event.preventDefault(); choosePicker(key, picker.matches[picker.activeIndex]);
    }
  });
  picker.input.addEventListener("blur", () => setTimeout(() => {
    if (!picker.root.contains(document.activeElement)) closePicker(key);
  }, 250));
}

function restorePickers() {
  if (!Object.hasOwn(SQUADRONS, selections.squadron) || draft.squadron !== selections.squadron) selections.squadron = "";
  for (const key of ["shelter", "workshop", "aircraft", "systemName"]) {
    if (!availableFor(key).includes(selections[key]) || draft[key] !== selections[key]) selections[key] = "";
  }
  for (const key of Object.keys(pickers)) draft[key] = selections[key] || "";
  draft.systemCode = SYSTEMS.find((system) => system.name === selections.systemName)?.code || "";
  draft.ewSystem = selections.aircraft ? (EW_SYSTEMS.includes(draft.ewSystem) ? draft.ewSystem : "גרניט") : "";
  draft.location = locationForDraft();
  refreshPickers();
  saveDraft();
}

restorePickers();

function renderConstants() {
  const container = document.querySelector("#manager-constants");
  container.replaceChildren();
  Object.entries(draft.constants).forEach(([groupName, entries]) => {
    const section = document.createElement("section");
    section.className = "constant-group";
    const heading = document.createElement("h3");
    heading.textContent = groupName;
    const grid = document.createElement("div");
    grid.className = "manager-constant-list";
    entries.forEach(([label, value], index) => {
      const field = document.createElement("label");
      field.className = "manager-constant-row";
      const name = document.createElement("span");
      name.textContent = label;
      const input = document.createElement("input");
      input.type = "text";
      input.inputMode = "decimal";
      input.value = value;
      input.setAttribute("aria-label", `${groupName} ${label}`);
      input.addEventListener("input", () => { draft.constants[groupName][index][1] = input.value; saveDraft(); });
      field.append(name, input);
      grid.append(field);
    });
    section.append(heading, grid);
    container.append(section);
  });
}

function renderActions() {
  const container = document.querySelector("#manager-actions");
  container.replaceChildren();
  EzEizoData.defaultSheet.actions.forEach((label, index) => {
    const row = document.createElement("div");
    row.className = "manager-action-row";
    const number = document.createElement("span");
    number.textContent = String(index + 1).padStart(2, "0");
    const text = document.createElement("p");
    text.textContent = label;
    row.append(number, text);
    container.append(row);
  });
}

document.querySelector("#create-link").addEventListener("click", async () => {
  const button = document.querySelector("#create-link");
  const acknowledgment = document.querySelector("#creation-ack");
  if (!acknowledgment.checked) { setStatus("#created-status", "יש לאשר את בדיקת פרטי התקלה לפני פתיחתה.", true); acknowledgment.focus(); return; }
  if (!draft.dispatcherSignature) { setStatus("#created-status", "חובה להחתים את משלח המשימה על תדריך הבטיחות וניהול הסיכונים.", true); document.querySelector("#dispatcher-signature").scrollIntoView({ block: "center" }); return; }
  const catalogError = validateCatalogSelection(selections, draft);
  if (catalogError) { setStatus("#created-status", catalogError.message, true); (pickers[catalogError.field]?.input || document.querySelector("#ew-system-select")).focus(); return; }
  const { assignees, error: assigneeError } = resolveAssignees(selectedAssigneeUids(), technicians);
  if (assigneeError) { setStatus("#created-status", assigneeError, true); document.querySelector("#new-assignees").scrollIntoView({ block: "center" }); return; }
  button.disabled = true;
  const sheet = EzEizoData.normalizeSheet(draft);
  if (!sheet.fault.trim()) {
    setStatus("#created-status", "חובה למלא תיאור תקלה לפני פתיחת התקלה.", true);
    document.querySelector('[data-sheet-field="fault"]').focus();
    button.disabled = false;
    return;
  }
  sheet.id = typeof crypto.randomUUID === "function" ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
  try {
    sheet.serial = await createAssignedIssues(sheet, assignees);
    acknowledgment.checked = false;
    draft.dispatcherSignature = "";
    clearDispatcherSignature();
    setStatus("#created-status", `תקלה מספר ${sheet.serial} הוקצתה ל־${assignees.map((person) => person.name).join(" ו־")}. לכל טכנאי דף מילוי משלו.`);
    window.location.hash = "open";
  } catch (error) { setStatus("#created-status", `פתיחת התקלה נכשלה: ${error.message}`, true); }
  finally { button.disabled = false; }
});

function addIssueCard(container, item, mode) {
  const card = document.createElement("div");
  card.className = "issue-item";
  const title = document.createElement("strong");
  title.textContent = `תקלה ${item.serial || "ללא מספר"} · מטוס ${item.aircraft || "—"} · ${item.location || "—"}`;
  const subtitle = document.createElement("span");
  const fullIssue = cloudIssues.find((issue) => issue.id === item.id);
  subtitle.textContent = `${item.fault || "ללא תיאור"} · ${mode === "closed" ? "סגורה" : mode === "pendingReview" ? "ממתינה לבודק הדף" : "פתוחה"} · ${fullIssue?.assigneeName || "לא שובצה"}`;
  card.append(title, subtitle);
  if (mode !== "open") {
    const view = document.createElement("a");
    view.className = "secondary-button";
    view.href = `./closed.html?id=${encodeURIComponent(item.id)}`;
    view.textContent = mode === "pendingReview" ? "בדוק וחתום" : "פתח דף סגור";
    card.append(view);
  } else {
    const assignLabel = document.createElement("label");
    assignLabel.className = "input-field";
    assignLabel.textContent = "טכנאי משויך";
    const select = document.createElement("select");
    select.setAttribute("aria-label", `שיוך תקלה ${item.serial}`);
    select.append(new Option("לא משויכת", ""));
    technicians.filter((person) => person.active || person.uid === fullIssue?.assigneeUid).forEach((person) => {
      select.append(new Option(`${person.name}${person.active ? "" : " (מושבת)"}`, person.uid));
    });
    select.value = fullIssue?.assigneeUid || "";
    const assignFeedback = document.createElement("p");
    assignFeedback.className = "copy-status";
    assignFeedback.setAttribute("role", "status");
    select.addEventListener("change", async () => {
      const person = technicians.find((entry) => entry.uid === select.value && entry.active);
      if (!person) { select.value = fullIssue?.assigneeUid || ""; return; }
      select.disabled = true;
      try { await assignIssue(item.id, person.uid, person.name); }
      catch (error) { assignFeedback.textContent = `השיבוץ נכשל: ${error.message}`; assignFeedback.dataset.state = "error"; select.value = fullIssue?.assigneeUid || ""; }
      finally { select.disabled = false; }
    });
    assignLabel.append(select);
    card.append(assignLabel, assignFeedback);
    const editor = document.createElement("details"); editor.className = "manager-note-editor";
    const summary = document.createElement("summary"); summary.textContent = "הערות מנהל";
    const existing = document.createElement("p"); existing.className = "manager-note-text"; existing.textContent = fullIssue?.sheet?.managerNotes || "עדיין אין הערות.";
    const note = document.createElement("textarea"); note.rows = 2; note.maxLength = 500; note.placeholder = "הוסף הערה לטכנאי…"; note.setAttribute("aria-label", `הוסף הערה לתקלה ${item.serial}`);
    const send = document.createElement("button"); send.type = "button"; send.className = "secondary-button"; send.textContent = "הוסף הערה";
    const feedback = document.createElement("p"); feedback.className = "copy-status"; feedback.setAttribute("role", "status");
    send.addEventListener("click", async () => {
      if (!note.value.trim()) { feedback.textContent = "יש לכתוב הערה לפני השליחה."; feedback.dataset.state = "error"; return; }
      send.disabled = true;
      try { await appendIssueManagerNote(item.id, note.value); note.value = ""; feedback.textContent = "ההערה נוספה ונשלחה לטכנאי."; delete feedback.dataset.state; }
      catch (error) { feedback.textContent = `הוספת ההערה נכשלה: ${error.message}`; feedback.dataset.state = "error"; }
      finally { send.disabled = false; }
    });
    editor.append(summary, existing, note, send, feedback); card.append(editor);
  }
  container.append(card);
}

function renderIssueLists() {
  const opened = document.querySelector("#issued-list");
  const closed = document.querySelector("#closed-list");
  const pending = document.querySelector("#pending-list");
  opened.replaceChildren();
  closed.replaceChildren();
  pending.replaceChildren();
  delete opened.dataset.state;
  delete closed.dataset.state;
  const filter = document.querySelector("#closed-filter").value;
  const matches = (selector, serial) => String(serial || "").includes(document.querySelector(selector).value.trim());
  const allClosed = cloudIssues.filter((item) => item.status === "closed" && item.closed?.record && (!filter || item.assigneeUid === filter) && matches("#closed-issue-search", item.closed.record.serial)).map((item) => item.closed.record);
  const awaiting = cloudIssues.filter((item) => item.status === "pendingReview" && item.closed?.record && matches("#pending-issue-search", item.closed.record.serial)).map((item) => item.closed.record);
  const active = cloudIssues.filter((item) => item.status === "open" && matches("#open-issue-search", item.sheet?.serial)).map((item) => item.sheet);
  active.forEach((item) => addIssueCard(opened, item, "open"));
  awaiting.forEach((item) => addIssueCard(pending, item, "pendingReview"));
  allClosed.forEach((item) => addIssueCard(closed, item, "closed"));
  if (!active.length) opened.textContent = "לא נמצאו תקלות פתוחות התואמות לחיפוש.";
  if (!awaiting.length) pending.textContent = "לא נמצאו תקלות לסגירה התואמות לחיפוש.";
  if (!allClosed.length) closed.textContent = "לא נמצאו תקלות סגורות התואמות לחיפוש.";
}

let keyPagesStarted = false;
function showManagerView() {
  const requested = window.location.hash.slice(1);
  const selected = requested === "keys" || requested.startsWith("keys/") ? "keys" : ["home", "management", "reports", "aircraft-constants", "new", "open", "pending", "closed", "users"].includes(requested) ? requested : "home";
  document.querySelector("#manager-back").hidden = !["users", "reports", "aircraft-constants"].includes(selected);
  document.querySelector("#reset-draft").hidden = requested !== "new" && requested !== "keys/create";
  if (selected !== "keys") document.querySelector("#key-back-link").hidden = true;
  document.querySelector("#fault-nav").hidden = !["new", "open", "pending", "closed"].includes(selected);
  document.querySelectorAll("[data-manager-view]").forEach((view) => { view.hidden = view.dataset.managerView !== (selected === "pending" ? "closed" : selected); });
  document.querySelector("#pending-section").hidden = selected !== "pending";
  document.querySelector("#closed-section").hidden = selected === "pending";
  document.querySelectorAll("[data-view-link]").forEach((link) => {
    const nav = ["new", "open", "pending", "closed"].includes(selected) ? "faults" : ["users", "reports", "aircraft-constants"].includes(selected) ? "management" : selected;
    if (link.dataset.viewLink === nav) link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
  });
  document.querySelectorAll("[data-fault-link]").forEach((link) => { if (link.dataset.faultLink === (selected === "pending" ? "closed" : selected)) link.setAttribute("aria-current", "page"); else link.removeAttribute("aria-current"); });
  document.querySelectorAll("[data-review-link]").forEach((link) => { if (link.dataset.reviewLink === selected) link.setAttribute("aria-current", "page"); else link.removeAttribute("aria-current"); });
  if (selected === "keys" && !keyPagesStarted) { keyPagesStarted = true; initKeyPages({ user: managerUser, embedded: true }); }
  if (selected !== "new") renderIssueLists();
}
document.querySelector("#manager-back").addEventListener("click", () => { if (location.hash === "#aircraft-constants" && closeConstantsEditor()) return; location.hash = "management"; });
window.addEventListener("hashchange", showManagerView);
function resetFaultDraft() {
  draft = EzEizoData.normalizeSheet(EzEizoData.defaultSheet);
  selections = {};
  for (const key of Object.keys(pickers)) draft[key] = "";
  draft.systemCode = "";
  draft.ewSystem = "";
  draft.location = "";
  draft.technicians = "";
  draft.fault = "";
  draft.managerNotes = "";
  draft.dispatcherSignature = "";
  clearDispatcherSignature();
  document.querySelectorAll("[data-sheet-field]").forEach((input) => { input.value = draft[input.dataset.sheetField] || ""; });
  refreshPickers();
  renderAssigneeSelectors(["", ""]);
  document.querySelector("#creation-ack").checked = false;
  setStatus("#assignee-feedback", "");
  renderConstants();
  renderActions();
  saveDraft();
  setStatus("#created-status", "טיוטת התקלה אופסה.");
}
document.querySelector("#reset-draft").addEventListener("click", () => {
  if (window.location.hash === "#new") resetFaultDraft();
  else if (window.location.hash === "#keys/create") document.dispatchEvent(new Event("ezeizo:reset-key-draft"));
});

watchIssues((issues) => {
  cloudIssues = issues;
  updateReportData({ issues });
  document.querySelector("#home-open-count").textContent = `${issues.filter((item) => item.status === "open").length} תקלות פתוחות`;
  document.querySelector("#home-review-count").textContent = `${issues.filter((item) => item.status === "pendingReview").length} ממתינות לסגירה · ${issues.filter((item) => item.status === "closed").length} סגורות`;
  renderIssueLists(); renderUsers();
}, (error) => {
  setStatus("#issued-list", `לא ניתן לטעון תקלות: ${error.message}`, true);
  setStatus("#closed-list", `לא ניתן לטעון תקלות: ${error.message}`, true);
});
function selectedAssigneeUids() {
  return [...document.querySelectorAll('#new-assignees select')].map((select) => select.value);
}
function syncAssigneeNames() {
  const selected = selectedAssigneeUids().filter(Boolean);
  const duplicate = selected.length !== new Set(selected).size;
  document.querySelectorAll('#new-assignees select').forEach((select) => { select.setAttribute('aria-invalid', String(Boolean(select.value && selected.filter((uid) => uid === select.value).length > 1))); });
  setStatus('#assignee-feedback', duplicate ? 'כל טכנאי יכול להיבחר פעם אחת בלבד.' : '', duplicate);
  draft.technicians = selected.map((uid) => technicians.find((person) => person.uid === uid)?.name).filter(Boolean).join(' · ');
  saveDraft();
}
function renderAssigneeSelectors(values = selectedAssigneeUids()) {
  const container = document.querySelector('#new-assignees');
  container.replaceChildren();
  (values.length >= 2 ? values : ["", ""]).forEach((uid, index) => {
    const row = document.createElement('div'); row.className = 'assignee-row';
    const roleLabel = index === 0 ? 'מוביל התקלה' : `טכנאי ${index + 1}`;
    const label = document.createElement('label'); label.className = 'input-field'; label.textContent = roleLabel;
    const select = document.createElement('select'); select.setAttribute('aria-label', roleLabel);
    select.append(new Option('בחר טכנאי', ''));
    technicians.filter((person) => person.active).forEach((person) => select.append(new Option(person.name, person.uid)));
    select.value = uid;
    select.addEventListener('change', syncAssigneeNames);
    label.append(select); row.append(label);
    if (index >= 2) {
      const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'secondary-button'; remove.textContent = 'הסר';
      remove.setAttribute('aria-label', `הסר ${roleLabel}`);
      remove.addEventListener('click', () => renderAssigneeSelectors(selectedAssigneeUids().filter((_, position) => position !== index)));
      row.append(remove);
    }
    container.append(row);
  });
  syncAssigneeNames();
}
document.querySelector('#add-assignee').addEventListener('click', () => renderAssigneeSelectors([...selectedAssigneeUids(), '']));
function updateUserSelectors() {
  renderAssigneeSelectors();
  const filter = document.querySelector('#closed-filter');
  const previous = filter.value;
  filter.replaceChildren(new Option('כל הטכנאים', ''));
  technicians.forEach((person) => filter.append(new Option(`${person.name}${person.deleted ? ' (נמחק)' : person.active ? '' : ' (הוסר)'}`, person.uid)));
  filter.value = previous;
}
document.querySelector("#closed-filter").addEventListener("change", renderIssueLists);
for (const id of ["open-issue-search", "pending-issue-search", "closed-issue-search"]) document.getElementById(id).addEventListener("input", renderIssueLists);
function renderUsers() {
  const list = document.querySelector("#users-list");
  const removedList = document.querySelector("#removed-users-list");
  list.replaceChildren();
  removedList.replaceChildren();
  if (!technicians.some((person) => person.active && !person.deleted)) list.textContent = "אין טכנאים פעילים. צור משתמש חדש.";
  if (!technicians.some((person) => !person.active && !person.deleted)) removedList.textContent = "אין משתמשים שהוסרו.";
  technicians.forEach((person) => {
    if (person.deleted) return;
    const card = document.createElement("div"); card.className = "issue-item";
    const title = document.createElement("strong"); title.textContent = `${person.name} · ${person.active ? "פעיל" : "הוסר"}`;
    const email = document.createElement("span"); email.textContent = person.email || person.uid;
    const assigned = cloudIssues.filter((issue) => issue.assigneeUid === person.uid);
    const closed = assigned.filter((issue) => issue.status === "closed");
    const history = document.createElement("p");
    history.textContent = `תקלות פתוחות: ${assigned.filter((issue) => issue.status === "open").length} · לסגירה: ${assigned.filter((issue) => issue.status === "pendingReview").length} · סגורות: ${closed.length} · מספרים סידוריים: ${closed.map((issue) => issue.sheet.serial).join(", ") || "אין"}`;
    const view = document.createElement("button"); view.type = "button"; view.className = "secondary-button"; view.textContent = "הצג דפים סגורים";
    view.addEventListener("click", () => { document.querySelector("#closed-filter").value = person.uid; location.hash = "closed"; renderIssueLists(); });
    const action = document.createElement("button"); action.type = "button"; action.className = "secondary-button";
    action.textContent = person.active ? "מחק משתמש" : "שחזר משתמש";
    action.addEventListener("click", async () => {
      if (person.active && assigned.some((issue) => issue.status === "open")) {
        setStatus("#user-status", `יש לשייך מחדש את התקלות הפתוחות של ${person.name} בדף ״תקלות פתוחות״ לפני הסרת המשתמש.`, true);
        return;
      }
      action.disabled = true;
      try {
        await setTechnicianActive(person.uid, !person.active);
        setStatus("#user-status", person.active ? `${person.name} הוסר מהמערכת והגישה שלו נחסמה. היסטוריית התקלות נשמרה.` : `${person.name} שוחזר.`);
      }
      catch (error) { setStatus("#user-status", `שינוי המשתמש נכשל: ${error.message}`, true); }
      finally { action.disabled = false; }
    });
    card.append(title, email, history, view, action);
    if (!person.active) {
      const destroy = document.createElement("button"); destroy.type = "button"; destroy.className = "secondary-button danger-button";
      destroy.textContent = "מחק סופית";
      destroy.addEventListener("click", async () => {
        if (!window.confirm(`למחוק סופית את חשבון ההתחברות של ${person.name} (${person.email || person.uid})? לא ניתן לשחזר אותו; תקלות סגורות יישמרו.`)) return;
        destroy.disabled = true;
        setStatus("#user-status", `מוחק את חשבון ההתחברות של ${person.name}…`);
        try { await deleteTechnicianPermanently(person.uid); setStatus("#user-status", `${person.name} נמחק סופית ממסד המשתמשים. היסטוריית התקלות נשמרה.`); }
        catch (error) { setStatus("#user-status", `המחיקה הסופית נכשלה: ${error.message}`, true); }
        finally { destroy.disabled = false; }
      });
      card.append(destroy);
    }
    (person.active ? list : removedList).append(card);
  });
}
watchTechnicians((items) => { technicians = items.sort((a, b) => a.name.localeCompare(b.name, "he")); updateReportData({ users: technicians }); updateUserSelectors(); renderUsers(); renderIssueLists(); }, (error) => {
  setStatus("#user-status", `טעינת המשתמשים נכשלה: ${error.message}`, true);
});
const userForm = document.querySelector("#create-user-form");
userForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = userForm.querySelector('button[type="submit"]'); button.disabled = true;
  setStatus("#user-status", "יוצר משתמש…");
  try {
    await createTechnician(userForm.elements.name.value, userForm.elements.personalNumber.value, "");
    userForm.reset(); setStatus("#user-status", "הטכנאי נוצר. מסור לו את המספר האישי.");
  } catch (error) { setStatus("#user-status", `יצירת המשתמש נכשלה: ${error.message}`, true); }
  finally { button.disabled = false; }
});
const locallyClosed = new Set(EzEizoData.readClosed().map((item) => item.id));
const legacy = EzEizoData.readIssues().filter((item) => item?.id && item.id !== "example" && !locallyClosed.has(item.id));
if (legacy.length) {
  setStatus("#legacy-status", `נמצאו ${legacy.length} תקלות מקומיות ישנות. מעביר אותן למסד המשותף…`);
  let imported = 0;
  for (const item of legacy) {
    try { await createIssue(EzEizoData.normalizeSheet(item), { uid: "", name: "" }, true); imported++; }
    catch (error) { setStatus("#legacy-status", `הועברו ${imported} מתוך ${legacy.length}. שגיאה: ${error.message}`, true); break; }
  }
  if (imported === legacy.length) setStatus("#legacy-status", `${imported} תקלות מקומיות הועברו למסד המשותף.`);
}
initReports();
let initialProfilesApplied = false;
initAircraftConstants({ user: managerUser, onProfilesLoaded: () => {
  if (!initialProfilesApplied && selections.squadron && selections.aircraft) {
    draft.constants = constantsForAircraft(selections.squadron, selections.aircraft);
    renderConstants();
  }
  initialProfilesApplied = true;
} });
watchKeyLoads((loads) => updateReportData({ loads }), (error) => { setStatus("#report-error", `טעינת הטענות נכשלה: ${error.message}`, true); });
renderConstants();
renderActions();
renderIssueLists();
showManagerView();
revealApp();
const clearDispatcherSignature = attachSignaturePad(document.querySelector("#dispatcher-signature"), {
  initial: draft.dispatcherSignature,
  onChange: (value) => { draft.dispatcherSignature = value; saveDraft(); }
});
document.querySelector("#clear-dispatcher-signature").addEventListener("click", clearDispatcherSignature);
