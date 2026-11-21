import { watchAircraftConstants, saveAircraftConstants } from "../access-sync.js?v=1.6.3";
import { SQUADRONS } from "./catalog.js?v=1.6.3";
import { downloadReportPdf } from "./report-pdf.js?v=1.6.3";
import { downloadFile } from "./report-pdf.js?v=1.6.3";
import { createFormPdf } from "./form-pdf.js?v=1.6.3";
import { zipFiles } from "./zip.js?v=1.6.3";

const $ = (selector) => document.querySelector(selector);
const byDate = (value) => value ? new Date(value).toLocaleString("he-IL", { dateStyle: "short", timeStyle: "short" }) : "—";
const localDateTime = (date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}T${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}`;
const option = (label, value = label) => new Option(label, value);
const element = (tag, content, className) => { const node = document.createElement(tag); if (content != null) node.textContent = content; if (className) node.className = className; return node; };

let aircraftProfiles = new Map();
let selectedAircraft = "";
let selectedSquadron = "";
let editorDirty = false;
let reportIssues = [];
let reportLoads = [];
let reportUsers = [];
let reportRows = [];
let reportBusy = false;
let squadronSummary = false;

function uniqueForms(rows) {
  return [...new Map(rows.map((row) => [`${row.type}:${row.id}`, row])).values()];
}

function formForRow(row) {
  const form = (row.type === "key" ? reportLoads : reportIssues).find((item) => item.id === row.id);
  if (!form) throw new Error(`הטופס ${row.serial} אינו זמין כרגע. רענן את הדף ונסה שוב.`);
  return form;
}

function formFilename(row) {
  const opened = new Date(row.openedAt);
  const date = Number.isNaN(opened.getTime()) ? "תאריך לא ידוע" : `${String(opened.getDate()).padStart(2, "0")}-${String(opened.getMonth() + 1).padStart(2, "0")}-${opened.getFullYear()}`;
  const title = row.type === "key" ? `הטענת מפתחות ${row.serial} ${date}` : `תקלה - ${row.serial} - ${date}`;
  return `${title}.pdf`.replace(/[\\/:*?"<>|]/g, "-");
}

function baseConstants() {
  return structuredClone(EzEizoData.defaultSheet.constants);
}

export function constantsForAircraft(squadron, aircraft) {
  const values = aircraftProfiles.get(`${squadron}_${aircraft}`)?.constants || {};
  return Object.fromEntries(Object.entries(baseConstants()).map(([group, entries]) => [group, entries.map(([name, initial]) => [name, String(values[group]?.[name] ?? initial)])]));
}

function selectOptions(select, values, placeholder) {
  const current = select.value;
  select.replaceChildren(option(placeholder, ""), ...values.map((value) => option(value)));
  select.value = values.includes(current) ? current : "";
}

function renderAircraftList() {
  const list = $("#constants-aircraft-list"); list.replaceChildren();
  if (!selectedSquadron) { list.textContent = "בחר טייסת כדי להציג את המטוסים שלה."; return; }
  SQUADRONS[selectedSquadron].aircraft.forEach((aircraft) => {
    const button = element("button", `מטוס ${aircraft}${aircraftProfiles.has(`${selectedSquadron}_${aircraft}`) ? " · עודכן" : ""}`, "secondary-button");
    button.type = "button";
    if (selectedAircraft === aircraft) button.setAttribute("aria-current", "true");
    button.addEventListener("click", () => { selectedAircraft = aircraft; editorDirty = false; $("#constants-picker").hidden = true; renderConstantsEditor(); });
    list.append(button);
  });
}

function renderConstantsEditor() {
  const section = $("#constants-editor"); section.hidden = !selectedAircraft;
  if (!selectedAircraft) return;
  $("#constants-aircraft-title").textContent = `טייסת ${selectedSquadron} · מטוס ${selectedAircraft}`;
  const container = $("#constants-fields"); container.replaceChildren();
  Object.entries(constantsForAircraft(selectedSquadron, selectedAircraft)).forEach(([group, entries]) => {
    const card = element("section", null, "constant-group"); card.dataset.group = group;
    card.append(element("h3", group));
    const grid = element("div", null, "manager-constant-list");
    entries.forEach(([name, value]) => {
      const field = element("label", null, "manager-constant-row");
      const input = document.createElement("input"); input.type = "text"; input.inputMode = "numeric"; input.maxLength = 80; input.value = value; input.dataset.name = name;
      input.addEventListener("input", () => { editorDirty = true; });
      field.append(element("span", name), input); grid.append(field);
    });
    card.append(grid); container.append(card);
  });
  const profile = aircraftProfiles.get(`${selectedSquadron}_${selectedAircraft}`);
  $("#constants-status").textContent = profile ? `עודכן לאחרונה: ${byDate(profile.updatedAt)}` : "ערכי הפתיחה זהים לקבועים שנמסרו לכל המטוסים.";
  delete $("#constants-status").dataset.state;
}

export function initAircraftConstants({ user, onProfilesLoaded }) {
  const squadronSelect = $("#constants-squadron");
  selectOptions(squadronSelect, Object.keys(SQUADRONS), "בחר טייסת");
  squadronSelect.addEventListener("change", () => {
    selectedSquadron = squadronSelect.value; selectedAircraft = ""; editorDirty = false;
    $("#constants-editor").hidden = true; renderAircraftList();
  });
  $("#constants-close").addEventListener("click", closeConstantsEditor);
  $("#constants-save").addEventListener("click", async () => {
    if (!selectedSquadron || !SQUADRONS[selectedSquadron].aircraft.includes(selectedAircraft)) return;
    const values = {};
    for (const group of $("#constants-fields").querySelectorAll("[data-group]")) {
      values[group.dataset.group] = {};
      for (const input of group.querySelectorAll("input")) {
        const value = input.value.trim();
        if (!/^-?\d{1,18}$/.test(value)) { $("#constants-status").textContent = `יש להזין מספר תקין עבור ${group.dataset.group} ${input.dataset.name}.`; $("#constants-status").dataset.state = "error"; input.focus(); return; }
        values[group.dataset.group][input.dataset.name] = value;
      }
    }
    const button = $("#constants-save"); button.disabled = true;
    try { await saveAircraftConstants(selectedSquadron, selectedAircraft, values, user.uid); editorDirty = false; $("#constants-status").textContent = "הקבועים נשמרו למטוס. תקלות חדשות שייפתחו למטוס יקבלו אותם."; delete $("#constants-status").dataset.state; }
    catch (error) { $("#constants-status").textContent = `השמירה נכשלה: ${error.message}`; $("#constants-status").dataset.state = "error"; }
    finally { button.disabled = false; }
  });
  watchAircraftConstants((profiles) => {
    aircraftProfiles = new Map(profiles.map((profile) => [profile.id, profile]));
    renderAircraftList(); if (selectedAircraft && !editorDirty) renderConstantsEditor();
    onProfilesLoaded?.();
  }, (error) => { $("#constants-aircraft-list").textContent = `טעינת קבועים נכשלה: ${error.message}`; $("#constants-aircraft-list").dataset.state = "error"; });
  renderAircraftList();
}

export function closeConstantsEditor() {
  if (!selectedAircraft) return false;
  selectedAircraft = ""; editorDirty = false; $("#constants-editor").hidden = true; $("#constants-picker").hidden = false; renderAircraftList();
  return true;
}

function statusLabel(status) { return ({ open: "פתוח", pendingReview: "ממתין לסגירה", closed: "סגור" })[status] || status; }
function aircraftNumber(squadron, value) {
  const number = String(value ?? "").trim();
  return String(squadron) === "109" && /^\d{1,2}$/.test(number) ? number.padStart(3, "0") : number;
}
function reportRecords() {
  const faults = reportIssues.map((issue) => ({
    id: issue.id, type: "issue", kind: "תקלה", serial: issue.sheet?.serial || "—", squadron: issue.sheet?.squadron || "", aircraft: aircraftNumber(issue.sheet?.squadron, issue.sheet?.aircraft), technician: (issue.assigneeNames || [issue.assigneeName]).join(" · ") || "—", technicianUids: issue.assigneeUids || [issue.assigneeUid], status: issue.status, title: issue.sheet?.fault || "—", openedAt: issue.createdAt, closedAt: issue.closed?.state?.closedAt || "", eventAt: issue.status === "closed" ? (issue.reviewedAt || issue.closed?.state?.closedAt || issue.createdAt) : issue.status === "pendingReview" ? (issue.closed?.state?.closedAt || issue.createdAt) : (issue.reopenedAt || issue.createdAt)
  }));
  const keys = reportLoads.flatMap((load) => (load.aircraftIds?.length ? load.aircraftIds : load.rows?.map((row) => row.aircraft) || []).map((aircraft) => ({
    id: load.id, type: "key", kind: "מפתחות", serial: load.serial || "—", squadron: load.squadron, aircraft: aircraftNumber(load.squadron, aircraft), technician: (load.teamNames || []).join(" · ") || "—", technicianUids: load.teamUids || [], status: load.status, title: `הטענת מפתחות ${load.period || ""}`.trim(), openedAt: load.createdAt, closedAt: load.completedAt || "", eventAt: load.status === "closed" ? (load.completedAt || load.createdAt || load.date) : (load.reopenedAt || load.createdAt || load.date)
  })));
  return [...faults, ...keys];
}

const reportTitles = { journal: "יומן פעילות", "squadron-journal": "יומן טייסת", aircraft: "היסטוריית מטוס", technician: "היסטוריית טכנאי", "issue-open": "תקלות פתוחות", "issue-closed": "תקלות סגורות", squadron: "סיכום טייסת", pending: "דפים הממתינים לסגירה" };
function reportState() {
  const selectedKind = $("#report-kind").value;
  const kind = selectedKind === "squadron-journal" && squadronSummary ? "squadron" : selectedKind;
  const squadron = $("#report-squadron").value;
  const mode = kind === "technician" ? "technician" : ["journal", "issue-open", "issue-closed", "pending"].includes(kind) ? "multi" : "aircraft";
  const aircraft = [...$("#report-aircraft-options").querySelectorAll('input:checked')].map((box) => box.value);
  const technicians = [...$("#report-technician-options").querySelectorAll('input:checked')].map((box) => box.value);
  const technicianSquadrons = [...$("#report-technician-squadron-options").querySelectorAll('input:checked')].map((box) => box.value);
  const technicianAircraft = [...$("#report-technician-aircraft-options").querySelectorAll('input:checked')].map((box) => box.value);
  const from = new Date($("#report-from").value).getTime();
  const to = new Date($("#report-to").value).getTime();
  const allDates = kind === "squadron" || $("#report-all-dates").checked;
  const status = kind === "squadron" ? "all" : kind === "pending" ? "pendingReview" : kind === "issue-open" ? "open" : kind === "issue-closed" ? "closed" : $("#report-status").value;
  const type = ["pending", "squadron"].includes(kind) ? "issue" : ["issue-open", "issue-closed"].includes(kind) ? "all" : $("#report-form").value;
  const error = !kind ? "בחר תחילה סוג דוח." : !allDates && (!Number.isFinite(from) || !Number.isFinite(to) || from > to) ? "בחר טווח תאריכים תקין." : mode === "aircraft" && !squadron ? "בחר טייסת כדי להציג דוח." : mode === "aircraft" && kind !== "squadron" && !aircraft.length ? "בחר לפחות מטוס אחד, או השתמש בכפתור בחירת כל המטוסים." : kind === "aircraft" && aircraft.length !== 1 ? "להיסטוריית מטוס יש לבחור מטוס אחד בלבד." : mode === "technician" && !technicians.length ? "בחר לפחות טכנאי אחד מהרשימה." : ["technician", "multi"].includes(mode) && !technicianSquadrons.length ? "בחר לפחות טייסת אחת." : ["technician", "multi"].includes(mode) && !technicianAircraft.length ? "בחר לפחות מטוס אחד, או השתמש בכפתור בחירת כל המטוסים." : "";
  return { squadron, mode, aircraft, technicians, technicianSquadrons, technicianAircraft, from, to, allDates, kind, status, type, error };
}

function makeReportRows(state) {
  if (state.error) return [];
  return reportRecords().filter((row) => (state.mode === "aircraft" ? row.squadron === state.squadron && (state.kind === "squadron" || state.aircraft.includes(row.aircraft)) : (state.mode === "multi" || row.technicianUids.some((uid) => state.technicians.includes(uid))) && state.technicianAircraft.includes(`${row.squadron}|${row.aircraft}`))
    && (state.type === "all" || row.type === state.type)
    && (state.status === "all" || row.status === state.status)
    && (state.allDates || (new Date(row.eventAt).getTime() >= state.from && new Date(row.eventAt).getTime() <= state.to)))
    .sort((a, b) => state.kind === "aircraft"
      ? a.squadron.localeCompare(b.squadron, "he", { numeric: true }) || a.aircraft.localeCompare(b.aircraft, "he", { numeric: true }) || new Date(b.eventAt) - new Date(a.eventAt)
      : new Date(b.eventAt) - new Date(a.eventAt));
}

function reportFilename(state = reportState(), producedAt = new Date()) {
  const timestamp = `${producedAt.getFullYear()}-${String(producedAt.getMonth() + 1).padStart(2, "0")}-${String(producedAt.getDate()).padStart(2, "0")} ${String(producedAt.getHours()).padStart(2, "0")}-${String(producedAt.getMinutes()).padStart(2, "0")}`;
  return `דוח - ${reportTitles[state.kind]} - ${timestamp}`.replace(/[\\/:*?"<>|]/g, "-");
}

function renderReport() {
  const state = reportState();
  const error = $("#report-error"); error.textContent = state.error; error.dataset.state = state.error ? "error" : "";
  $("#report-title").textContent = reportTitles[state.kind] || "בחר סוג דוח";
  const names = state.technicians.map((uid) => reportUsers.find((user) => user.uid === uid)?.name).filter(Boolean);
  const multiSubject = `${state.technicianSquadrons.length ? `טייסות ${state.technicianSquadrons.join(", ")}` : "בחר טייסות"} · ${state.technicianAircraft.length ? `${state.technicianAircraft.length} מטוסים נבחרו` : "בחר מטוסים"}`;
  const subject = state.mode === "technician" ? `טכנאים: ${names.join(", ") || "—"} · ${multiSubject}` : state.mode === "multi" ? multiSubject : state.kind === "squadron" ? `טייסת ${state.squadron || "—"} · כל המטוסים` : `טייסת ${state.squadron || "—"} · ${state.aircraft.length ? `מטוסים ${state.aircraft.join(", ")}` : "בחר מטוסים"}`;
  $("#report-period").textContent = state.kind === "squadron" ? `${subject} · כל התקופות · כל התקלות` : `${subject} · ${state.allDates ? "כל התקופות" : `${byDate(state.from)} עד ${byDate(state.to)}`} · ${state.type === "all" ? "תקלות והטענות מפתחות" : state.type === "issue" ? "תקלות" : "מפתחות"} · ${state.status === "all" ? "כל המצבים" : statusLabel(state.status)}`;
  reportRows = makeReportRows(state);
  const forms = uniqueForms(reportRows);
  $("#report-count").textContent = state.error ? "" : `${reportRows.length} רשומות · ${forms.length} טפסים`;
  $("#report-pdf").disabled = $("#report-print").disabled = $("#report-csv").disabled = $("#report-download-all").disabled = Boolean(state.error || !reportRows.length || reportBusy);
  const summary = $("#report-summary"); summary.replaceChildren();
  if (!state.error) {
    const counts = [["תקלות", reportRows.filter((r) => r.type === "issue").length], ["מטוסים בהטענות", reportRows.filter((r) => r.type === "key").length], ["פתוח", reportRows.filter((r) => r.status === "open").length], ["ממתין לסגירה", reportRows.filter((r) => r.status === "pendingReview").length], ["סגור", reportRows.filter((r) => r.status === "closed").length]];
    counts.forEach(([label, amount]) => { const item = element("div", null, "report-stat"); item.append(element("strong", String(amount)), element("span", label)); summary.append(item); });
  }
  const results = $("#report-results"); results.replaceChildren();
  if (state.error || !reportRows.length) { results.textContent = state.error ? "" : `אין רשומות שמתאימות לסינון הזה.${state.allDates ? "" : " נסה לבחור ״כל התקופות״ כדי לראות גם טפסים ישנים."}`; return; }
  let previousAircraft = "";
  reportRows.forEach((row) => {
    if (state.kind === "aircraft" && previousAircraft !== `${row.squadron}|${row.aircraft}`) {
      const group = element("h3", `טייסת ${row.squadron} · מטוס ${row.aircraft}`, "report-aircraft-group");
      results.append(group); previousAircraft = `${row.squadron}|${row.aircraft}`;
    }
    const card = element("article", null, "report-entry");
    const heading = element("div", null, "report-entry-heading");
    const title = element("h3", `${row.kind} · מטוס ${row.aircraft}`);
    heading.append(title, element("span", statusLabel(row.status), "report-entry-status"));
    const description = element("p", row.title, "report-entry-description");
    const details = element("dl", null, "report-entry-details");
    [[row.type === "key" ? "מספר הטענה" : "מספר תקלה", row.serial], ["טכנאי / צוות", row.technician], ["מועד הפעילות", byDate(row.eventAt)], ["נפתח", byDate(row.openedAt)], ["נסגר", byDate(row.closedAt)]].forEach(([label, value]) => {
      const pair = element("div"); pair.append(element("dt", label), element("dd", value)); details.append(pair);
    });
    const download = element("button", "הורד טופס PDF", "secondary-button"); download.type = "button";
    download.disabled = reportBusy;
    download.addEventListener("click", async () => {
      const status = $("#report-download-status");
      download.disabled = true; status.textContent = `יוצר PDF לטופס ${row.serial}…`; delete status.dataset.state;
      try {
        const blob = await createFormPdf(formForRow(row), row.type);
        downloadFile(blob, formFilename(row));
        status.textContent = `טופס ${row.serial} הורד כ־PDF.`;
      } catch (error) { status.textContent = `הורדת הטופס נכשלה: ${error.message}`; status.dataset.state = "error"; }
      finally { download.disabled = false; }
    });
    const actions = element("div", null, "report-entry-actions"); actions.append(download);
    card.append(heading, description, details, actions); results.append(card);
  });
}

function refreshReportSelectors() {
  const squadron = $("#report-squadron").value;
  $("#report-aircraft-fields-picker").hidden = !squadron;
  const selectedKind = $("#report-kind").value;
  const kind = selectedKind === "squadron-journal" && squadronSummary ? "squadron" : selectedKind;
  const isTechnician = kind === "technician";
  const isMultiSquadron = isTechnician || ["journal", "issue-open", "issue-closed", "pending"].includes(kind);
  $("#report-after-kind").hidden = !kind;
  $("#report-aircraft-fields").hidden = isMultiSquadron;
  $("#report-aircraft-fields-picker").hidden = !squadron || kind === "squadron";
  $("#report-squadron-summary").hidden = selectedKind !== "squadron-journal";
  $("#report-squadron-summary").disabled = !squadron;
  $("#report-squadron-summary").textContent = squadronSummary ? "חזרה ליומן הטייסת" : "סיכום טייסת";
  $("#report-select-all-squadron-aircraft").hidden = kind === "aircraft";
  $("#report-technician-field").hidden = !isMultiSquadron;
  $("#report-technician-header").hidden = !isTechnician;
  $("#report-technician-aircraft-fields").hidden = !$("#report-technician-squadron-options").querySelector('input:checked');
  $("#report-view-dates").hidden = kind === "squadron";
  $("#report-all-dates-field").hidden = kind === "squadron";
  $("#report-from").disabled = $("#report-to").disabled = $("#report-all-dates").checked;
  const issueKind = ["pending", "issue-open", "issue-closed", "squadron"].includes(kind);
  $("#report-form").closest("label").hidden = issueKind;
  $("#report-status").closest("label").hidden = issueKind;
  $("#report-form-status").hidden = issueKind;
  $("#report-help").hidden = kind === "squadron";
}

export function updateReportData({ issues, loads, users } = {}) {
  if (issues) reportIssues = issues;
  if (loads) reportLoads = loads;
  if (users) { reportUsers = users; renderTechnicianOptions(); }
  refreshReportSelectors(); renderReport();
}

function renderTechnicianOptions() {
  const options = $("#report-technician-options");
  const previous = new Set([...options.querySelectorAll('input:checked')].map((box) => box.value));
  const search = $("#report-technician-search").value.trim().toLocaleLowerCase("he");
  options.replaceChildren();
  for (const user of reportUsers) {
    const label = element("label"), box = document.createElement("input");
    label.hidden = Boolean(search && !user.name.toLocaleLowerCase("he").includes(search));
    box.type = "checkbox"; box.value = user.uid; box.checked = previous.has(user.uid);
    label.append(box, document.createTextNode(` ${user.name}${user.active ? "" : " (הוסר)"}`)); options.append(label);
  }
  if (search && ![...options.children].some((label) => !label.hidden)) options.append(element("p", "לא נמצאו טכנאים.", "section-note"));
  updateTechnicianNames();
}

function updateTechnicianNames() {
  const chosen = [...$("#report-technician-options").querySelectorAll('input:checked')].map((box) => reportUsers.find((user) => user.uid === box.value)?.name).filter(Boolean);
  $("#report-technician-summary").textContent = chosen.length ? chosen.length <= 2 ? chosen.join(" · ") : `${chosen.length} טכנאים נבחרו` : "בחר טכנאים";
}

export function initReports() {
  const managerTitle = document.title;
  window.addEventListener("hashchange", () => { if (location.hash !== "#reports") document.title = managerTitle; });
  selectOptions($("#report-squadron"), Object.keys(SQUADRONS), "בחר טייסת");
  const squadronOptions = $("#report-technician-squadron-options");
  for (const name of Object.keys(SQUADRONS)) {
    const label = element("label"); const box = document.createElement("input");
    box.type = "checkbox"; box.value = name;
    label.append(box, document.createTextNode(` טייסת ${name}`)); squadronOptions.append(label);
  }
  const setupAircraftPicker = ({ squadronSelector, optionsSelector, summarySelector, pickerSelector, selectAllSelector, clearSelector }) => {
    const options = $(optionsSelector);
    const updateSummary = () => {
      const checked = [...options.querySelectorAll("input:checked")].map((box) => box.value);
      const total = options.querySelectorAll("input").length;
      $(summarySelector).textContent = !checked.length ? "בחר מטוסים" : $("#report-kind").value === "aircraft" ? `מטוס ${checked[0]}` : checked.length === total ? `כל המטוסים (${total})` : checked.length <= 3 ? `מטוסים ${checked.join(", ")}` : `${checked.length} מטוסים נבחרו`;
    };
    const renderOptions = () => {
      options.replaceChildren();
      for (const aircraft of SQUADRONS[$(squadronSelector).value]?.aircraft || []) {
        const label = element("label");
        const box = document.createElement("input"); box.type = $("#report-kind").value === "aircraft" ? "radio" : "checkbox"; box.name = box.type === "radio" ? "history-aircraft" : ""; box.value = aircraft;
        label.append(box, document.createTextNode(` מטוס ${aircraft}`)); options.append(label);
      }
      $(pickerSelector).open = false;
      updateSummary(); renderReport();
    };
    $(squadronSelector).addEventListener("change", renderOptions);
    options.addEventListener("change", (event) => {
      if (event.target.matches("input") && event.target.checked && $("#report-kind").value === "aircraft") {
        options.querySelectorAll("input").forEach((box) => { if (box !== event.target) box.checked = false; });
        $(pickerSelector).open = false;
      }
      updateSummary(); renderReport();
    });
    $(selectAllSelector).addEventListener("click", () => { options.querySelectorAll("input").forEach((box) => { box.checked = true; }); updateSummary(); renderReport(); });
    $(clearSelector).addEventListener("click", () => { options.querySelectorAll("input").forEach((box) => { box.checked = false; }); updateSummary(); renderReport(); });
    return updateSummary;
  };
  const now = new Date();
  $("#report-to").value = localDateTime(now);
  $("#report-from").value = localDateTime(new Date(now.getTime() - 24 * 60 * 60 * 1000));
  $("#view-reports").querySelectorAll("input, select").forEach((control) => control.addEventListener("change", () => { refreshReportSelectors(); renderReport(); }));
  const updateAircraftSummary = setupAircraftPicker({ squadronSelector: "#report-squadron", optionsSelector: "#report-aircraft-options", summarySelector: "#report-aircraft-summary", pickerSelector: "#report-aircraft-picker", selectAllSelector: "#report-select-all-squadron-aircraft", clearSelector: "#report-clear-squadron-aircraft" });
  $("#report-technician-search").addEventListener("input", renderTechnicianOptions);
  $("#report-technician-options").addEventListener("change", () => { updateTechnicianNames(); renderReport(); });
  const technicianOptions = $("#report-technician-aircraft-options");
  const updateTechnicianSummary = () => {
    const chosen = [...technicianOptions.querySelectorAll("input:checked")].map((box) => box.value);
    const total = technicianOptions.querySelectorAll("input").length;
    $("#report-technician-aircraft-summary").textContent = !chosen.length ? "בחר מטוסים" : chosen.length === total ? `כל המטוסים (${total})` : `${chosen.length} מטוסים נבחרו`;
  };
  const updateTechnicianSquadrons = () => {
    const names = [...squadronOptions.querySelectorAll("input:checked")].map((box) => box.value);
    $("#report-technician-squadron-summary").textContent = !names.length ? "בחר טייסות" : names.length === Object.keys(SQUADRONS).length ? "כל הטייסות" : names.map((name) => `טייסת ${name}`).join(" · ");
    const previous = new Set([...technicianOptions.querySelectorAll("input:checked")].map((box) => box.value));
    technicianOptions.replaceChildren();
    for (const name of names) for (const aircraft of SQUADRONS[name].aircraft) {
      const label = element("label"); const box = document.createElement("input");
      box.type = "checkbox"; box.value = `${name}|${aircraft}`; box.checked = previous.has(box.value);
      label.append(box, document.createTextNode(` ${name} · ${aircraft}`)); technicianOptions.append(label);
    }
    $("#report-technician-aircraft-picker").open = false;
    updateTechnicianSummary(); refreshReportSelectors(); renderReport();
  };
  squadronOptions.addEventListener("change", updateTechnicianSquadrons);
  $("#report-select-all-squadrons").addEventListener("click", () => { squadronOptions.querySelectorAll("input").forEach((box) => { box.checked = true; }); updateTechnicianSquadrons(); });
  $("#report-clear-squadrons").addEventListener("click", () => { squadronOptions.querySelectorAll("input").forEach((box) => { box.checked = false; }); updateTechnicianSquadrons(); });
  technicianOptions.addEventListener("change", () => { updateTechnicianSummary(); renderReport(); });
  $("#report-select-all-aircraft").addEventListener("click", () => { technicianOptions.querySelectorAll("input").forEach((box) => { box.checked = true; }); updateTechnicianSummary(); renderReport(); });
  $("#report-clear-aircraft").addEventListener("click", () => { technicianOptions.querySelectorAll("input").forEach((box) => { box.checked = false; }); updateTechnicianSummary(); renderReport(); });
  $("#report-technician-all").addEventListener("click", () => {
    squadronOptions.querySelectorAll("input").forEach((box) => { box.checked = true; });
    updateTechnicianSquadrons();
    technicianOptions.querySelectorAll("input").forEach((box) => { box.checked = true; });
    updateTechnicianSummary(); renderReport();
  });
  $("#report-kind").addEventListener("change", () => {
    squadronSummary = false;
    const kind = $("#report-kind").value;
    if (kind === "aircraft") {
      const checked = [...$("#report-aircraft-options").querySelectorAll('input:checked')];
      checked.slice(1).forEach((box) => { box.checked = false; });
    }
    $("#report-aircraft-options").querySelectorAll("input").forEach((box) => {
      box.type = kind === "aircraft" ? "radio" : "checkbox";
      box.name = kind === "aircraft" ? "history-aircraft" : "";
    });
    updateAircraftSummary();
    refreshReportSelectors(); renderReport();
  });
  $("#report-squadron-summary").addEventListener("click", () => {
    if (!$("#report-squadron").value) return;
    squadronSummary = !squadronSummary;
    refreshReportSelectors(); renderReport();
  });
  $("#report-print").addEventListener("click", () => {
    const state = reportState();
    if (state.error || !reportRows.length) return;
    const filename = reportFilename(state);
    document.title = filename;
    // Keep the title throughout the native printer's second Save PDF dialog.
    requestAnimationFrame(() => window.print());
  });
  $("#report-pdf").addEventListener("click", async () => {
    const state = reportState();
    if (state.error || !reportRows.length) return;
    const button = $("#report-pdf"), status = $("#report-download-status");
    button.disabled = true; status.textContent = "יוצר קובץ PDF…"; delete status.dataset.state;
    try {
      await downloadReportPdf({ name: reportFilename(state), title: reportTitles[state.kind], period: $("#report-period").textContent, rows: reportRows, statusLabel, byDate });
      status.textContent = "קובץ ה־PDF הורד בשם הדוח.";
    } catch (error) { status.textContent = `יצירת ה־PDF נכשלה: ${error.message}`; status.dataset.state = "error"; }
    finally { button.disabled = false; }
  });
  $("#report-csv").addEventListener("click", () => {
    const cell = (value) => `"${String(value ?? "").replace(/^[=+@\-\t\r]/, "'$&").replaceAll('"', '""')}"`;
    const lines = [["מועד", "טייסת", "סוג", "מטוס", "מספר טופס", "עבודה", "טכנאי / צוות", "מצב", "שעת פתיחה", "שעת סגירה"], ...reportRows.map((row) => [byDate(row.eventAt), row.squadron, row.kind, row.aircraft, row.serial, row.title, row.technician, statusLabel(row.status), byDate(row.openedAt), byDate(row.closedAt)])];
    const url = URL.createObjectURL(new Blob(["\uFEFF", lines.map((line) => line.map(cell).join(",")).join("\r\n")], { type: "text/csv;charset=utf-8" }));
    const link = document.createElement("a"); link.href = url; link.download = `${reportFilename()}.csv`; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
  });
  const confirm = $("#report-bulk-confirm");
  $("#report-confirm-no").addEventListener("click", () => confirm.close("no"));
  $("#report-confirm-yes").addEventListener("click", () => confirm.close("yes"));
  $("#report-download-all").addEventListener("click", async () => {
    const state = reportState();
    if (state.error || !reportRows.length || reportBusy) return;
    const forms = uniqueForms(reportRows);
    if (forms.length > 5) {
      confirm.showModal();
      const accepted = await new Promise((resolve) => confirm.addEventListener("close", () => resolve(confirm.returnValue === "yes"), { once: true }));
      if (!accepted) return;
    }
    reportBusy = true; renderReport();
    const status = $("#report-download-status"); delete status.dataset.state;
    try {
      const files = [], filenames = new Set();
      for (const [index, row] of forms.entries()) {
        status.textContent = `מכין טפסים ל־ZIP: ${index + 1} מתוך ${forms.length}…`;
        await new Promise((resolve) => setTimeout(resolve, 0));
        const base = formFilename(row); let name = base, number = 2;
        while (filenames.has(name)) name = base.replace(/\.pdf$/, ` (${number++}).pdf`);
        filenames.add(name);
        files.push({ name, blob: await createFormPdf(formForRow(row), row.type) });
      }
      status.textContent = "אורז את הטפסים בקובץ ZIP…";
      downloadFile(await zipFiles(files), `${reportFilename(state)} - טפסים.zip`);
      status.textContent = `${files.length} טפסים הורדו בקובץ ZIP אחד.`;
    } catch (error) { status.textContent = `הורדת הטפסים נכשלה: ${error.message}`; status.dataset.state = "error"; }
    finally { reportBusy = false; renderReport(); }
  });
  refreshReportSelectors(); renderReport();
}
