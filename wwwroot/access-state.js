import { call } from './offline-bridge.js';

// Drafts are kept per signed-in user in OfflineDrafts inside the local ACCDB.
const values = new Map();
let pending = Promise.resolve();
let lastError = null;
const supported = (key) => key.startsWith('ezeizo-') || key.startsWith('fault-sheet-');
function enqueue(action, payload) {
  const result = pending.then(() => call(action, payload));
  pending = result.then(() => { lastError = null; }, (error) => {
    lastError = error;
    document.dispatchEvent(new CustomEvent('ezeizo:storage-error', { detail: error.message }));
  });
  return result;
}
export async function initializeDrafts() {
  const saved = await call('offline.drafts');
  values.clear();
  for (const [key, value] of Object.entries(saved)) values.set(key, value);
  // Migrate drafts from the previous Windows build once, retaining data during upgrade.
  const olderKeys = Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index));
  for (const key of olderKeys) {
    if (key === null) continue;
    if (!supported(key)) continue;
    const legacy = localStorage.getItem(key);
    if (!values.has(key) && legacy !== null) {
      await call('offline.saveDraft', { key, value: legacy });
      values.set(key, legacy);
    }
    localStorage.removeItem(key);
  }
}
export const draftState = {
  getItem(key) { return values.get(key) ?? null; },
  setItem(key, value) {
    if (!supported(key)) throw new Error('מפתח טיוטה לא תקין.');
    values.set(key, String(value));
    return enqueue('offline.saveDraft', { key, value: String(value) });
  },
  removeItem(key) {
    values.delete(key);
    return enqueue('offline.removeDraft', { key });
  }
};
export async function flushDrafts() {
  await pending;
  if (lastError) throw lastError;
}
