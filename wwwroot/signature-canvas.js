export function attachSignaturePad(canvas, { initial = "", onChange = () => {} } = {}) {
  const context = canvas.getContext("2d");
  const ratio = window.devicePixelRatio || 1;
  const width = canvas.clientWidth || 400;
  const height = canvas.clientHeight || 82;
  canvas.width = Math.round(width * ratio);
  canvas.height = Math.round(height * ratio);
  context.scale(ratio, ratio);
  context.strokeStyle = "#18334a";
  context.lineWidth = 2;
  context.lineCap = "round";
  context.lineJoin = "round";
  if (initial) {
    const image = new Image();
    image.onload = () => context.drawImage(image, 0, 0, width, height);
    image.src = initial;
  }
  let drawing = false;
  const point = (event) => {
    const rect = canvas.getBoundingClientRect();
    return { x: (event.clientX - rect.left) * width / rect.width, y: (event.clientY - rect.top) * height / rect.height };
  };
  canvas.addEventListener("pointerdown", (event) => {
    drawing = true;
    canvas.setPointerCapture(event.pointerId);
    const p = point(event);
    context.beginPath();
    context.moveTo(p.x, p.y);
    context.lineTo(p.x + 0.1, p.y + 0.1);
    context.stroke();
    event.preventDefault();
  });
  canvas.addEventListener("pointermove", (event) => {
    if (!drawing) return;
    const p = point(event);
    context.lineTo(p.x, p.y);
    context.stroke();
    event.preventDefault();
  });
  const finish = () => {
    if (!drawing) return;
    drawing = false;
    onChange(canvas.toDataURL("image/png"));
  };
  canvas.addEventListener("pointerup", finish);
  canvas.addEventListener("pointercancel", finish);
  canvas.addEventListener("lostpointercapture", finish);
  return () => {
    context.clearRect(0, 0, canvas.width, canvas.height);
    onChange("");
  };
}
