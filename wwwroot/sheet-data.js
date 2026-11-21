import { draftState } from "./access-state.js";
(function () {
  const defaultSheet = {
    id: "example",
    serial: "דוגמה",
    shelter: "14",
    workshop: "",
    squadron: "105",
    location: "14/105",
    aircraft: "603",
    systemCode: "74A",
    systemName: "מכם",
    ewSystem: "גרניט",
    technicians: "רועי סויסה · צוק עינב",
    fault: "פירוק והרכבה אנטנת מכם",
    managerNotes: "",
    dispatcherSignature: "",
    constants: {
      HUD: [["k1", "0"], ["K2", "74"], ["K3", "829"], ["K4", "-949"], ["K5", "90"], ["K6", "-719"], ["K7", "0"], ["K8", "7"], ["K9", "-88890"]],
      CTVS: [["T1", "78"], ["T2", "789"], ["T3", "13"], ["T4", "781"], ["T5", "99"], ["T6", "11"], ["T7", "9"], ["T8", "0"], ["T9", "72939"]],
      CAMERA: [["HG1", "793"], ["HO2", "829"], ["VG3", "829"], ["VO4", "918"], ["CS5", "0"]],
      TFL: [["K10-K13", "0"], ["K14", "0"]],
      INS: [["MISC37", "893"], ["MISC38", "-930"], ["MISC39", "193"]]
    },
    actions: [
      "חתום על כלים רלוונטיים וגש לנקודת העבודה",
      "זמן הגעה לדת״ק",
      "קבל אישור עבודה על המטוס מר״צ חימוש / מפקד דת״ק",
      "בצע ביקורת כלים עם ר״צ / מפקד תורן – קוד שכיח 310 (משוך את הכלים לנקודת העבודה)",
      "וודא מטוס בטוח לאחזקה לפי כרך בטיחות המטוס",
      "לפתרון התקלה פעל עפ״י תהליך מרכזי דרג א׳",
      "עדכן ר״צ / מפקד דת״ק בסטטוס המטוס",
      "בצע ביקורת כלים בסיום העבודה – קוד שכיח 310",
      "בצע בדיקה להיעדר גופים זרים במקום העבודה – קוד שכיח 3100",
      "זכה את הכלים שנלקחו מהגף",
      "הצמד תג בר תיקון למכלול / צב״ד תקול",
      "זמן סיום התקלה"
    ]
  };

  const scalarKeys = ["serial", "location", "shelter", "workshop", "squadron", "aircraft", "systemCode", "systemName", "ewSystem", "technicians", "fault", "managerNotes"];
  function normalizeSheet(input) {
    if (!input || typeof input !== "object") return null;
    const sheet = structuredClone(defaultSheet);
    sheet.id = String(input.id || sheet.id).slice(0, 80);
    scalarKeys.forEach((key) => {
      if (typeof input[key] === "string") sheet[key] = input[key].slice(0, ["fault", "managerNotes"].includes(key) ? 2000 : 200);
    });
    if (typeof input.dispatcherSignature === "string" && input.dispatcherSignature.startsWith("data:image/png;base64,")) {
      sheet.dispatcherSignature = input.dispatcherSignature.slice(0, 200000);
    }
    if (typeof input.location === "string" && (typeof input.shelter !== "string" || typeof input.squadron !== "string")) {
      const parts = input.location.split("/");
      if (parts.length === 2) {
        if (typeof input.shelter !== "string") sheet.shelter = parts[0].trim();
        if (typeof input.squadron !== "string") sheet.squadron = parts[1].trim();
      }
    }
    sheet.location = [sheet.shelter === "מוסך" && sheet.workshop ? sheet.workshop : sheet.shelter, sheet.squadron].filter(Boolean).join("/");
    if (input.constants && typeof input.constants === "object") {
      Object.keys(sheet.constants).forEach((group) => {
        const source = input.constants[group];
        if (!Array.isArray(source)) return;
        sheet.constants[group] = sheet.constants[group].map(([label, value], index) => [label, String(source[index]?.[1] ?? value).slice(0, 80)]);
      });
    }
    return sheet;
  }

  function nextSerial() {
    const max = readList(ISSUES_KEY).reduce((n, item) => Math.max(n, Number(item.serial) || 0), 0);
    return String(max + 1).padStart(6, "0");
  }

  function encodeSheet(sheet) {
    const bytes = new TextEncoder().encode(JSON.stringify(normalizeSheet(sheet)));
    let binary = "";
    bytes.forEach((byte) => { binary += String.fromCharCode(byte); });
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }

  function decodeSheet(value) {
    try {
      if (!value || value.length > 30000 || !/^[A-Za-z0-9_-]+$/.test(value)) return null;
      const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
      const binary = atob(base64);
      const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
      return normalizeSheet(JSON.parse(new TextDecoder().decode(bytes)));
    } catch { return null; }
  }

  const ISSUES_KEY = "ezeizo-issues-v1";
  const CLOSED_KEY = "ezeizo-closed-v1";
  function readList(key) {
    try { const value = JSON.parse(draftState.getItem(key) || "[]"); return Array.isArray(value) ? value : []; }
    catch { return []; }
  }
  function upsert(key, item) {
    const list = readList(key).filter((entry) => entry && entry.id !== item.id);
    list.unshift(item);
    draftState.setItem(key, JSON.stringify(list));
    return list;
  }
  function encodePayload(value) {
    const bytes = new TextEncoder().encode(JSON.stringify(value));
    let binary = "";
    bytes.forEach((byte) => { binary += String.fromCharCode(byte); });
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  }
  function decodePayload(value) {
    try {
      if (!value || value.length > 30000 || !/^[A-Za-z0-9_-]+$/.test(value)) return null;
      const binary = atob(value.replace(/-/g, "+").replace(/_/g, "/"));
      return JSON.parse(new TextDecoder().decode(Uint8Array.from(binary, (character) => character.charCodeAt(0))));
    } catch { return null; }
  }

  const TRANSFER_FORMAT = "ezeizo-offline-v1";
  function makeTransfer(type, payload) {
    return JSON.stringify({ format: TRANSFER_FORMAT, type, payload, exportedAt: new Date().toISOString() }, null, 2);
  }
  function parseTransfer(text, expectedType) {
    if (typeof text !== "string" || text.length > 2_000_000) throw new Error("הקובץ גדול מדי או אינו תקין.");
    let transfer;
    try { transfer = JSON.parse(text); } catch { throw new Error("הקובץ אינו קובץ העברה של EzEizo."); }
    if (transfer?.format !== TRANSFER_FORMAT || transfer.type !== expectedType) throw new Error("סוג הקובץ אינו מתאים לייבוא הזה.");
    return transfer.payload;
  }
  async function copyTransfer(type, payload) {
    if (!navigator.clipboard?.writeText) throw new Error("העתקה אינה זמינה כאן. השתמש בשיתוף קובץ.");
    await navigator.clipboard.writeText(makeTransfer(type, payload));
  }
  async function readTransfer(type) {
    if (!navigator.clipboard?.readText) throw new Error("קריאה מהלוח אינה זמינה כאן. בחר קובץ במקום.");
    return parseTransfer(await navigator.clipboard.readText(), type);
  }
  async function shareTransfer(type, payload, serial) {
    const filename = `EzEizo-${type}-${String(serial || "data").replace(/[^a-zA-Z0-9-]/g, "")}.json`;
    const file = new File([makeTransfer(type, payload)], filename, { type: "application/json" });
    if (navigator.canShare?.({ files: [file] }) && navigator.share) {
      try { await navigator.share({ files: [file], title: `EzEizo · תקלה ${serial}` }); return "shared"; }
      catch (error) { if (error.name === "AbortError") return "cancelled"; }
    }
    const url = URL.createObjectURL(file);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
    return "downloaded";
  }

  window.EzEizoData = { defaultSheet, normalizeSheet, encodeSheet, decodeSheet, readIssues: () => readList(ISSUES_KEY), saveIssue: (item) => upsert(ISSUES_KEY, item), readClosed: () => readList(CLOSED_KEY), nextSerial, saveClosed: (item) => upsert(CLOSED_KEY, item), encodePayload, decodePayload, makeTransfer, parseTransfer, shareTransfer, copyTransfer, readTransfer };
})();
