using System.Text.Json.Nodes;

namespace EzEizo.Core;

public sealed partial class NativeWindow
{
    private sealed record PersonChoice(Account User)
    {
        public override string ToString() => $"{User.Name} · {User.PersonalNumber}";
    }
    private static FlowLayoutPanel FieldGrid() => new()
    {
        Width = 745, Height = 350,
        FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 0, 0, 12)
    };
    private static FlowLayoutPanel FieldCell() => new()
    {
        Width = 358, Height = 87, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Margin = new Padding(0, 0, 8, 8), BackColor = Color.White
    };
    private static void AddField(FlowLayoutPanel cell, string title, Control input)
    {
        var caption = Label(title, 30); caption.Width = 350; cell.Controls.Add(caption);
        input.Width = 350;
        cell.Controls.Add(input);
    }
    private static ComboBox Choice(FlowLayoutPanel stack, string title, IEnumerable<string> values)
    {
        var caption = Label(title);
        if (stack.Width <= 500) caption.Width = 365;
        stack.Controls.Add(caption);
        var box = new NativeChoice { Width = stack.Width <= 500 ? 350 : 440, Height = 42,
            Margin = new Padding(0, 0, 0, 12), Tag = caption };
        box.Items.Add("בחר");
        box.Items.AddRange(values.Cast<object>().ToArray());
        box.SelectedIndex = 0;
        stack.Controls.Add(box);
        return box;
    }
    private static void Options(ComboBox box, IEnumerable<string> values)
    {
        box.Items.Clear(); box.Items.Add("בחר"); box.Items.AddRange(values.Cast<object>().ToArray()); box.SelectedIndex = 0;
    }
    private static string Selected(ComboBox box) => box.SelectedIndex > 0 ? box.SelectedItem?.ToString() ?? "" : "";
    private static CheckedListBox Team(FlowLayoutPanel stack, IReadOnlyList<Account> technicians)
    {
        stack.Controls.Add(Label("צוות טכנאים", 42, true));
        stack.Controls.Add(Label("בחר לפחות שני טכנאים מרשימת המשתמשים הפעילים.", 34));
        var list = new NativeCheckedListBox { Width = 720, Height = Math.Min(192, Math.Max(78, technicians.Count * 38 + 8)),
            CheckOnClick = true, BackColor = Color.White };
        foreach (var tech in technicians) list.Items.Add(new PersonChoice(tech));
        stack.Controls.Add(list);
        if (technicians.Count == 0)
            stack.Controls.Add(new Label { Text = "אין טכנאים פעילים. צור טכנאי בניהול משתמשים לפני פתיחת משימה.",
                Width = 745, Height = 44, ForeColor = Color.FromArgb(190, 26, 42), TextAlign = ContentAlignment.MiddleLeft });
        return list;
    }
    private static Account[] Checked(CheckedListBox list) => list.CheckedItems.Cast<PersonChoice>().Select(x => x.User).ToArray();
    private static JsonObject SampleConstants() => new()
    {
        ["HUD"] = Values(("k1", "0"), ("K2", "74"), ("K3", "829"), ("K4", "-949"), ("K5", "90"), ("K6", "-719"), ("K7", "0"), ("K8", "7"), ("K9", "-88890")),
        ["CTVS"] = Values(("T1", "78"), ("T2", "789"), ("T3", "13"), ("T4", "781"), ("T5", "99"), ("T6", "11"), ("T7", "9"), ("T8", "0"), ("T9", "72939")),
        ["CAMERA"] = Values(("HG1", "793"), ("HO2", "829"), ("VG3", "829"), ("VO4", "918"), ("CS5", "0")),
        ["TFL"] = Values(("K10-K13", "0"), ("K14", "0")),
        ["INS"] = Values(("MISC37", "893"), ("MISC38", "-930"), ("MISC39", "193"))
    };
    private static JsonArray Values(params (string name, string value)[] values) =>
        new(values.Select(x => (JsonNode?)Strings([x.name, x.value])).ToArray());

    private async Task ShowCreateFault()
    {
        if (role != AppRole.Manager || account is null) return;
        var user = account;
        Loading("טוען רשימות מטוסים וטכנאים…");
        try
        {
            var technicians = await Work(() => database.Technicians());
            var constants = await Work(() => store.Constants(user));
            currentPage = "new-fault"; refreshTimer.Stop();
            var stack = Stack();
            Section(stack, "יצירת תקלה");
            stack.Controls.Add(Label("מלא את פרטי האירוע ופתח תקלה חדשה. היא תקבל מספר סידורי ותופיע ברשימה.", 48));
            Section(stack, "נתוני התקלה");
            var fields = FieldGrid(); fields.Height = 296; stack.Controls.Add(fields);
            var squadCell = FieldCell(); fields.Controls.Add(squadCell);
            var squadron = Choice(squadCell, "טייסת", NativeCatalog.Squadrons.Keys);
            var shelterCell = FieldCell(); fields.Controls.Add(shelterCell);
            var shelter = Choice(shelterCell, "דת״ק", []);
            var workshopCell = FieldCell(); fields.Controls.Add(workshopCell);
            var workshop = Choice(workshopCell, "מוסך", ["מוסך א", "מוסך ב", "כנפ״ת"]);
            workshopCell.Visible = false;
            var aircraftCell = FieldCell(); fields.Controls.Add(aircraftCell);
            var aircraft = Choice(aircraftCell, "מספר מטוס", []);
            squadron.SelectedIndexChanged += (_, _) =>
            {
                var name = Selected(squadron);
                Options(shelter, NativeCatalog.Squadrons.TryGetValue(name, out var entry) ? entry.shelters : []);
                Options(aircraft, NativeCatalog.Squadrons.TryGetValue(name, out entry) ? entry.aircraft : []);
            };
            shelter.SelectedIndexChanged += (_, _) =>
            { workshopCell.Visible = Selected(shelter) == "מוסך"; fields.Height = workshopCell.Visible ? 385 : 296; };
            var systemCell = FieldCell(); fields.Controls.Add(systemCell);
            var system = Choice(systemCell, "שם מערכת", NativeCatalog.Systems);
            var code = Input("קוד מערכת · מתמלא אוטומטית"); code.ReadOnly = true;
            var codeCell = FieldCell(); fields.Controls.Add(codeCell); AddField(codeCell, "קוד מערכת", code);
            system.SelectedIndexChanged += (_, _) =>
            { var index = Array.IndexOf(NativeCatalog.Systems, Selected(system)); code.Text = index >= 0 ? NativeCatalog.Codes[index] : ""; };
            var ewCell = FieldCell(); fields.Controls.Add(ewCell);
            var ew = Choice(ewCell, "חליפת ל״א", NativeCatalog.Ew);
            ew.Enabled = false;
            aircraft.SelectedIndexChanged += (_, _) => { ew.Enabled = Selected(aircraft).Length > 0; if (ew.Enabled) ew.SelectedIndex = 1; };
            Section(stack, "תיאור תקלה");
            stack.Controls.Add(Label("תיאור התקלה"));
            var fault = Input("תיאור התקלה", 2000); fault.Multiline = true; fault.Height = 90; stack.Controls.Add(fault);
            stack.Controls.Add(Label("הערות מנהל (לא חובה)"));
            var notes = Input("הערות", 2000); notes.Multiline = true; notes.Height = 70; stack.Controls.Add(notes);
            Section(stack, "קבועים");
            stack.Controls.Add(Label("ערוך את הערכים שיופיעו לטכנאי. שמות הקבועים נשארים קבועים.", 40));
            var constantGrid = new DataGridView { Width = 745, Height = 360, Visible = false,
                AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            constantGrid.Columns.Add("group", "מערכת"); constantGrid.Columns.Add("name", "קבוע"); constantGrid.Columns.Add("value", "ערך");
            constantGrid.Columns["group"].ReadOnly = true; constantGrid.Columns["name"].ReadOnly = true;
            void LoadConstants()
            {
                constantGrid.Rows.Clear();
                var selected = Object(constants.FirstOrDefault(x => V(x, "squadron") == Selected(squadron) && V(x, "aircraft") == Selected(aircraft))?["constants"] ?? SampleConstants());
                foreach (var (group, entries) in selected)
                    foreach (var entry in entries as JsonArray ?? new()) constantGrid.Rows.Add(group, entry?[0]?.ToString(), entry?[1]?.ToString());
            }
            squadron.SelectedIndexChanged += (_, _) => LoadConstants();
            aircraft.SelectedIndexChanged += (_, _) => LoadConstants();
            stack.Controls.Add(Button("הצג / הסתר קבועים", (_, _) => constantGrid.Visible = !constantGrid.Visible));
            stack.Controls.Add(constantGrid);
            Section(stack, "12 משימות חובה");
            stack.Controls.Add(Label("הפעולות קבועות. פעולות 2 ו־12 מוצגות לטכנאי כשעת הגעה וסיום.", 43));
            var actionList = new FlowLayoutPanel { Width = 745, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
            for (var i = 0; i < NativeCatalog.Actions.Length; i++)
                actionList.Controls.Add(Label($"{i + 1:D2}. {NativeCatalog.Actions[i]}", 47));
            stack.Controls.Add(Button("הצג / הסתר משימות", (_, _) => actionList.Visible = !actionList.Visible));
            stack.Controls.Add(actionList);
            Section(stack, "פתיחת התקלה");
            var briefing = new NativeCheckBox { Width = 745, Height = 65, Text = "תדריך בטיחות וניהול סיכונים יועבר ע״י מ״ע התקלה / משלח המשימה וייחתם על ידו." };
            stack.Controls.Add(briefing);
            var signature = new NativeSignature();
            stack.Controls.Add(Label("חתימת משלח המשימה")); stack.Controls.Add(signature);
            stack.Controls.Add(Button("נקה חתימה", (_, _) => signature.Clear()));
            var team = Team(stack, technicians);
            stack.Controls.Add(Button("פתח תקלה", async (_, _) =>
            {
                try
                {
                    var sq = Selected(squadron); var sh = Selected(shelter); var plane = Selected(aircraft); var sys = Selected(system); var people = Checked(team);
                    if (!NativeCatalog.Squadrons.TryGetValue(sq, out var catalog) || !catalog.shelters.Contains(sh) || !catalog.aircraft.Contains(plane) || sys.Length == 0 || Selected(ew).Length == 0 || sh == "מוסך" && Selected(workshop).Length == 0)
                        throw new ArgumentException("יש לבחור טייסת, דת״ק, מטוס ומערכת מהרשימות.");
                    if (people.Length < 2 || string.IsNullOrWhiteSpace(fault.Text)) throw new ArgumentException("יש להזין תיאור תקלה ולבחור לפחות שני טכנאים.");
                    if (!briefing.Checked || !signature.HasSignature) throw new ArgumentException("יש לאשר את התדריך ולחתום כמשלח המשימה.");
                    var index = Array.IndexOf(NativeCatalog.Systems, sys);
                    constantGrid.EndEdit();
                    var matching = new JsonObject();
                    foreach (DataGridViewRow row in constantGrid.Rows)
                    {
                        var group = row.Cells["group"].Value?.ToString() ?? "";
                        if (matching[group] is null) matching[group] = new JsonArray();
                        ((JsonArray)matching[group]!).Add(Strings([row.Cells["name"].Value?.ToString() ?? "", row.Cells["value"].Value?.ToString() ?? ""]));
                    }
                    var sheet = new JsonObject
                    {
                        ["squadron"] = sq, ["shelter"] = sh, ["workshop"] = sh == "מוסך" ? Selected(workshop) : "",
                        ["location"] = $"{(sh == "מוסך" ? Selected(workshop) : sh)}/{sq}", ["aircraft"] = plane,
                        ["systemName"] = sys, ["systemCode"] = NativeCatalog.Codes[index], ["ewSystem"] = Selected(ew),
                        ["fault"] = fault.Text.Trim(), ["managerNotes"] = notes.Text.Trim(),
                        ["dispatcherSignature"] = signature.DataUrl(), ["constants"] = matching,
                        ["actions"] = Strings(NativeCatalog.Actions)
                    };
                    var assignees = new JsonArray(people.Select(x => (JsonNode?)new JsonObject { ["uid"] = x.Id, ["name"] = x.Name }).ToArray());
                    var serial = await Work(() => store.CreateIssues(Json(sheet), Json(assignees), user));
                    status.Text = $"תקלה {serial} נפתחה לצוות.";
                    await OpenList("issues");
                }
                catch (Exception ex) { Error(ex); }
            }));
            Present(stack);
            status.Text = "מלא פרטים וחתימת משלח המשימה.";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task ShowCreateKey()
    {
        if (role != AppRole.Manager || account is null) return;
        var user = account;
        Loading("טוען טכנאים…");
        try
        {
            var technicians = await Work(() => database.Technicians());
            currentPage = "new-key"; refreshTimer.Stop();
            var stack = Stack();
            Section(stack, "הטענת מפתחות");
            var fields = FieldGrid(); fields.Height = 240; stack.Controls.Add(fields);
            var date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Value = DateTime.Today };
            var dateCell = FieldCell(); fields.Controls.Add(dateCell); AddField(dateCell, "תאריך", date);
            var time = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Now };
            var timeCell = FieldCell(); fields.Controls.Add(timeCell); AddField(timeCell, "שעה", time);
            var nowButton = Button("השעה עכשיו", (_, _) => time.Value = DateTime.Now);
            nowButton.Height = 32;
            nowButton.Margin = Padding.Empty;
            timeCell.Controls.Add(nowButton);
            timeCell.Height = 129;
            var squadCell = FieldCell(); fields.Controls.Add(squadCell);
            var squadron = Choice(squadCell, "טייסת", ["101", "105", "109"]);
            var shelterCell = FieldCell(); fields.Controls.Add(shelterCell);
            var shelter = Choice(shelterCell, "דת״ק", []);
            var team = Team(stack, technicians);
            Section(stack, "רשימת הטענה");
            stack.Controls.Add(Label("חפש ובחר מטוס מהטייסת וסמן לפחות סוג מפתח אחד. שורה נוספת נפתחת אוטומטית.", 46));
            var grid = new DataGridView { Width = 745, Height = 90, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            var aircraft = new DataGridViewComboBoxColumn { Name = "aircraft", HeaderText = "מספר מטוס", FlatStyle = FlatStyle.Flat,
                FillWeight = 30, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
            grid.Columns.Add(aircraft);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "blue", HeaderText = "ארז כחול", FillWeight = 13 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "shach", HeaderText = "שחק", FillWeight = 13 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "הערות", FillWeight = 35 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "remove", HeaderText = "", Text = "מחק", UseColumnTextForButtonValue = true, FillWeight = 9 });
            grid.RowTemplate.Height = 43;
            grid.ColumnHeadersHeight = 43;
            grid.RowsAdded += (_, _) => grid.Height = Math.Min(430, Math.Max(90, grid.ColumnHeadersHeight + grid.Rows.Count * grid.RowTemplate.Height + 6));
            grid.RowsRemoved += (_, _) => grid.Height = Math.Min(430, Math.Max(90, grid.ColumnHeadersHeight + grid.Rows.Count * grid.RowTemplate.Height + 6));
            grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == grid.Columns["remove"].Index && !grid.Rows[e.RowIndex].IsNewRow) grid.Rows.RemoveAt(e.RowIndex); };
            grid.EditingControlShowing += (_, e) =>
            {
                if (grid.CurrentCell?.OwningColumn.Name == "aircraft" && e.Control is ComboBox combo)
                { combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend; combo.AutoCompleteSource = AutoCompleteSource.ListItems; }
            };
            squadron.SelectedIndexChanged += (_, _) =>
            {
                var sq = Selected(squadron);
                Options(shelter, NativeCatalog.Squadrons.TryGetValue(sq, out var selected) ? selected.shelters : []);
                grid.Rows.Clear(); aircraft.Items.Clear();
                if (NativeCatalog.Squadrons.TryGetValue(sq, out selected)) aircraft.Items.AddRange(selected.aircraft.Cast<object>().ToArray());
            };
            stack.Controls.Add(grid);
            var required = Label("", 50);
            date.ValueChanged += (_, _) => required.Text = $"אחזקה נדרשת לביצוע: הטענת מטוסים במסגרת ביצוע שינוי מפתחות לחודשים {date.Value:MM}-{date.Value.AddMonths(1):MM/yy}";
            required.Text = $"אחזקה נדרשת לביצוע: הטענת מטוסים במסגרת ביצוע שינוי מפתחות לחודשים {date.Value:MM}-{date.Value.AddMonths(1):MM/yy}";
            stack.Controls.Add(required);
            stack.Controls.Add(Label("יש לוודא כי לא מופיעות תקלות: EREZ 78 לאחר טעינה ולאחר שידור PTT בארז, SAK 7 / 34 בשחק.", 52));
            stack.Controls.Add(Label("כלי עבודה / צב״ד צת״ת / חלפים נדרשים: מ.כ 2020 + חרב אש", 48));
            Section(stack, "משימות לביצוע");
            stack.Controls.Add(Label("המשימה הראשונה תאושר ותיחתם בידי משלח המשימה בעת פתיחת הדף.", 44));
            var briefing = new NativeCheckBox { Width = 745, Height = 60, Text = "תדריך בטיחות וניהול סיכונים הועבר ונחתם ע״י משלח המשימה." };
            stack.Controls.Add(briefing);
            stack.Controls.Add(Label("חתימת משלח המשימה"));
            var signature = new NativeSignature(); stack.Controls.Add(signature);
            stack.Controls.Add(Button("נקה חתימה", (_, _) => signature.Clear()));
            var taskList = new FlowLayoutPanel { Width = 745, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Visible = false };
            for (var i = 0; i < NativeCatalog.KeyActions.Length; i++)
                taskList.Controls.Add(Label($"{i + 1:D2}. {NativeCatalog.KeyActions[i]}", 47));
            stack.Controls.Add(Button("הצג את 12 המשימות לביצוע", (sender, _) =>
            {
                taskList.Visible = !taskList.Visible;
                ((Button)sender!).Text = taskList.Visible ? "הסתר את 12 המשימות" : "הצג את 12 המשימות לביצוע";
            }));
            stack.Controls.Add(taskList);
            stack.Controls.Add(Button("פתח הטענה", async (_, _) =>
            {
                try
                {
                    var sq = Selected(squadron); var sh = Selected(shelter); var people = Checked(team);
                    if (!NativeCatalog.Squadrons.TryGetValue(sq, out var entry) || !entry.shelters.Contains(sh)) throw new ArgumentException("בחר טייסת ודת״ק מהרשימות.");
                    if (people.Length < 2) throw new ArgumentException("יש לבחור לפחות שני טכנאים.");
                    if (!briefing.Checked || !signature.HasSignature) throw new ArgumentException("תדריך וחתימת משלח המשימה הם חובה.");
                    grid.EndEdit();
                    var rows = new JsonArray(); var seen = new HashSet<string>();
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (row.IsNewRow) continue;
                        var plane = row.Cells["aircraft"].Value?.ToString() ?? "";
                        var blue = row.Cells["blue"].Value is true; var shach = row.Cells["shach"].Value is true;
                        var remark = row.Cells["notes"].Value?.ToString() ?? "";
                        if (plane.Length == 0 && !blue && !shach && remark.Length == 0) continue;
                        if (!entry.aircraft.Contains(plane) || !seen.Add(plane) || !(blue || shach)) throw new ArgumentException("בכל שורה יש לבחור מטוס ייחודי וסוג מפתח אחד לפחות.");
                        rows.Add(new JsonObject { ["aircraft"] = plane, ["blue"] = blue, ["shach"] = shach, ["notes"] = remark });
                    }
                    if (rows.Count == 0) throw new ArgumentException("יש להזין לפחות מטוס אחד.");
                    var next = date.Value.AddMonths(1);
                    var period = $"{date.Value:MM}-{next:MM/yy}";
                    var load = new JsonObject
                    {
                        ["date"] = date.Value.ToString("yyyy-MM-dd"), ["time"] = time.Value.ToString("HH:mm"),
                        ["squadron"] = sq, ["shelter"] = sh, ["teamUids"] = Strings(people.Select(x => x.Id)),
                        ["teamNames"] = Strings(people.Select(x => x.Name)), ["rows"] = rows,
                        ["aircraftIds"] = Strings(seen), ["period"] = period, ["dispatcherSignature"] = signature.DataUrl()
                    };
                    await Work(() => store.CreateKey(Json(load), user));
                    await OpenList("keys");
                }
                catch (Exception ex) { Error(ex); }
            }));
            Present(stack);
            status.Text = "בחר טייסת לפני בחירת מטוסים ברשימה.";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task EditDefinition(string page, JsonNode doc)
    {
        if (account is null || role != AppRole.Manager || V(doc, "status") != "open") return;
        var user = account; var id = V(doc, "id");
        Loading("טוען טופס לעריכת מנהל…");
        try
        {
            var technicians = await Work(() => database.Technicians());
            currentPage = "edit-definition"; refreshTimer.Stop();
            var stack = Stack();
            stack.Controls.Add(Button("חזרה לטופס", async (_, _) => await OpenDocument(page, id)));
            Section(stack, "עריכת פרטי טופס פתוח");
            var sheet = page == "issues" ? doc["sheet"] ?? doc : doc;
            var squad = Choice(stack, "טייסת", NativeCatalog.Squadrons.Keys);
            squad.SelectedItem = V(sheet, "squadron");
            var shelter = Choice(stack, "דת״ק", NativeCatalog.Squadrons.TryGetValue(Selected(squad), out var current) ? current.shelters : []);
            shelter.SelectedItem = V(sheet, "shelter");
            squad.SelectedIndexChanged += (_, _) => Options(shelter, NativeCatalog.Squadrons.TryGetValue(Selected(squad), out var entry) ? entry.shelters : []);
            var aircraft = Choice(stack, "מספר מטוס", NativeCatalog.Squadrons.TryGetValue(Selected(squad), out current) ? current.aircraft : []);
            aircraft.SelectedItem = V(sheet, "aircraft");
            squad.SelectedIndexChanged += (_, _) => Options(aircraft, NativeCatalog.Squadrons.TryGetValue(Selected(squad), out var entry) ? entry.aircraft : []);
            var team = Team(stack, technicians);
            var uids = (doc[page == "issues" ? "assigneeUids" : "teamUids"] as JsonArray ?? new()).Select(x => x?.ToString()).ToHashSet();
            for (var i = 0; i < team.Items.Count; i++)
                team.SetItemChecked(i, uids.Contains(((PersonChoice)team.Items[i]!).User.Id));
            TextBox? fault = null, notes = null;
            ComboBox? system = null, ew = null, workshop = null;
            DateTimePicker? date = null, time = null;
            DataGridView? rows = null;
            if (page == "issues")
            {
                workshop = Choice(stack, "מוסך", ["מוסך א", "מוסך ב", "כנפ״ת"]);
                workshop.SelectedItem = V(sheet, "workshop");
                system = Choice(stack, "שם מערכת", NativeCatalog.Systems); system.SelectedItem = V(sheet, "systemName");
                ew = Choice(stack, "חליפת ל״א", NativeCatalog.Ew); ew.SelectedItem = V(sheet, "ewSystem");
                stack.Controls.Add(Label("תיאור התקלה")); fault = Input("תיאור התקלה", 2000); fault.Multiline = true; fault.Height = 80; fault.Text = V(sheet, "fault"); stack.Controls.Add(fault);
                stack.Controls.Add(Label("הערות מנהל")); notes = Input("הערות", 2000); notes.Multiline = true; notes.Height = 70; notes.Text = V(sheet, "managerNotes"); stack.Controls.Add(notes);
            }
            else
            {
                date = new DateTimePicker { Width = 190, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Value = DateTime.TryParse(V(sheet, "date"), out var d) ? d : DateTime.Today };
                stack.Controls.Add(Label("תאריך")); stack.Controls.Add(date);
                time = new DateTimePicker { Width = 190, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = TimeSpan.TryParse(V(sheet, "time"), out var t) ? DateTime.Today.Add(t) : DateTime.Now };
                stack.Controls.Add(Label("שעה")); stack.Controls.Add(time);
                rows = new DataGridView { Width = 740, Height = 220, AllowUserToAddRows = true, RowHeadersVisible = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
                rows.Columns.Add("aircraft", "מטוס"); rows.Columns.Add(new DataGridViewCheckBoxColumn { Name = "blue", HeaderText = "ארז כחול" });
                rows.Columns.Add(new DataGridViewCheckBoxColumn { Name = "shach", HeaderText = "שחק" }); rows.Columns.Add("notes", "הערות");
                foreach (var row in sheet["rows"] as JsonArray ?? new()) rows.Rows.Add(V(row, "aircraft"), B(row, "blue"), B(row, "shach"), V(row, "notes"));
                stack.Controls.Add(rows);
            }
            stack.Controls.Add(Button("שמור שינויים", async (_, _) =>
            {
                try
                {
                    var sq = Selected(squad); var sh = Selected(shelter); var plane = Selected(aircraft); var members = Checked(team);
                    if (!NativeCatalog.Squadrons.TryGetValue(sq, out var catalog) || !catalog.shelters.Contains(sh)) throw new ArgumentException("בחר טייסת ודת״ק מהרשימות.");
                    if (members.Length < 2) throw new ArgumentException("יש לבחור לפחות שני טכנאים.");
                    JsonObject changes;
                    if (page == "issues")
                    {
                        if (!catalog.aircraft.Contains(plane) || system is null || Selected(system).Length == 0 || ew is null || Selected(ew).Length == 0 || string.IsNullOrWhiteSpace(fault?.Text))
                            throw new ArgumentException("בחר מטוס ומערכת והזן תיאור תקלה.");
                        changes = new JsonObject
                        {
                            ["squadron"] = sq, ["shelter"] = sh, ["workshop"] = sh == "מוסך" ? Selected(workshop!) : "",
                            ["aircraft"] = plane, ["systemName"] = Selected(system), ["systemCode"] = NativeCatalog.Codes[Array.IndexOf(NativeCatalog.Systems, Selected(system))],
                            ["ewSystem"] = Selected(ew), ["fault"] = fault!.Text, ["managerNotes"] = notes?.Text ?? ""
                        };
                    }
                    else
                    {
                        rows!.EndEdit();
                        var entries = new JsonArray(); var planes = new List<string>();
                        foreach (DataGridViewRow row in rows.Rows)
                        {
                            if (row.IsNewRow) continue;
                            var number = row.Cells["aircraft"].Value?.ToString() ?? "";
                            var blue = row.Cells["blue"].Value is true; var shach = row.Cells["shach"].Value is true;
                            if (number.Length == 0 && !blue && !shach) continue;
                            if (!catalog.aircraft.Contains(number) || planes.Contains(number) || !(blue || shach)) throw new ArgumentException("בחר מטוסים ייחודיים וסוגי מפתחות תקינים.");
                            planes.Add(number);
                            entries.Add(new JsonObject { ["aircraft"] = number, ["blue"] = blue, ["shach"] = shach, ["notes"] = row.Cells["notes"].Value?.ToString() ?? "" });
                        }
                        if (entries.Count == 0) throw new ArgumentException("יש להזין מטוס אחד לפחות.");
                        var next = date!.Value.AddMonths(1);
                        changes = new JsonObject { ["date"] = date.Value.ToString("yyyy-MM-dd"), ["time"] = time!.Value.ToString("HH:mm"),
                            ["squadron"] = sq, ["shelter"] = sh, ["rows"] = entries, ["aircraftIds"] = Strings(planes),
                            ["period"] = $"{date.Value:MM}-{next:MM/yy}" };
                    }
                    await Work(() => store.Update(page == "issues" ? "issues" : "keyLoads", id, "editDefinition",
                        Json(new JsonObject { ["changes"] = changes, ["teamUids"] = Strings(members.Select(x => x.Id)) }), user));
                    await OpenDocument(page, id);
                }
                catch (Exception ex) { Error(ex); }
            }));
            Present(stack);
            status.Text = "עריכת פרטי הטופס אפשרית כל עוד טכנאי אינו מחזיק בנעילת העריכה.";
        }
        catch (Exception ex) { Error(ex); }
    }
}
