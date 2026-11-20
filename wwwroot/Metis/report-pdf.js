// Render a report into image-backed A4 PDF pages so Hebrew and the chosen
// filename remain consistent across browsers and native print drivers.
const PAGE_WIDTH = 1240;
const PAGE_HEIGHT = 1754;
const MARGIN = 92;
const CONTENT_WIDTH = PAGE_WIDTH - MARGIN * 2;
const encoder = new TextEncoder();

function wrapped(ctx, value, maxWidth) {
  const words = String(value ?? "—").split(/\s+/).filter(Boolean);
  const lines = [];
  let line = "";
  for (const word of words) {
    const candidate = line ? `${line} ${word}` : word;
    if (line && ctx.measureText(candidate).width > maxWidth) { lines.push(line); line = word; }
    else line = candidate;
  }
  if (line) lines.push(line);
  return lines.length ? lines : ["—"];
}

function pageCanvas() {
  const canvas = document.createElement("canvas");
  canvas.width = PAGE_WIDTH; canvas.height = PAGE_HEIGHT;
  const ctx = canvas.getContext("2d", { alpha: false });
  ctx.fillStyle = "#ffffff"; ctx.fillRect(0, 0, PAGE_WIDTH, PAGE_HEIGHT);
  ctx.direction = "rtl"; ctx.textAlign = "right"; ctx.textBaseline = "top";
  return { canvas, ctx };
}

export function pdfFromJpegs(jpegs) {
  const parts = []; let size = 0;
  const add = (value) => { const bytes = typeof value === "string" ? encoder.encode(value) : value; parts.push(bytes); size += bytes.length; };
  const offsets = [0];
  const object = (number, value) => { offsets[number] = size; add(`${number} 0 obj\n`); add(value); add("\nendobj\n"); };
  add("%PDF-1.4\n");
  object(1, "<< /Type /Catalog /Pages 2 0 R >>");
  object(2, `<< /Type /Pages /Kids [${jpegs.map((_, i) => `${3 + i * 3} 0 R`).join(" ")}] /Count ${jpegs.length} >>`);
  jpegs.forEach((jpeg, index) => {
    const page = 3 + index * 3, image = page + 1, contents = page + 2;
    object(page, `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 ${image} 0 R >> >> /Contents ${contents} 0 R >>`);
    offsets[image] = size;
    add(`${image} 0 obj\n<< /Type /XObject /Subtype /Image /Width ${PAGE_WIDTH} /Height ${PAGE_HEIGHT} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length ${jpeg.length} >>\nstream\n`);
    add(jpeg); add("\nendstream\nendobj\n");
    const drawing = "q\n595 0 0 842 0 0 cm\n/Im0 Do\nQ\n";
    object(contents, `<< /Length ${encoder.encode(drawing).length} >>\nstream\n${drawing}endstream`);
  });
  const xref = size, count = 3 + jpegs.length * 3;
  add(`xref\n0 ${count}\n0000000000 65535 f \n`);
  for (let i = 1; i < count; i++) add(`${String(offsets[i]).padStart(10, "0")} 00000 n \n`);
  add(`trailer\n<< /Size ${count} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`);
  return new Blob(parts, { type: "application/pdf" });
}

export { PAGE_WIDTH, PAGE_HEIGHT, MARGIN, CONTENT_WIDTH, wrapped, pageCanvas };

export function downloadFile(blob, filename) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a"); link.href = url; link.download = filename;
  document.body.append(link); link.click(); link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60000);
}

export async function downloadReportPdf({ name, title, period, rows, statusLabel, byDate }) {
  await document.fonts?.ready;
  const images = []; let pageNumber = 0, page, ctx, y;
  const finish = () => {
    ctx.fillStyle = "#71818f"; ctx.font = "20px Arial, sans-serif";
    ctx.fillText(`עמוד ${pageNumber}`, PAGE_WIDTH - MARGIN, PAGE_HEIGHT - 72);
    const data = page.toDataURL("image/jpeg", 0.9).split(",")[1];
    images.push(Uint8Array.from(atob(data), (char) => char.charCodeAt(0)));
  };
  const start = () => {
    ({ canvas: page, ctx } = pageCanvas()); pageNumber++;
    ctx.fillStyle = "#173b5d"; ctx.font = "bold 48px Arial, sans-serif";
    ctx.fillText(title, PAGE_WIDTH - MARGIN, MARGIN);
    ctx.font = "23px Arial, sans-serif"; ctx.fillStyle = "#405c70";
    y = MARGIN + 80;
    for (const line of wrapped(ctx, period, CONTENT_WIDTH)) { ctx.fillText(line, PAGE_WIDTH - MARGIN, y); y += 34; }
    y += 34;
  };
  start();
  for (const row of rows) {
    ctx.font = "26px Arial, sans-serif";
    const info = [row.type === "key" ? `מספר הטענה: ${row.serial}` : `מספר תקלה: ${row.serial}`, `טכנאי / צוות: ${row.technician}`, `מועד פעילות: ${byDate(row.eventAt)}`, `נפתח: ${byDate(row.openedAt)} · נסגר: ${byDate(row.closedAt)}`];
    const description = wrapped(ctx, row.title, CONTENT_WIDTH - 38);
    const infoLines = info.flatMap((item) => wrapped(ctx, item, CONTENT_WIDTH - 38));
    const height = 76 + description.length * 38 + infoLines.length * 32 + 32;
    if (y + height > PAGE_HEIGHT - 135 && y > MARGIN + 180) { finish(); start(); }
    ctx.fillStyle = "#f5f9fb"; ctx.fillRect(MARGIN, y, CONTENT_WIDTH, height);
    ctx.strokeStyle = "#d6e4eb"; ctx.strokeRect(MARGIN, y, CONTENT_WIDTH, height);
    ctx.fillStyle = "#173b5d"; ctx.font = "bold 29px Arial, sans-serif";
    ctx.fillText(`${row.kind} · מטוס ${row.aircraft} · ${statusLabel(row.status)}`, PAGE_WIDTH - MARGIN - 18, y + 17);
    let textY = y + 67; ctx.fillStyle = "#344e61"; ctx.font = "26px Arial, sans-serif";
    for (const line of description) { ctx.fillText(line, PAGE_WIDTH - MARGIN - 18, textY); textY += 38; }
    ctx.font = "22px Arial, sans-serif"; ctx.fillStyle = "#5a7080"; textY += 7;
    for (const line of infoLines) { ctx.fillText(line, PAGE_WIDTH - MARGIN - 18, textY); textY += 32; }
    y += height + 20;
  }
  finish();
  downloadFile(pdfFromJpegs(images), `${name}.pdf`);
}
