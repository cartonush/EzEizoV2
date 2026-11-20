export function validateTreatment(sheet, state, tables) {
  const missing = (message, selector) => ({ message, selector });
  const filled = (value) => String(value ?? "").trim().length > 0;

  if (!filled(sheet.fault)) return missing("לא ניתן לסגור: המנהל חייב למלא תיאור תקלה.", "#fault-description");
  if (!filled(state.safetyTechnicianSignature)) return missing("חובה לחתום על בדיקת בטיחות המטוס לפני שליחת הדף.", 'canvas[data-signature="safetyTechnicianSignature"]');
  if (!filled(state.repair)) return missing("לא ניתן לסגור: חובה למלא תיאור תיקון.", "#repair-description");

  for (let index = 0; index < sheet.actions.length; index++) {
    if (index === 1 && !/^([01]\d|2[0-3]):[0-5]\d$/.test(state.arrivalTime || "")) {
      return missing("לא ניתן לסגור: חובה לבחור שעת הגעה לדת״ק בפעולה 2.", "#action-list li:nth-child(2) input");
    }
    if (index === 10 && !["done", "notRequired"].includes(state.action11Result)) {
      return missing("לא ניתן לסגור: בפעולה 11 חובה לבחור ✓ אם בוצעה או X אם לא נדרשה.", "#action-list li:nth-child(11) input");
    }
    if (index === sheet.actions.length - 1 && !/^([01]\d|2[0-3]):[0-5]\d$/.test(state.completionTime || "")) {
      return missing("לא ניתן לסגור: חובה לבחור שעת סיום תקלה תקינה.", `#action-list li:nth-child(${index + 1}) input`);
    }
    if (!state.actions[index]) {
      return missing(`לא ניתן לסגור: פעולה ${index + 1} עדיין לא סומנה.`, `#action-list li:nth-child(${index + 1}) input`);
    }
  }

  function checkRows(type, title) {
    const fields = tables[type];
    const rows = Array.isArray(state[type]) ? state[type] : [];
    const active = rows.map((row, index) => ({ row, index })).filter(({ row }) => fields.some(({ key }) => filled(row?.[key])));
    if (!active.length) return missing(`לא ניתן לסגור: חובה למלא לפחות שורה אחת בטבלת ${title}.`, `#${type}-rows input`);
    for (const { row, index } of active) {
      for (const { key, label, optional } of fields) {
        if (optional) continue;
        if (!filled(row[key])) return missing(`לא ניתן לסגור: בשורה ${index + 1} בטבלת ${title} חסר ${label}.`, `#${type}-rows input[data-row-index="${index}"][data-field="${key}"]`);
      }
    }
    return null;
  }
  function checkFields(fields) {
    for (const [key, label, signature = false] of fields) {
      if (!filled(state[key])) return missing(`לא ניתן לסגור: חובה למלא ${label}.`, signature ? `canvas[data-signature="${key}"]` : `[data-state-field="${key}"]`);
      if (key.endsWith("InspectorNumber") || key === "takenPersonalNumber") {
        if (!/^[0-9]{1,7}$/.test(String(state[key]))) return missing(`לא ניתן לסגור: ${label} חייב להכיל ספרות בלבד, עד 7 ספרות.`, `[data-state-field="${key}"]`);
      }
    }
    return null;
  }

  if (state.unitsEnabled) {
    const error = checkRows("removed", "יחידות שפורקו") || checkRows("installed", "יחידות שהורכבו");
    if (error) return error;
  }
  if (state.toolsEnabled) {
    const error = checkRows("tools", "כלים");
    if (error) return error;
    if (state.entryEnabled) {
      const entry = checkFields([
        ["entryTechnicianSignature", "חתימת הטכנאי בביקורת כניסה", true],
        ["entryInspectorSignature", "חתימת הבודק בביקורת כניסה", true],
        ["entryInspectorNumber", "מספר אישי של הבודק בביקורת כניסה"]
      ]);
      if (entry) return entry;
    }
    if (state.interimEnabled) {
      const interim = checkFields([
        ["interimTechnicianSignature", "חתימת הטכנאי בביקורת במהלך העבודה", true],
        ["interimInspectorNumber", "מספר אישי של הבודק בביקורת במהלך העבודה"],
        ["interimInspectorSignature", "חתימת הבודק בביקורת במהלך העבודה", true],
        ["interimToolsName", "שם פ.כלים / מ״ע בביקורת במהלך העבודה"],
        ["interimToolsSignature", "חתימת פ.כלים / מ״ע בביקורת במהלך העבודה", true]
      ]);
      if (interim) return interim;
    }
    if (state.finalEnabled) {
      const final = checkFields([
        ["finalTechnicianSignature", "חתימת הטכנאי בביקורת בסיום העבודה", true],
        ["finalInspectorNumber", "מספר אישי של הבודק בביקורת בסיום העבודה"],
        ["finalInspectorSignature", "חתימת הבודק בביקורת בסיום העבודה", true],
        ["finalLeadName", "שם ר״צ / מ״ע בביקורת בסיום העבודה"],
        ["finalLeadSignature", "חתימת ר״צ / מ״ע בביקורת בסיום העבודה", true],
        ["departureTime", "שעת העזיבה"]
      ]);
      if (final) return final;
      if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(state.departureTime || "")) return missing("לא ניתן לסגור: יש לבחור שעת עזיבה תקינה.", '[data-state-field="departureTime"]');
    }
  }
  if (state.takenEnabled) {
    const error = checkRows("taken", "קשיחים שנלקחו") || checkFields([
      ["takenFullName", "שם מלא בקשיחים שנלקחו"],
      ["takenPersonalNumber", "מספר אישי בקשיחים שנלקחו"],
      ["takenRank", "דרגה בקשיחים שנלקחו"],
      ["takenSignature", "חתימה על קשיחים שנלקחו", true]
    ]);
    if (error) return error;
  }
  if (state.rejectedEnabled) {
    const error = checkRows("rejected", "קשיחים שנפסלו");
    if (error) return error;
  }
  if (!filled(state.technicianDeclarationSignature)) return missing("חובה לחתום על הצהרת הטכנאי לפני שליחת הדף לבדיקת בודק.", 'canvas[data-signature="technicianDeclarationSignature"]');
  return null;
}
