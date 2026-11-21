using System.Text.Json;
using System.Text.Json.Nodes;

namespace EzEizo.Core;

public sealed partial class NativeWindow
{
    private string? activeFormId;
    private string? activeSession;
    private readonly System.Windows.Forms.Timer lockTimer = new() { Interval = 30000 };

    private static JsonObject Object(JsonNode? value) => value as JsonObject ?? new JsonObject();
    private static string V(JsonNode? value, string key) => value?[key]?.ToString() ?? "";
    private static bool B(JsonNode? value, string key) => bool.TryParse(V(value, key), out var result) && result;
    private static JsonElement Json(JsonNode value) => JsonSerializer.SerializeToElement(value);
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    private async Task ReleaseForm()
    {
        lockTimer.Stop();
        var id = activeFormId; var session = activeSession;
        activeFormId = null; activeSession = null;
        if (id is not null && session is not null)
            try { await Work(() => { store.ReleaseLock(id, session); return true; }); }
            catch { /* Expired locks recover automatically; never conceal a save failure. */ }
    }
    private async Task<bool> AcquireForm(string collection, JsonNode doc)
    {
        if (account is null) return false;
        await ReleaseForm();
        var id = V(doc, "id");
        var session = Guid.NewGuid().ToString();
        var result = await Work(() => store.Lock(collection, id, session, account));
        if (!B(result, "held"))
        {
            status.Text = $"הטופס נמצא בעריכת {V(result, "ownerName")}.";
            return false;
        }
        activeFormId = id; activeSession = session;
        lockTimer.Tick -= RenewLock;
        lockTimer.Tick += RenewLock;
        lockTimer.Start();
        return true;
    }
    private async void RenewLock(object? sender, EventArgs e)
    {
        if (activeFormId is null || activeSession is null || account is null) return;
        var id = activeFormId; var session = activeSession; var user = account;
        try
        {
            var collection = currentPage == "fault-editor" ? "issues" : "keyLoads";
            var result = await Work(() => store.Lock(collection, id, session, user));
            if (!B(result, "held")) throw new InvalidOperationException("נעילת הטופס אבדה. יש לפתוח אותו מחדש.");
        }
        catch (Exception ex) { lockTimer.Stop(); status.Text = ex.Message; }
    }
    private static TextBox Field(FlowLayoutPanel stack, string label, JsonObject state, string key, bool multi = false)
    {
        var caption = Label(label, label.Length > 80 ? 100 : 32);
        caption.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        stack.Controls.Add(caption);
        var input = Input(label, 2000);
        if (multi) { input.Multiline = true; input.Height = 88; input.ScrollBars = ScrollBars.Vertical; input.Width = 735; }
        input.Text = V(state, key);
        input.TextChanged += (_, _) => state[key] = input.Text;
        stack.Controls.Add(input);
        return input;
    }
    private static CheckBox Check(FlowLayoutPanel stack, string label, JsonObject state, string key)
    {
        var box = new CheckBox { Text = label, Checked = B(state, key), AutoSize = false, Width = 745, Height = 49,
            ForeColor = Navy, BackColor = Color.FromArgb(249, 251, 252), Padding = new Padding(12, 0, 12, 0),
            Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) };
        box.CheckedChanged += (_, _) => state[key] = box.Checked;
        stack.Controls.Add(box);
        return box;
    }
    private static NativeSignature Signature(FlowLayoutPanel stack, string label, JsonObject state, string key, bool editable)
    {
        stack.Controls.Add(Label(label, label.Length > 80 ? 120 : 34));
        var pad = new NativeSignature(V(state, key)) { Enabled = editable, Margin = new Padding(0, 0, 0, 12) };
        stack.Controls.Add(pad);
        if (editable) stack.Controls.Add(Button("נקה חתימה", (_, _) => { pad.Clear(); state[key] = ""; }));
        return pad;
    }
    private static void Section(FlowLayoutPanel stack, string title)
    {
        var heading = new Panel { Width = 745, Height = 78, BackColor = Color.White, Margin = new Padding(0, 12, 0, 8) };
        heading.Controls.Add(new Label { Text = "פרטי טופס", Location = new Point(0, 4), Width = 740, Height = 22,
            TextAlign = ContentAlignment.MiddleRight, ForeColor = Blue, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        heading.Controls.Add(new Label { Text = title, Location = new Point(0, 27), Width = 740, Height = 39,
            TextAlign = ContentAlignment.MiddleRight, ForeColor = Navy, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        heading.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Color.FromArgb(232, 239, 243) });
        stack.Controls.Add(heading);
    }
    private static FlowLayoutPanel EditorBar(FlowLayoutPanel stack)
    {
        var bar = new FlowLayoutPanel { Width = 745, Height = 65, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, BackColor = Color.White,
            Padding = new Padding(8, 8, 8, 0), Margin = new Padding(0, 0, 0, 15) };
        stack.Controls.Add(bar);
        return bar;
    }
    private static Label EditorStatus() => new()
    {
        Text = "הנתונים נשמרים במסד המקומי לאחר לחיצה על שמירה",
        Width = 335, Height = 44, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Navy, Font = new Font("Segoe UI", 9)
    };

    private async Task AddDocumentActions(FlowLayoutPanel stack, string page, JsonNode doc)
    {
        var user = account;
        if (user is null) return;
        var statusValue = V(doc, "status");
        if (role == AppRole.Manager)
        {
            ShowReadOnlyForm(stack, page, doc);
            if (statusValue == "open")
                stack.Controls.Add(Button("ערוך פרטי טופס וצוות", async (_, _) => await EditDefinition(page, doc)));
            if (page == "issues" && statusValue == "pendingReview")
            {
                var reviewer = Signature(stack, "חתימת בודק הדף", new JsonObject(), "reviewerSignature", true);
                stack.Controls.Add(Button("בדוק וסגור", async (_, _) =>
                {
                    if (!reviewer.HasSignature) { status.Text = "חתימת בודק הדף חסרה."; return; }
                    try
                    {
                        await Work(() => store.Update("issues", V(doc, "id"), "approve",
                            Json(new JsonObject { ["signature"] = reviewer.DataUrl() }), user));
                        await OpenList("issues");
                    }
                    catch (Exception ex) { Error(ex); }
                }));
            }
            if (statusValue is "closed" or "pendingReview")
                stack.Controls.Add(Button("החזר לטכנאי לעריכה", async (_, _) =>
                {
                    try
                    {
                        await Work(() => store.Update(page == "issues" ? "issues" : "keyLoads", V(doc, "id"), "reopen", Json(new JsonObject()), user));
                        await OpenList(page);
                    }
                    catch (Exception ex) { Error(ex); }
                }));
            if (statusValue == "open")
            {
                var note = Field(stack, "הוסף הערת מנהל", new JsonObject(), "note", true);
                stack.Controls.Add(Button("שמור הערה", async (_, _) =>
                {
                    try
                    {
                        await Work(() => store.Update(page == "issues" ? "issues" : "keyLoads", V(doc, "id"), "note",
                            Json(new JsonObject { ["note"] = note.Text }), user));
                        await OpenDocument(page, V(doc, "id"));
                    }
                    catch (Exception ex) { Error(ex); }
                }));
            }
            return;
        }
        if (statusValue != "open") return;
        if (!await AcquireForm(page == "issues" ? "issues" : "keyLoads", doc))
        {
            stack.Controls.Add(Label($"הטופס נמצא בעריכה של טכנאי אחר. {status.Text}", 72));
            stack.Controls.Add(Button("בדוק שוב", async (_, _) => await OpenDocument(page, V(doc, "id"))));
            return;
        }
        if (page == "issues") await FaultEditor(stack, doc, user);
        else await KeyEditor(stack, doc, user);
    }

    private static void ShowReadOnlyForm(FlowLayoutPanel stack, string page, JsonNode doc)
    {
        var state = page == "issues" ? doc["closed"]?["state"] : doc["work"] ?? doc["workDraft"];
        if (state is null) { stack.Controls.Add(Label("טרם נשמר מילוי של טכנאי בטופס זה.")); return; }
        Section(stack, "מילוי הטכנאי");
        if (page == "issues")
        {
            stack.Controls.Add(Label("תיאור תיקון: " + V(state, "repair"), 85));
            stack.Controls.Add(Label("הערת טכנאי: " + V(state, "technicianNote"), 60));
            stack.Controls.Add(Label("שעת הגעה: " + V(state, "arrivalTime") + "     שעת סיום: " + V(state, "completionTime")));
        }
        else
        {
            var aircraft = state["aircraft"] as JsonArray ?? new();
            var rows = doc["rows"] as JsonArray ?? new();
            for (var i = 0; i < rows.Count; i++)
                stack.Controls.Add(Label($"מטוס {V(rows[i], "aircraft")} · ארז כחול: {(i < aircraft.Count && B(aircraft[i], "blueDone") ? "בוצע" : "—")} · שחק: {(i < aircraft.Count && B(aircraft[i], "shachDone") ? "בוצע" : "—")} · הערה: {V(i < aircraft.Count ? aircraft[i] : null, "technicianNote")}", 50));
        }
        var actions = state["actions"] as JsonArray ?? new();
        var titles = page == "issues" ? NativeCatalog.Actions : NativeCatalog.KeyActions;
        Section(stack, "פעולות אחזקה");
        for (var i = 0; i < titles.Length; i++)
            stack.Controls.Add(Label($"{i + 1:D2}. {(i < actions.Count && actions[i]?.ToString() == "true" ? "✓" : "□")} {titles[i]}", 48));
        foreach (var (kind, spec) in NativeCatalog.Tables)
        {
            var rows = state[kind] as JsonArray;
            if (rows is null || rows.Count == 0) continue;
            Section(stack, spec.title);
            var grid = new DataGridView { Width = 740, Height = Math.Min(280, rows.Count * 35 + 55), ReadOnly = true,
                AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            foreach (var field in spec.fields) grid.Columns.Add(field.key, field.label);
            foreach (var row in rows) grid.Rows.Add(spec.fields.Select(field => V(row, field.key)).ToArray());
            stack.Controls.Add(grid);
        }
        foreach (var (name, label) in new[]
        {
            ("safetyTechnicianSignature", "בדיקת מטוס בטוח לאחזקה"),
            ("entryTechnicianSignature", "ביקורת כניסה · חתימת טכנאי"),
            ("entryInspectorSignature", "ביקורת כניסה · חתימת בודק"),
            ("interimTechnicianSignature", "ביקורת במהלך העבודה · טכנאי"),
            ("interimInspectorSignature", "ביקורת במהלך העבודה · בודק"),
            ("finalTechnicianSignature", "ביקורת בסיום · טכנאי"),
            ("finalInspectorSignature", "ביקורת בסיום · בודק"),
            ("takenSignature", "קשיחים שנלקחו"),
            ("technicianDeclarationSignature", "הצהרת הטכנאי")
        })
        {
            var value = V(state, name);
            if (!value.StartsWith("data:image/png;base64,")) continue;
            stack.Controls.Add(Label(label)); stack.Controls.Add(new NativeSignature(value) { Enabled = false });
        }
    }

    private async Task FaultEditor(FlowLayoutPanel stack, JsonNode doc, Account user)
    {
        currentPage = "fault-editor";
        var id = V(doc, "id"); var sheet = Object(doc["sheet"]);
        var key = "fault-sheet-v2:" + id;
        var saved = await Work(() => store.ReadDraft(key, user));
        var state = saved is not null ? Object(JsonNode.Parse(saved)) :
            Object(doc["closed"]?["state"]?.DeepClone());
        var controls = new Dictionary<string, NativeSignature>();
        Section(stack, "טופס טיפול בתקלה");
        var faultBar = EditorBar(stack);
        controls["safetyTechnicianSignature"] = Signature(stack, "בדיקת המטוס בטוח לאחזקה טרם התהליך: אין קסטות מוץ / נורים וכפט״ים מורכבים על המטוס. חתימת הטכנאי", state, "safetyTechnicianSignature", true);
        Field(stack, "תיאור תיקון", state, "repair", true);
        stack.Controls.Add(Label("הערות מנהל: " + V(sheet, "managerNotes"), 60));
        Field(stack, "הערת טכנאי", state, "technicianNote", true);
        Section(stack, "קבועים לפי מטוס");
        foreach (var (group, entries) in Object(sheet["constants"]))
        {
            stack.Controls.Add(Label(group, 38, true));
            foreach (var row in entries as JsonArray ?? new())
                stack.Controls.Add(Label($"{row?[0]}: {row?[1]}", 28));
        }
        Section(stack, "פעולות אחזקה");
        var actionValues = state["actions"] as JsonArray ?? new JsonArray();
        while (actionValues.Count < 12) actionValues.Add(false);
        if (state["actions"] is null) state["actions"] = actionValues;
        for (var i = 0; i < NativeCatalog.Actions.Length; i++)
        {
            var index = i;
            if (i == 1 || i == 11)
            {
                var keyName = i == 1 ? "arrivalTime" : "completionTime";
                var picker = new DateTimePicker { Width = 190, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Today };
                if (TimeSpan.TryParse(V(state, keyName), out var value)) picker.Value = DateTime.Today.Add(value);
                stack.Controls.Add(Label($"{i + 1:D2}. {NativeCatalog.Actions[i]}")); stack.Controls.Add(picker);
                var now = Button("השעה עכשיו", (_, _) => { picker.Value = DateTime.Now; state[keyName] = picker.Value.ToString("HH:mm"); actionValues[index] = true; });
                stack.Controls.Add(now);
                picker.ValueChanged += (_, _) => { state[keyName] = picker.Value.ToString("HH:mm"); actionValues[index] = true; };
            }
            else if (i == 10)
            {
                stack.Controls.Add(Label($"11. {NativeCatalog.Actions[i]}"));
                var choice = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
                choice.Items.AddRange(["בחר תוצאה", "✓ בוצע", "X לא נדרש"]);
                choice.SelectedIndex = V(state, "action11Result") == "done" ? 1 : V(state, "action11Result") == "notRequired" ? 2 : 0;
                choice.SelectedIndexChanged += (_, _) => { state["action11Result"] = choice.SelectedIndex == 1 ? "done" : choice.SelectedIndex == 2 ? "notRequired" : ""; actionValues[index] = choice.SelectedIndex > 0; };
                stack.Controls.Add(choice);
            }
            else
            {
                var box = new CheckBox { Text = $"{i + 1:D2}. {NativeCatalog.Actions[i]}", Width = 750, Height = 45,
                    Checked = bool.TryParse(actionValues[i]?.ToString(), out var checkedValue) && checkedValue };
                box.CheckedChanged += (_, _) => actionValues[index] = box.Checked;
                stack.Controls.Add(box);
            }
        }
        foreach (var kind in NativeCatalog.Tables.Keys)
        {
            if (kind == "installed") Section(stack, "יחידות שהורכבו");
            var toggle = kind == "installed" ? stack.Controls.OfType<CheckBox>().Last(x => x.Text == "יחידות שפורקו") :
                Check(stack, NativeCatalog.Tables[kind].title, state, kind == "removed" ? "unitsEnabled" : kind + "Enabled");
            var table = Table(stack, state, kind);
            table.Visible = toggle.Checked;
            toggle.CheckedChanged += (_, _) => table.Visible = toggle.Checked;
        }
        ToolReviews(stack, state, controls);
        var takenDeclaration = TakenDeclaration(stack, state, controls);
        var takenToggle = stack.Controls.OfType<CheckBox>().Last(x => x.Text == "קשיחים שנלקחו");
        takenDeclaration.Visible = takenToggle.Checked;
        takenToggle.CheckedChanged += (_, _) => takenDeclaration.Visible = takenToggle.Checked;
        controls["technicianDeclarationSignature"] = Signature(stack,
            "הנני מאשר שכל הפרטים הרשומים שלמים, נכונים ומדויקים. הנני מאשר שעדכנתי במערכות ממוכנות / בספר המטוס את מספרי המכשירים שפורקו והורכבו. הנני מאשר שהעברתי את המכשירים התקולים בצירוף תגי בר תיקון ממולאים בצורה מושלמת. הדף בלשון זכר אך מתייחס לשני המינים. חתימת הטכנאי", state, "technicianDeclarationSignature", true);
        void CaptureSignatures() { foreach (var (name, pad) in controls) state[name] = pad.DataUrl(); }
        faultBar.Controls.Add(Button("שמירה", async (_, _) =>
        {
            try { CaptureSignatures(); await Work(() => { store.SaveDraft(key, state.ToJsonString(), user); return true; }); status.Text = "הטיוטה נשמרה במסד המשותף."; }
            catch (Exception ex) { Error(ex); }
        }));
        faultBar.Controls.Add(EditorStatus());
        faultBar.Controls.Add(Button("איפוס טופס", async (_, _) =>
        {
            if (MessageBox.Show(this, "לנקות את כל נתוני הטיוטה?", "איפוס", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { await Work(() => { store.RemoveDraft(key, user); return true; }); await ReleaseForm(); await OpenDocument("issues", id); }
            catch (Exception ex) { Error(ex); }
        }));
        stack.Controls.Add(Button("שמירה וסגירה", async (_, _) =>
        {
            try
            {
                CaptureSignatures();
                var error = ValidateFault(sheet, state);
                if (error is not null) { status.Text = error; MessageBox.Show(this, error, "חסר מידע", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                var payload = new JsonObject { ["session"] = activeSession, ["record"] = new JsonObject { ["closedAt"] = DateTimeOffset.Now.ToString("o") }, ["state"] = state.DeepClone() };
                await Work(() => store.Update("issues", id, "submit", Json(payload), user));
                await ReleaseForm(); await OpenList("issues");
            }
            catch (Exception ex) { Error(ex); }
        }));
    }

    private static string? ValidateFault(JsonObject sheet, JsonObject state)
    {
        if (string.IsNullOrWhiteSpace(V(sheet, "fault"))) return "תיאור התקלה חסר.";
        if (string.IsNullOrWhiteSpace(V(state, "safetyTechnicianSignature"))) return "חסרה חתימת בדיקת בטיחות המטוס.";
        if (string.IsNullOrWhiteSpace(V(state, "repair"))) return "תיאור התיקון חסר.";
        var actions = state["actions"] as JsonArray;
        if (actions is null || actions.Count < 12 || actions.Any(x => x?.ToString() != "true")) return "יש להשלים את כל 12 הפעולות.";
        if (string.IsNullOrWhiteSpace(V(state, "arrivalTime")) || string.IsNullOrWhiteSpace(V(state, "completionTime"))) return "חסרה שעת הגעה או שעת סיום.";
        if (V(state, "action11Result") is not ("done" or "notRequired")) return "יש לסמן ✓ או X בפעולה 11.";
        foreach (var (kind, spec) in NativeCatalog.Tables)
        {
            var enabled = B(state, kind is "removed" or "installed" ? "unitsEnabled" : kind + "Enabled");
            if (!enabled) continue;
            var rows = (state[kind] as JsonArray ?? new()).Where(x => spec.fields.Any(f => !string.IsNullOrWhiteSpace(V(x, f.key)))).ToArray();
            if (rows.Length == 0) return $"חסרה שורה בטבלת {spec.title}.";
            foreach (var row in rows) foreach (var field in spec.fields)
                if (!field.optional && string.IsNullOrWhiteSpace(V(row, field.key))) return $"חסר {field.label} בטבלת {spec.title}.";
        }
        if (B(state, "toolsEnabled"))
        {
            if (!B(state, "entryEnabled") || !B(state, "finalEnabled")) return "כאשר יש כלים, ביקורת כניסה וסיום הן חובה.";
            foreach (var key in new[] { "entryTechnicianSignature", "entryInspectorSignature", "entryInspectorNumber", "finalTechnicianSignature", "finalInspectorSignature", "finalInspectorNumber", "finalLeadName", "finalLeadSignature", "departureTime" })
                if (string.IsNullOrWhiteSpace(V(state, key))) return "חסרים פרטי ביקורת כלים.";
            if (B(state, "interimEnabled")) foreach (var key in new[] { "interimTechnicianSignature", "interimInspectorSignature", "interimInspectorNumber", "interimToolsName", "interimToolsSignature" })
                if (string.IsNullOrWhiteSpace(V(state, key))) return "חסרים פרטי ביקורת במהלך העבודה.";
        }
        if (B(state, "takenEnabled")) foreach (var key in new[] { "takenFullName", "takenPersonalNumber", "takenRank", "takenSignature" })
            if (string.IsNullOrWhiteSpace(V(state, key))) return "חסרים פרטי חתימה על קשיחים שנלקחו.";
        foreach (var key in new[] { "entryInspectorNumber", "interimInspectorNumber", "finalInspectorNumber", "takenPersonalNumber" })
            if (V(state, key).Length > 0 && (V(state, key).Length > 7 || !V(state, key).All(char.IsAsciiDigit))) return "מספר אישי חייב להכיל ספרות בלבד, עד 7 ספרות.";
        if (string.IsNullOrWhiteSpace(V(state, "technicianDeclarationSignature"))) return "חסרה חתימת הצהרת הטכנאי.";
        return null;
    }

    private static DataGridView Table(FlowLayoutPanel stack, JsonObject state, string kind)
    {
        var spec = NativeCatalog.Tables[kind];
        var data = state[kind] as JsonArray ?? new JsonArray();
        if (state[kind] is null) state[kind] = data;
        var grid = new DataGridView { Width = 740, Height = 185, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, BackgroundColor = Color.White };
        foreach (var field in spec.fields)
        {
            if (field.key == "name" && kind is "removed" or "installed")
                grid.Columns.Add(new DataGridViewComboBoxColumn { Name = field.key, HeaderText = field.label, DataSource = new[] { "MLPRF", "PSP", "DMT" }, FlatStyle = FlatStyle.Flat });
            else grid.Columns.Add(field.key, field.label);
        }
        foreach (var row in data) grid.Rows.Add(spec.fields.Select(f => V(row, f.key)).ToArray());
        grid.CellEndEdit += (_, _) => Sync();
        grid.RowsRemoved += (_, _) => Sync();
        void Sync()
        {
            var next = new JsonArray();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var item = new JsonObject();
                foreach (var f in spec.fields) item[f.key] = row.Cells[f.key].Value?.ToString() ?? "";
                if (spec.fields.Any(f => !string.IsNullOrWhiteSpace(V(item, f.key)))) next.Add(item);
            }
            state[kind] = next;
        }
        stack.Controls.Add(grid);
        return grid;
    }

    private static void ToolReviews(FlowLayoutPanel stack, JsonObject state, Dictionary<string, NativeSignature> pads)
    {
        foreach (var (title, toggle, fields) in new[]
        {
            ("ביקורת כניסה", "entryEnabled", new[] { ("entryTechnicianSignature", "חתימת טכנאי", true), ("entryInspectorSignature", "חתימת בודק", true), ("entryInspectorNumber", "מספר אישי של הבודק", false) }),
            ("ביקורת במהלך העבודה", "interimEnabled", new[] { ("interimTechnicianSignature", "חתימת טכנאי", true), ("interimInspectorNumber", "מספר אישי של הבודק", false), ("interimInspectorSignature", "חתימת בודק", true), ("interimToolsName", "שם פ. כלים / מ״ע", false), ("interimToolsSignature", "חתימת פ. כלים / מ״ע", true) }),
            ("ביקורת בסיום העבודה", "finalEnabled", new[] { ("finalTechnicianSignature", "חתימת טכנאי", true), ("finalInspectorNumber", "מספר אישי של הבודק", false), ("finalInspectorSignature", "חתימת בודק", true), ("finalLeadName", "שם ר״צ / מ״ע", false), ("finalLeadSignature", "חתימת ר״צ / מ״ע", true), ("departureTime", "שעת עזיבה", false) })
        })
        {
            var box = Check(stack, title, state, toggle);
            var group = new FlowLayoutPanel { Width = 750, Height = fields.Sum(f => f.Item3 ? 220 : 90), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            foreach (var (key, label, sign) in fields)
                if (sign) pads[key] = Signature(group, label, state, key, true);
                else if (key == "departureTime") TimeField(group, label, state, key);
                else
                {
                    var input = Field(group, label, state, key);
                    if (key.EndsWith("InspectorNumber")) Numeric(input);
                }
            group.Visible = box.Checked; box.CheckedChanged += (_, _) => group.Visible = box.Checked;
            stack.Controls.Add(group);
        }
    }

    private static void Numeric(TextBox input)
    {
        input.MaxLength = 7;
        input.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar)) e.Handled = true; };
    }
    private static void TimeField(FlowLayoutPanel stack, string label, JsonObject state, string key)
    {
        stack.Controls.Add(Label(label));
        var time = new DateTimePicker { Width = 190, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        if (TimeSpan.TryParse(V(state, key), out var value)) time.Value = DateTime.Today.Add(value);
        time.ValueChanged += (_, _) => state[key] = time.Value.ToString("HH:mm");
        stack.Controls.Add(time);
        stack.Controls.Add(Button("השעה עכשיו", (_, _) => { time.Value = DateTime.Now; state[key] = time.Value.ToString("HH:mm"); }));
    }
    private static FlowLayoutPanel TakenDeclaration(FlowLayoutPanel stack, JsonObject state, Dictionary<string, NativeSignature> pads)
    {
        var group = new FlowLayoutPanel { Width = 750, Height = 500, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Section(group, "חתימה על קשיחים שנלקחו");
        Field(group, "שם מלא", state, "takenFullName");
        var number = Field(group, "מספר אישי", state, "takenPersonalNumber"); Numeric(number);
        Field(group, "דרגה", state, "takenRank");
        pads["takenSignature"] = Signature(group, "חתימה", state, "takenSignature", true);
        stack.Controls.Add(group);
        return group;
    }

    private async Task KeyEditor(FlowLayoutPanel stack, JsonNode doc, Account user)
    {
        currentPage = "key-editor";
        var id = V(doc, "id");
        var work = Object(doc["workDraft"]?.DeepClone() ?? doc["work"]?.DeepClone());
        var rows = doc["rows"] as JsonArray ?? new();
        var completed = work["aircraft"] as JsonArray ?? new JsonArray();
        while (completed.Count < rows.Count) completed.Add(new JsonObject());
        if (work["aircraft"] is null) work["aircraft"] = completed;
        Section(stack, "רשימת מטוסים להטענה");
        var keyBar = EditorBar(stack);
        for (var i = 0; i < rows.Count; i++)
        {
            var index = i; var row = rows[i]; var state = Object(completed[i]); if (completed[i] is null) completed[i] = state;
            stack.Controls.Add(Label($"מטוס {row?["aircraft"]} · הערת מנהל: {row?["notes"]}", 45, true));
            if (B(row, "blue")) Check(stack, "ארז כחול בוצע", state, "blueDone");
            if (B(row, "shach")) Check(stack, "שחק בוצע", state, "shachDone");
            Field(stack, "הערת טכנאי (לא חובה)", state, "technicianNote", true);
        }
        stack.Controls.Add(Label($"אחזקה נדרשת: הטענת מטוסים במסגרת שינוי מפתחות לחודשים {V(doc, "period")}", 55));
        stack.Controls.Add(Label("יש לוודא כי לא מופיעות תקלות EREZ 78 לאחר טעינה ושידור PTT בארז, SAK 7 / 34 בשחק.", 55));
        stack.Controls.Add(Label("כלי עבודה / צב״ד צת״ת / חלפים: מ.כ 2020 + חרב אש", 48));
        Section(stack, "פעולות לביצוע");
        var actions = work["actions"] as JsonArray ?? new JsonArray();
        while (actions.Count < 12) actions.Add(false);
        actions[0] = true; if (work["actions"] is null) work["actions"] = actions;
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            var box = new CheckBox { Text = $"{i + 1:D2}. {NativeCatalog.KeyActions[i]}", Width = 745, Height = 48,
                Checked = i == 0 || bool.TryParse(actions[i]?.ToString(), out var b) && b, Enabled = i != 0 };
            box.CheckedChanged += (_, _) => actions[index] = box.Checked;
            stack.Controls.Add(box);
        }
        stack.Controls.Add(Label("חתימת משלח המשימה"));
        stack.Controls.Add(new NativeSignature(V(doc, "dispatcherSignature")) { Enabled = false });
        var infoButton = Button("מידע לעזר הטכנאי · הצג / הסתר", (_, _) => { });
        var reference = new FlowLayoutPanel { Width = 745, Height = 300, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        reference.Controls.Add(Label("קוד ביקורת: ברק 1 – 500, ברק 2 – 584 לביצוע שחק.", 40));
        reference.Controls.Add(Label("קוד ביקורת: ברק 1 – 510, ברק 2 – 578 לביצוע ארז.", 40));
        var table = new DataGridView { Width = 735, Height = 195, ReadOnly = true, RowHeadersVisible = false,
            AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        table.Columns.Add("item", "שם הפריט"); table.Columns.Add("code", "סימול"); table.Columns.Add("meaning", "משמעות"); table.Columns.Add("valid", "תוקף");
        var periodDate = DateTime.TryParse(V(doc, "date"), out var parsed) ? parsed : DateTime.Today;
        var nextDate = periodDate.AddMonths(1);
        table.Rows.Add("כר־156 · ארז כחול", "6003-216", "כרטיס מחיקה שנתי", periodDate.ToString("yyyy"));
        table.Rows.Add("כר־156 · ארז כחול", "6001-295", "חודש נוכחי", periodDate.ToString("MM/yy"));
        table.Rows.Add("כר־156 · ארז כחול", "6001-296", "חודש הבא", nextDate.ToString("MM/yy"));
        table.Rows.Add("כר־116 · שחק", "6016-385", "חודש נוכחי", periodDate.ToString("MM/yy"));
        table.Rows.Add("כר־116 · שחק", "6016-386", "חודש הבא", nextDate.ToString("MM/yy"));
        reference.Controls.Add(table);
        infoButton.Click += (_, _) => reference.Visible = !reference.Visible;
        stack.Controls.Add(infoButton); stack.Controls.Add(reference);
        var pads = new Dictionary<string, NativeSignature>();
        foreach (var kind in new[] { "tools", "taken", "rejected" })
        {
            var box = Check(stack, NativeCatalog.Tables[kind].title, work, kind + "Enabled");
            var grid = Table(stack, work, kind); grid.Visible = box.Checked; box.CheckedChanged += (_, _) => grid.Visible = box.Checked;
        }
        ToolReviews(stack, work, pads);
        var takenDeclaration = TakenDeclaration(stack, work, pads);
        var takenToggle = stack.Controls.OfType<CheckBox>().Last(x => x.Text == "קשיחים שנלקחו");
        takenDeclaration.Visible = takenToggle.Checked;
        takenToggle.CheckedChanged += (_, _) => takenDeclaration.Visible = takenToggle.Checked;
        void Capture() { foreach (var (name, pad) in pads) work[name] = pad.DataUrl(); }
        keyBar.Controls.Add(Button("שמירה", async (_, _) =>
        {
            try { Capture(); await Work(() => store.Update("keyLoads", id, "draft", Json(new JsonObject { ["session"] = activeSession, ["work"] = work.DeepClone() }), user)); status.Text = "הטיוטה נשמרה במסד המשותף."; }
            catch (Exception ex) { Error(ex); }
        }));
        keyBar.Controls.Add(EditorStatus());
        keyBar.Controls.Add(Button("איפוס טופס", async (_, _) =>
        {
            if (MessageBox.Show(this, "לנקות את מילוי הטופס?", "איפוס", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { await Work(() => store.Update("keyLoads", id, "draft", Json(new JsonObject { ["session"] = activeSession, ["work"] = new JsonObject() }), user)); await ReleaseForm(); await OpenDocument("keys", id); }
            catch (Exception ex) { Error(ex); }
        }));
        stack.Controls.Add(Button("סיים הטענה", async (_, _) =>
        {
            Capture();
            var validation = ValidateKey(doc, work);
            if (validation is not null) { status.Text = validation; MessageBox.Show(this, validation, "חסר מידע"); return; }
            try { await Work(() => store.Update("keyLoads", id, "complete", Json(new JsonObject { ["session"] = activeSession, ["work"] = work.DeepClone() }), user)); await ReleaseForm(); await OpenList("keys"); }
            catch (Exception ex) { Error(ex); }
        }));
    }
    private static string? ValidateKey(JsonNode doc, JsonObject state)
    {
        var actions = state["actions"] as JsonArray;
        if (actions?.Count != 12 || actions.Any(x => x?.ToString() != "true")) return "יש לסמן את כל 12 המשימות.";
        var rows = doc["rows"] as JsonArray ?? new();
        var completed = state["aircraft"] as JsonArray ?? new();
        foreach (var (row, index) in rows.Select((row, index) => (row, index)))
            if (index >= completed.Count || B(row, "blue") && !B(completed[index], "blueDone") || B(row, "shach") && !B(completed[index], "shachDone"))
                return $"לא הושלמה הטענה במטוס {V(row, "aircraft")}.";
        foreach (var kind in new[] { "tools", "taken", "rejected" })
        {
            if (!B(state, kind + "Enabled")) continue;
            var spec = NativeCatalog.Tables[kind];
            var filled = (state[kind] as JsonArray ?? new()).Where(x => spec.fields.Any(f => !string.IsNullOrWhiteSpace(V(x, f.key)))).ToArray();
            if (filled.Length == 0) return $"יש למלא שורה בטבלת {spec.title}.";
            foreach (var row in filled) foreach (var field in spec.fields)
                if (!field.optional && string.IsNullOrWhiteSpace(V(row, field.key))) return $"חסר {field.label} בטבלת {spec.title}.";
        }
        if (B(state, "toolsEnabled"))
        {
            if (!B(state, "entryEnabled") || !B(state, "finalEnabled")) return "כאשר יש כלים חובה לסמן ביקורת כניסה וסיום.";
            foreach (var key in new[] { "entryTechnicianSignature", "entryInspectorSignature", "entryInspectorNumber", "finalTechnicianSignature", "finalInspectorSignature", "finalInspectorNumber", "finalLeadName", "finalLeadSignature", "departureTime" })
                if (string.IsNullOrWhiteSpace(V(state, key))) return "חסרים פרטי ביקורת כלים.";
            if (B(state, "interimEnabled")) foreach (var key in new[] { "interimTechnicianSignature", "interimInspectorSignature", "interimInspectorNumber", "interimToolsName", "interimToolsSignature" })
                if (string.IsNullOrWhiteSpace(V(state, key))) return "חסרים פרטי ביקורת במהלך העבודה.";
        }
        if (B(state, "takenEnabled")) foreach (var key in new[] { "takenFullName", "takenPersonalNumber", "takenRank", "takenSignature" })
            if (string.IsNullOrWhiteSpace(V(state, key))) return "חסרים פרטי חתימה על קשיחים.";
        foreach (var key in new[] { "entryInspectorNumber", "interimInspectorNumber", "finalInspectorNumber", "takenPersonalNumber" })
            if (V(state, key).Length > 0 && (V(state, key).Length > 7 || !V(state, key).All(char.IsAsciiDigit))) return "מספר אישי חייב להכיל ספרות בלבד, עד 7 ספרות.";
        return null;
    }
}
