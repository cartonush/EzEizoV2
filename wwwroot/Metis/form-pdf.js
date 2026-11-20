import { CONTENT_WIDTH, MARGIN, PAGE_HEIGHT, PAGE_WIDTH, pageCanvas, pdfFromJpegs, wrapped } from "./report-pdf.js?v=1.6.3";
import { KEY_TASKS, KEY_TABLES } from "../keys/data.js?v=1.6.3";

const left = MARGIN, right = PAGE_WIDTH - MARGIN;
const dateTime = (value) => value ? new Date(value).toLocaleString("he-IL", { dateStyle: "short", timeStyle: "short" }) : "—";
const show = (value) => String(value ?? "").trim() || "—";
const checked = (value) => value ? "✓" : "—";
const columns = {
  removed: [["שם יחידה", "name"], ["מס״ד", "serial"]],
  installed: [["שם יחידה", "name"], ["מס״ד", "serial"]],
  ...Object.fromEntries(Object.entries(KEY_TABLES).map(([key, fields]) => [key, fields.map((field) => [field.label, field.key])]))
};

function pdfPages(title, subtitle) {
  let page, ctx, y, pageNumber = 0;
  const images = [];
  function start() {
    ({ canvas: page, ctx } = pageCanvas()); pageNumber++;
    ctx.fillStyle = "#102c45"; ctx.fillRect(0, 0, PAGE_WIDTH, 165);
    ctx.fillStyle = "#bcd6e5"; ctx.font = "24px Arial";
    ctx.fillText("כנף 1 · טייסת תחזוקה · גף אוויוניקה קרב", right, 28);
    ctx.fillStyle = "#ffffff"; ctx.font = "bold 45px Arial"; ctx.fillText(title, right, 67);
    ctx.font = "22px Arial"; ctx.fillText(subtitle, right, 123);
    y = 192;
  }
  function finish() {
    ctx.fillStyle = "#6b7f8d"; ctx.font = "19px Arial"; ctx.fillText(`עמוד ${pageNumber}`, right, PAGE_HEIGHT - 65);
    const bytes = atob(page.toDataURL("image/jpeg", .91).split(",")[1]);
    images.push(Uint8Array.from(bytes, (char) => char.charCodeAt(0)));
    page.width = 0;
  }
  function space(height) { if (y + height > PAGE_HEIGHT - 115) { finish(); start(); return true; } return false; }
  function heading(title) {
    space(85); y += 17; ctx.fillStyle = "#eaf4f8"; ctx.fillRect(left, y, CONTENT_WIDTH, 54);
    ctx.fillStyle = "#173b5d"; ctx.font = "bold 28px Arial"; ctx.fillText(title, right - 17, y + 12); y += 65;
  }
  function text(label, value, prefix = "") {
    ctx.font = "23px Arial";
    const lines = wrapped(ctx, `${prefix}${label}: ${show(value)}`, CONTENT_WIDTH - 36);
    space(lines.length * 34 + 12);
    ctx.fillStyle = "#274457";
    for (const line of lines) { ctx.fillText(line, right - 15, y); y += 34; }
    y += 10;
  }
  function paragraph(value) {
    ctx.font = "22px Arial";
    const lines = String(value || "—").split("\n").flatMap((line) => wrapped(ctx, line, CONTENT_WIDTH - 38));
    space(lines.length * 32 + 18); ctx.fillStyle = "#344f61";
    for (const line of lines) { ctx.fillText(line, right - 16, y); y += 32; }
    y += 17;
  }
  function row(values) {
    ctx.font = "21px Arial";
    const lines = wrapped(ctx, values.join("    ·    "), CONTENT_WIDTH - 40);
    const height = Math.max(50, lines.length * 31 + 20); space(height + 6);
    ctx.fillStyle = "#f8fbfd"; ctx.fillRect(left, y, CONTENT_WIDTH, height);
    ctx.strokeStyle = "#dbe7ed"; ctx.strokeRect(left, y, CONTENT_WIDTH, height);
    ctx.fillStyle = "#274457"; let lineY = y + 10;
    for (const line of lines) { ctx.fillText(line, right - 16, lineY); lineY += 31; }
    y += height + 6;
  }
  function rows(title, entries, fields, proportions) {
    heading(title);
    const active = (entries || []).filter((entry) => fields.some(([, key]) => String(entry?.[key] ?? "").trim()));
    if (!active.length) { paragraph("לא נרשמו פריטים."); return; }
    const weights = proportions || fields.map(() => 1);
    const total = weights.reduce((sum, weight) => sum + weight, 0);
    const widths = weights.map((weight) => CONTENT_WIDTH * weight / total);
    const fontSize = fields.length > 4 ? 18 : 21;
    const lineHeight = fontSize + 8;
    const headerHeight = 56;
    const tableHeader = () => {
      ctx.fillStyle = "#eaf4f8"; ctx.fillRect(left, y, CONTENT_WIDTH, headerHeight);
      ctx.strokeStyle = "#d6e4eb"; ctx.strokeRect(left, y, CONTENT_WIDTH, headerHeight);
      let edge = right;
      ctx.font = `bold ${fontSize}px Arial`; ctx.fillStyle = "#173b5d";
      fields.forEach(([label], index) => {
        wrapped(ctx, label, widths[index] - 18).slice(0, 2).forEach((line, lineIndex) => ctx.fillText(line, edge - 10, y + 7 + lineIndex * 23));
        edge -= widths[index];
      });
      y += headerHeight;
    };
    space(headerHeight + 55); tableHeader();
    for (const [index, entry] of active.entries()) {
      ctx.font = `${fontSize}px Arial`;
      const lines = fields.map(([, key], column) => {
        const value = show(entry?.[key]);
        const words = wrapped(ctx, value, widths[column] - 20);
        // A long identifier without spaces still needs to fit its column.
        return words.flatMap((word) => {
          if (ctx.measureText(word).width <= widths[column] - 20) return [word];
          const segments = []; let segment = "";
          for (const char of word) {
            if (segment && ctx.measureText(segment + char).width > widths[column] - 20) { segments.push(segment); segment = ""; }
            segment += char;
          }
          if (segment) segments.push(segment);
          return segments;
        });
      });
      const height = Math.max(47, 16 + Math.max(...lines.map((item) => item.length)) * lineHeight);
      if (y + height > PAGE_HEIGHT - 115) { finish(); start(); tableHeader(); }
      ctx.fillStyle = index % 2 ? "#f6fafc" : "#ffffff";
      ctx.fillRect(left, y, CONTENT_WIDTH, height);
      ctx.strokeStyle = "#d6e4eb"; ctx.strokeRect(left, y, CONTENT_WIDTH, height);
      let edge = right;
      ctx.fillStyle = "#274457"; ctx.font = `${fontSize}px Arial`;
      lines.forEach((cellLines, column) => {
        let textY = y + 9;
        for (const line of cellLines) { ctx.fillText(line, edge - 10, textY); textY += lineHeight; }
        edge -= widths[column];
        if (column < fields.length - 1) { ctx.beginPath(); ctx.moveTo(edge, y); ctx.lineTo(edge, y + height); ctx.stroke(); }
      });
      y += height;
    }
    y += 10;
  }
  async function inspection(title, details, signatures) {
    const detailLines = details.map(([label, value]) => `${label}: ${show(value)}`);
    ctx.font = "20px Arial";
    const detailRows = wrapped(ctx, detailLines.join("     ·     "), CONTENT_WIDTH - 32);
    const titleHeight = 40, detailsHeight = detailRows.length * 27 + 15, signatureHeight = 80;
    const height = titleHeight + detailsHeight + signatureHeight;
    space(height + 12);
    ctx.fillStyle = "#eaf4f8"; ctx.fillRect(left, y, CONTENT_WIDTH, titleHeight);
    ctx.fillStyle = "#173b5d"; ctx.font = "bold 22px Arial"; ctx.fillText(title, right - 12, y + 8);
    ctx.fillStyle = "#f8fbfd"; ctx.fillRect(left, y + titleHeight, CONTENT_WIDTH, detailsHeight + signatureHeight);
    ctx.strokeStyle = "#d6e4eb"; ctx.strokeRect(left, y, CONTENT_WIDTH, height);
    ctx.fillStyle = "#274457"; ctx.font = "20px Arial";
    detailRows.forEach((line, index) => ctx.fillText(line, right - 12, y + titleHeight + 9 + index * 27));
    const signatureTop = y + titleHeight + detailsHeight;
    const width = CONTENT_WIDTH / signatures.length;
    for (const [index, [label, dataUrl]] of signatures.entries()) {
      const edge = right - index * width;
      ctx.strokeStyle = "#d6e4eb"; ctx.strokeRect(edge - width, signatureTop, width, signatureHeight);
      ctx.fillStyle = "#5a7180"; ctx.font = "18px Arial"; ctx.fillText(label, edge - 10, signatureTop + 6);
      if (dataUrl?.startsWith("data:image/")) {
        try {
          const img = new Image(); img.src = dataUrl; await img.decode();
          const scale = Math.min((width - 26) / img.naturalWidth, 44 / img.naturalHeight);
          ctx.drawImage(img, edge - 12 - img.naturalWidth * scale, signatureTop + 32, img.naturalWidth * scale, img.naturalHeight * scale);
        } catch { ctx.fillText("חתימה שמורה", edge - 10, signatureTop + 38); }
      } else ctx.fillText("טרם נחתם", edge - 10, signatureTop + 38);
    }
    y += height + 12;
  }
  async function signature(label, dataUrl) {
    if (!dataUrl?.startsWith("data:image/")) { text(label, "טרם נחתם"); return; }
    space(100); ctx.fillStyle = "#274457"; ctx.font = "22px Arial"; ctx.fillText(`${label}:`, right - 15, y + 12);
    try {
      const img = new Image(); img.src = dataUrl; await img.decode();
      const scale = Math.min(250 / img.naturalWidth, 76 / img.naturalHeight);
      ctx.drawImage(img, right - 255 - img.naturalWidth * scale, y, img.naturalWidth * scale, img.naturalHeight * scale);
    } catch { ctx.fillText("חתימה שמורה", right - 260, y + 12); }
    y += 95;
  }
  start();
  return { heading, text, paragraph, row, rows, inspection, signature, blob: () => { finish(); return pdfFromJpegs(images); } };
}

