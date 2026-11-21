import { acquireEditLock, releaseEditLock, watchEditLock } from "./access-sync.js?v=1.6.3";

// The lease expires after a crashed window. A live editor renews it regularly.
export function createEditLock({ kind, serialOrId, formId, user, onChange }) {
  const session = crypto.randomUUID();
  let held = false, disposed = false, acquiring = false;
  const emit = (message) => { if (!disposed) onChange({ held, message }); };
  const unsubscribe = watchEditLock(kind, serialOrId, (lock) => {
    if (lock?.session === session && lock.expiresAt.toMillis() > Date.now()) {
      if (!held) { held = true; emit(""); }
      return;
    }
    if (held && (!lock || lock.session !== session || lock.expiresAt.toMillis() <= Date.now())) {
      held = false;
      emit(lock?.ownerName ? `הטופס ננעל לעריכה על ידי ${lock.ownerName}.` : "נעילת העריכה פגה. לחץ כדי לנסות שוב.");
    } else if (!held && !acquiring && lock?.expiresAt.toMillis() > Date.now()) {
      emit(`הטופס נמצא כעת בעריכה של ${lock.ownerName}. אפשר לנסות שוב לאחר שיסיים.`);
    }
  }, (error) => { held = false; if (error.code === "unavailable") document.dispatchEvent(new Event("ezeizo:connection-lost")); emit(`לא ניתן לבדוק את נעילת העריכה: ${error.message}`); });
  const timer = setInterval(async () => {
    if (!held || disposed) return;
    try {
      const result = await acquireEditLock(kind, serialOrId, formId, user, session);
      if (!result.held) { held = false; emit(`הטופס נמצא כעת בעריכה של ${result.ownerName}.`); }
    } catch (error) { held = false; if (error.code === "unavailable") document.dispatchEvent(new Event("ezeizo:connection-lost")); emit(`לא ניתן להמשיך לערוך: ${error.message}. בדוק את החיבור ונסה שוב.`); }
  }, 25000);
  async function acquire() {
    if (disposed || acquiring) return;
    acquiring = true;
    emit("בודק אם הטופס פנוי לעריכה…");
    try {
      const result = await acquireEditLock(kind, serialOrId, formId, user, session);
      if (disposed) { if (result.held) await releaseEditLock(kind, serialOrId, session).catch(() => {}); return; }
      held = result.held;
      emit(result.held ? "" : `הטופס נמצא כעת בעריכה של ${result.ownerName}. אפשר לנסות שוב לאחר שיסיים.`);
    } catch (error) {
      held = false;
      if (error.code === "unavailable") document.dispatchEvent(new Event("ezeizo:connection-lost"));
      emit(`לא ניתן לנעול את הטופס במסד המקומי: ${error.message}. בדוק שהקובץ אינו פתוח בתוכנה אחרת ונסה שוב.`);
    } finally { acquiring = false; }
  }
  function dispose() {
    if (disposed) return;
    disposed = true; clearInterval(timer); unsubscribe();
    if (held) void releaseEditLock(kind, serialOrId, session).catch(() => {});
    held = false;
  }
  async function release() {
    if (disposed) return;
    const wasHeld=held;
    disposed=true;clearInterval(timer);unsubscribe();held=false;
    if (wasHeld) await releaseEditLock(kind,serialOrId,session);
  }
  return { acquire, dispose, release, get held() { return held; }, session };
}
