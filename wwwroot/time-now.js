let nextId = 0;

function currentTime() {
  return new Intl.DateTimeFormat("en-GB", {
    timeZone: "Asia/Jerusalem", hour: "2-digit", minute: "2-digit", hourCycle: "h23"
  }).format(new Date());
}

export function attachTimeNow(root = document) {
  root.querySelectorAll('input[type="time"]').forEach((input) => {
    if (input.dataset.timeNowAttached) return;
    input.dataset.timeNowAttached = "true";

    const parent = input.parentElement;
    const caption = parent.matches("label.input-field") ? parent.textContent.trim() : input.getAttribute("aria-label") || "שעה";
    const group = document.createElement("div");
    group.className = `time-field${input.classList.contains("time-picker") ? " action-time-field" : ""}`;
    const button = document.createElement("button");
    button.type = "button";
    button.className = "time-now-button";
    button.textContent = "עכשיו";
    button.disabled = input.disabled || input.readOnly;
    button.setAttribute("aria-label", `מלא שעה נוכחית: ${caption}`);
    button.addEventListener("click", () => {
      if (input.disabled || input.readOnly) return;
      input.value = currentTime();
      input.dispatchEvent(new Event("input", { bubbles: true }));
      input.dispatchEvent(new Event("change", { bubbles: true }));
    });

    if (parent.matches("label.input-field")) {
      if (!input.id) input.id = `time-field-${++nextId}`;
      const field = document.createElement("div");
      field.className = parent.className;
      const label = document.createElement("label");
      label.htmlFor = input.id;
      label.textContent = caption;
      group.append(input, button);
      field.append(label, group);
      parent.replaceWith(field);
    } else {
      parent.classList.add("has-time");
      input.replaceWith(group);
      group.append(input, button);
    }
  });
}
