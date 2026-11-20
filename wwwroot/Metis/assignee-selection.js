export function resolveAssignees(uids, technicians) {
  const selected = uids.filter(Boolean);
  if (selected.length < 1) return { assignees: [], error: "יש לבחור לפחות טכנאי פעיל אחד מהרשימות." };
  if (new Set(selected).size !== selected.length) return { assignees: [], error: "כל טכנאי יכול להיבחר פעם אחת בלבד." };
  const assignees = selected.map((uid) => technicians.find((person) => person.uid === uid && person.active));
  if (assignees.some((person) => !person)) return { assignees: [], error: "יש לבחור טכנאים פעילים מתוך הרשימות." };
  return { assignees, error: null };
}