async function materials(pdf, state) {
  if (state.toolsEnabled) {
    pdf.rows("כלים", state.tools, columns.tools, [3, 2, 3]);
    for (const [enabled, title, details, signatures] of [
      ["entryEnabled", "ביקורת כניסה", [["מספר אישי של הבודק", "entryInspectorNumber"]], [["חתימת הטכנאי", "entryTechnicianSignature"], ["חתימת הבודק", "entryInspectorSignature"]]],
      ["interimEnabled", "ביקורת במהלך העבודה", [["מספר אישי של הבודק", "interimInspectorNumber"], ["שם פ.כלים / מ״ע", "interimToolsName"]], [["חתימת הטכנאי", "interimTechnicianSignature"], ["חתימת הבודק", "interimInspectorSignature"], ["חתימת פ.כלים / מ״ע", "interimToolsSignature"]]],
      ["finalEnabled", "ביקורת בסיום העבודה", [["מספר אישי של הבודק", "finalInspectorNumber"], ["שם ר״צ / מ״ע", "finalLeadName"], ["שעת עזיבה", "departureTime"]], [["חתימת הטכנאי", "finalTechnicianSignature"], ["חתימת הבודק", "finalInspectorSignature"], ["חתימת ר״צ / מ״ע", "finalLeadSignature"]]]
    ]) if (state[enabled]) await pdf.inspection(title, details.map(([label, key]) => [label, state[key]]), signatures.map(([label, key]) => [label, state[key]]));
  }
  if (state.takenEnabled) {
    pdf.rows("קשיחים נצרכים", state.taken, columns.taken, [3, 2.5, 2, 2, 2.5]);
    await pdf.inspection("אישור קשיחים נצרכים", [["שם מלא", state.takenFullName], ["מספר אישי", state.takenPersonalNumber], ["דרגה", state.takenRank]], [["חתימה", state.takenSignature]]);
  }
  if (state.rejectedEnabled) pdf.rows("קשיחים שנפסלו", state.rejected, columns.rejected, [3, 1.3, 1.7, 3]);
}

