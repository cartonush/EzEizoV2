using System.Text.Json.Nodes;

namespace EzEizo.Core;

public sealed partial class NativeWindow
{
    private sealed record PersonChoice(Account User)
    {
        public override string ToString() => $"{User.Name} · {User.PersonalNumber}";
    }
    private static ComboBox Choice(FlowLayoutPanel stack, string title, IEnumerable<string> values)
    {
        stack.Controls.Add(Label(title));
        var box = new ComboBox { Width = 440, Height = 40, DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 14) };
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
        var list = new CheckedListBox { Width = 440, Height = 145, CheckOnClick = true };
        foreach (var tech in technicians) list.Items.Add(new PersonChoice(tech));
        stack.Controls.Add(list);
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
            stack.Controls.Add(Button("חזרה", async (_, _) => await OpenList("issues")));
            Section(stack, "פתיחת תקלה חדשה");
            var squadron = Choice(stack, "טייסת", NativeCatalog.Squadrons.Keys);
            var shelter = Choice(stack, "דת״ק", []);
            var workshop = Choice(stack, "מוסך", ["מוסך א", "מוסך ב", "כנפ״ת"]);
            workshop.Visible = false;
            var aircraft = Choice(stack, "מספר מטוס", []);
            squadron.SelectedIndexChanged += (_, _) =>
            {
                var name = Selected(squadron);
                Options(shelter, NativeCatalog.Squadrons.TryGetValue(name, out var entry) ? entry.shelters : []);
                Options(aircraft, NativeCatalog.Squadrons.TryGetValue(name, out entry) ? entry.aircraft : []);
            };
            shelter.SelectedIndexChanged += (_, _) => workshop.Visible = Selected(shelter) == "מוסך";
            var system = Choice(stack, "שם מערכת", NativeCatalog.Systems);
            var ew = Choice(stack, "חליפת ל״א", NativeCatalog.Ew);
            ew.Enabled = false;
            aircraft.SelectedIndexChanged += (_, _) => { ew.Enabled = Selected(aircraft).Length > 0; if (ew.Enabled) ew.SelectedIndex = 1; };
            stack.Controls.Add(Label("תיאור התקלה"));
            var fault = Input("תיאור התקלה", 2000); fault.Multiline = true; fault.Height = 90; stack.Controls.Add(fault);
            stack.Controls.Add(Label("הערות מנהל (לא חובה)"));
            var notes = Input("הערות", 2000); notes.Multiline = true; notes.Height = 70; stack.Controls.Add(notes);
            var team = Team(stack, technicians);
            var briefing = new CheckBox { Width = 745, Height = 65, Text = "תדריך בטיחות וניהול סיכונים יועבר ע״י מ״ע התקלה / משלח המשימה וייחתם על ידו." };
            stack.Controls.Add(briefing);
            var signature = new NativeSignature();
            stack.Controls.Add(Label("חתימת משלח המשימה")); stack.Controls.Add(signature);
            stack.Controls.Add(Button("נקה חתימה", (_, _) => signature.Clear()));
            stack.Controls.Add(Button("פתח תקלה", async (_, _) =>
            {
                try
                {
                    var sq = Selected(squadron); var sh = Selected(shelter); var plane = Selected(aircraft); var sys = Selected(system); var people = Checked(team);
                    if (!NativeCatalog.Squadrons.TryGetValue(sq, out var catalog) || !catalog.shelters.Contains(sh) || !catalog.aircraft.Contains(plane) || sys.Length == 0 || Selected(ew).Length == 0 || sh == "מוסך" && Selected(workshop).Length == 0)
                        throw new ArgumentException("יש לבחור טייסת, דת״ק, מטוס ומערכת מהרשימות.");
                    if (people.Length == 0 || string.IsNullOrWhiteSpace(fault.Text)) throw new ArgumentException("יש להזין תיאור תקלה ולבחור לפחות טכנאי אחד.");
                    if (!briefing.Checked || !signature.HasSignature) throw new ArgumentException("יש לאשר את התדריך ולחתום כמשלח המשימה.");
                    var index = Array.IndexOf(NativeCatalog.Systems, sys);
                    var matching = constants.FirstOrDefault(x => V(x, "squadron") == sq && V(x, "aircraft") == plane)?["constants"]?.DeepClone() ?? SampleConstants();
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
            content.Controls.Clear(); content.Controls.Add(stack);
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
            stack.Controls.Add(Button("חזרה", async (_, _) => await OpenList("keys")));
            Section(stack, "הטענת מפתחות חדשה");
            var date = new DateTimePicker { Width = 200, Format = DateTimePickerFormat.Short, Value = DateTime.Today };
            stack.Controls.Add(Label("תאריך")); stack.Controls.Add(date);
            var time = new DateTimePicker { Width = 200, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Value = DateTime.Now };
            stack.Controls.Add(Label("שעה")); stack.Controls.Add(time);
            stack.Controls.Add(Button("השעה עכשיו", (_, _) => time.Value = DateTime.Now));
            var squadron = Choice(stack, "טייסת", ["101", "105", "109"]);
            var shelter = Choice(stack, "דת״ק", []);
            var team = Team(stack, technicians);
            var grid = new DataGridView { Width = 745, Height = 240, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            var aircraft = new DataGridViewComboBoxColumn { Name = "aircraft", HeaderText = "מספר מטוס", FlatStyle = FlatStyle.Flat };
            grid.Columns.Add(aircraft);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "blue", HeaderText = "ארז כחול", FillWeight = 35 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "shach", HeaderText = "שחק", FillWeight = 35 });
            grid.Columns.Add("notes", "הערות");
            squadron.SelectedIndexChanged += (_, _) =>
            {
                var sq = Selected(squadron);
                Options(shelter, NativeCatalog.Squadrons.TryGetValue(sq, out var selected) ? selected.shelters : []);
                grid.Rows.Clear(); aircraft.Items.Clear();
                if (NativeCatalog.Squadrons.TryGetValue(sq, out selected)) aircraft.Items.AddRange(selected.aircraft.Cast<object>().ToArray());
            };
            stack.Controls.Add(Label("רשימת הטענה", 45, true)); stack.Controls.Add(grid);
            var briefing = new CheckBox { Width = 745, Height = 60, Text = "תדריך בטיחות וניהול סיכונים הועבר ונחתם ע״י משלח המשימה." };
            stack.Controls.Add(briefing);
            stack.Controls.Add(Label("חתימת משלח המשימה"));
            var signature = new NativeSignature(); stack.Controls.Add(signature);
            stack.Controls.Add(Button("נקה חתימה", (_, _) => signature.Clear()));
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
            content.Controls.Clear(); content.Controls.Add(stack);
            status.Text = "בחר טייסת לפני בחירת מטוסים ברשימה.";
        }
        catch (Exception ex) { Error(ex); }
    }
}
