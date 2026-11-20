export function issueTimeline(issue) {
  if (!issue) return [];
  const events = [{ label: "נפתח", at: issue.createdAt }];
  for (const reopen of issue.reopenEvents || []) {
    events.push({ label: "נסגר", at: reopen.priorReviewedAt || reopen.priorClosedAt });
    events.push({ label: "נפתח לעריכה", at: reopen.at });
  }
  if (issue.status === "closed") events.push({ label: (issue.reopenEvents || []).length ? "נסגר לאחר עריכה" : "נסגר", at: issue.reviewedAt || issue.closed?.state?.closedAt });
  if (issue.status === "pendingReview") events.push({ label: (issue.reopenEvents || []).length ? "הוגש לסגירה לאחר עריכה" : "הוגש לסגירה", at: issue.closed?.state?.closedAt });
  return events.filter((event) => event.at);
}

export function keyLoadTimeline(load) {
  if (!load) return [];
  const events = [{ label: "נפתח", at: load.createdAt }];
  for (const reopen of load.reopenEvents || []) {
    events.push({ label: "נסגר", at: reopen.priorCompletedAt });
    events.push({ label: "נפתח לעריכה", at: reopen.at });
  }
  if (load.status === "closed") events.push({ label: (load.reopenEvents || []).length ? "נסגר לאחר עריכה" : "נסגר", at: load.completedAt });
  return events.filter((event) => event.at);
}