async function issuePdf(issue) {
  const sheet = issue.sheet || {}, state = issue.closed?.state || {};
  const pdf = pdfPages(`טופס תקלה ${show(sheet.serial)}`, `${issue.status === "closed" ? "סגור" : issue.status === "pendingReview" ? "ממתין לסגירה" : "פתוח"} · מטוס ${show(sheet.aircraft)}`);
  pdf.heading("פרטי התקלה");
  for (const [label, value] of [["מספר תקלה", sheet.serial], ["טייסת", sheet.squadron], ["דת״ק", sheet.shelter], ["מוסך", sheet.workshop], ["מספר מטוס", sheet.aircraft], ["שם מערכת", sheet.systemName], ["קוד מערכת", sheet.systemCode], ["חליפת ל״א", sheet.ewSystem], ["מוביל התקלה", issue.assigneeName], ["שעת פתיחה", dateTime(issue.createdAt)], ["שעת סגירה", dateTime(state.closedAt || issue.reviewedAt)]]) if (value) pdf.text(label, value);
  pdf.heading("תדריך ובדיקת בטיחות");
  pdf.paragraph('תדריך בטיחות וניהול סיכונים: יועבר ע״י מ״ע התקלה / משלח המשימה וייחתם על ידו.');
  await pdf.signature("חתימת משלח המשימה", sheet.dispatcherSignature);
  pdf.paragraph('בדיקת המטוס בטוח לאחזקה טרם התהליך: אין קסטות מוץ / נורים וכפט״ים מורכבים על המטוס.');
  await pdf.signature("חתימת הטכנאי על בדיקת בטיחות", state.safetyTechnicianSignature);
  pdf.heading("תקלה ותיקון");
  if (issue.status === "open") pdf.paragraph("הטופס פתוח. נתונים שהטכנאי הזין ונשמרו במכשירו בלבד יופיעו כאן לאחר סגירת הטופס.");
  pdf.text("תיאור תקלה", sheet.fault); pdf.text("הערות מנהל", sheet.managerNotes);
  for (const note of state.technicianNotes || []) pdf.text("הערות טכנאי", note);
  pdf.text("תיאור תיקון", state.repair);
  pdf.heading("פעולות לביצוע");
  (sheet.actions || window.EzEizoData?.defaultSheet.actions || []).forEach((task, index) => pdf.row([`${String(index + 1).padStart(2, "0")}. ${task}`, index === 1 ? show(state.arrivalTime) : index === 11 ? show(state.completionTime) : index === 10 ? state.action11Result === "notRequired" ? "X · לא נדרש" : checked(state.actions?.[index]) : checked(state.actions?.[index])]));
  if (state.unitsEnabled) { pdf.rows("יחידות שפורקו", state.removed, columns.removed); pdf.rows("יחידות שהורכבו", state.installed, columns.installed); }
  if (state.constantsEnabled) {
    pdf.heading("קבועים");
    for (const [group, entries] of Object.entries(sheet.constants || {})) pdf.row([group, ...(entries || []).map(([key, value]) => `${key}: ${value}`)]);
  }
  await materials(pdf, state);
  pdf.heading("אישור סיום");
  pdf.paragraph("הנני מאשר שכל הפרטים הרשומים שלמים נכונים ומדויקים. הנני מאשר שעדכנתי במערכות ממוכנות / בספר המטוס את מספרי המכשירים שפורקו והורכבו. הנני מאשר שהעברתי את המכשירים התקולים בצירוף תגי בר תיקון ממולאים בצורה מושלמת. הדף הוא בלשון זכר אך מתייחס לשני המינים.");
  await pdf.signature("חתימת הטכנאי", state.technicianDeclarationSignature);
  if (issue.status === "closed") await pdf.signature("חתימת בודק הדף", issue.reviewerSignature);
  return pdf.blob();
}

async function keyPdf(load) {
  const state = load.work || {};
  const pdf = pdfPages(`הטענת מפתחות ${show(load.serial)}`, `${load.status === "closed" ? "סגור" : "פתוח"} · טייסת ${load.squadron}`);
  pdf.heading("פרטי הטענה");
  for (const [label, value] of [["מספר הטענה", load.serial], ["תאריך", load.date], ["שעה", load.time], ["טייסת", load.squadron], ["דת״ק", load.shelter], ["צוות", (load.teamNames || []).join(" · ")], ["נפתחה", dateTime(load.createdAt)], ["נסגרה", dateTime(load.completedAt)]]) if (value) pdf.text(label, value);
  if (load.status === "open") pdf.paragraph("ההטענה פתוחה. נתונים שהטכנאי הזין ונשמרו במכשירו בלבד יופיעו כאן לאחר סגירת הטופס.");
  pdf.rows("רשימת הטענה", (load.rows || []).map((entry, index) => {
    const done = state.aircraft?.[index] || {};
    return { aircraft: entry.aircraft, blue: entry.blue ? checked(done.blueDone) : "לא נדרש", shach: entry.shach ? checked(done.shachDone) : "לא נדרש", notes: [entry.notes && `מנהל: ${entry.notes}`, done.technicianNote && `טכנאי: ${done.technicianNote}`].filter(Boolean).join(" · ") };
  }), [["מס׳ מטוס", "aircraft"], ["ארז כחול", "blue"], ["שחק", "shach"], ["הערות", "notes"]], [1.6, 1.5, 1.2, 4]);
  if (load.managerNotes) pdf.text("הערות מנהל", load.managerNotes);
  pdf.heading("הנחיות עבודה");
  pdf.paragraph(`אחזקה נדרשת לביצוע: הטענת מטוסים במסגרת ביצוע שינוי מפתחות לחודשים ${load.period}.`);
  pdf.paragraph("יש לוודא כי לא מופיעות תקלות: EREZ 78 לאחר טעינה ולאחר שידור PTT בארז, SAK 7 / 34 בשחק.");
  pdf.paragraph("כלי עבודה / צב״ד צת״ת / חלפים נדרשים: מ.כ 2020 + חרב אש");
  pdf.heading("משימות לביצוע");
  KEY_TASKS.forEach((task, index) => pdf.row([`${String(index + 1).padStart(2, "0")}. ${task}`, checked(index === 0 || state.actions?.[index])]));
  await pdf.signature("חתימת משלח המשימה", load.dispatcherSignature);
  await materials(pdf, state);
  return pdf.blob();
}

export async function createFormPdf(form, type) {
  await document.fonts?.ready;
  const blob = await (type === "key" ? keyPdf(form) : issuePdf(form));
  if (blob.type !== "application/pdf" || blob.size < 100 || new TextDecoder().decode(await blob.slice(0, 5).arrayBuffer()) !== "%PDF-") {
    throw new Error("קובץ ה־PDF לא נוצר כראוי. נסה להוריד אותו שוב.");
  }
  return blob;
}
