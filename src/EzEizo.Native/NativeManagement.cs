using System.Text.Json.Nodes;

namespace EzEizo.Core;

public sealed partial class NativeWindow
{
    private async Task OpenConstants()
    {
        if (account is null || role != AppRole.Manager) return;
        var user = account;
        Loading("טוען קבועים…");
        try
        {
            var records = await Work(() => store.Constants(user));
            currentPage = "constants"; refreshTimer.Stop();
            var stack = Stack();
            Section(stack, "עדכון קבועים לפי מטוס");
            var squad = Choice(stack, "טייסת", NativeCatalog.Squadrons.Keys);
            var plane = Choice(stack, "מטוס", []);
            var grid = new DataGridView { Width = 740, Height = 380, RowHeadersVisible = false, AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            grid.Columns.Add("group", "קבוצה"); grid.Columns.Add("key", "קבוע"); grid.Columns.Add("value", "ערך");
            grid.Columns["group"].ReadOnly = true; grid.Columns["key"].ReadOnly = true;
            stack.Controls.Add(grid);
            squad.SelectedIndexChanged += (_, _) => Options(plane, NativeCatalog.Squadrons.TryGetValue(Selected(squad), out var entry) ? entry.aircraft : []);
            plane.SelectedIndexChanged += (_, _) =>
            {
                grid.Rows.Clear();
                if (Selected(plane).Length == 0) return;
                var constants = Object(records.FirstOrDefault(x => V(x, "squadron") == Selected(squad) && V(x, "aircraft") == Selected(plane))?["constants"] ?? SampleConstants());
                foreach (var (group, entries) in constants)
                    foreach (var row in entries as JsonArray ?? new JsonArray())
                        grid.Rows.Add(group, row?[0]?.ToString(), row?[1]?.ToString());
            };
            stack.Controls.Add(Button("שמור קבועים", async (_, _) =>
            {
                if (Selected(plane).Length == 0) { status.Text = "בחר טייסת ומטוס מהרשימות."; return; }
                try
                {
                    grid.EndEdit();
                    var constants = new JsonObject();
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        var group = row.Cells["group"].Value?.ToString() ?? "";
                        if (constants[group] is null) constants[group] = new JsonArray();
                        ((JsonArray)constants[group]!).Add(Strings([row.Cells["key"].Value?.ToString() ?? "", row.Cells["value"].Value?.ToString() ?? ""]));
                    }
                    await Work(() => { store.SaveConstants(Json(new JsonObject { ["squadron"] = Selected(squad), ["aircraft"] = Selected(plane), ["constants"] = constants }), user); return true; });
                    status.Text = $"קבועי מטוס {Selected(plane)} נשמרו במסד.";
                }
                catch (Exception ex) { Error(ex); }
            }));
            Present(stack);
            status.Text = "בחר מטוס כדי לראות ולערוך את הקבועים שלו.";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenKeyHistory()
    {
        if (account is null || role != AppRole.Manager) return;
        var user = account;
        Loading("מחשב היסטוריית הטענות…");
        try
        {
            var loads = await Work(() => store.List("keyLoads", user));
            currentPage = "key-history"; refreshTimer.Stop();
            var stack = Stack();
            Section(stack, "היסטוריית מפתחות");
            var squad = Choice(stack, "טייסת", ["101", "105", "109"]);
            var grid = new DataGridView { Width = 745, Height = 500, ReadOnly = true, RowHeadersVisible = false,
                AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            foreach (var (name, heading) in new[] { ("aircraft", "מספר מטוס"), ("blueDate", "תאריך ארז כחול"), ("bluePeriod", "חודשים ארז כחול"), ("shachDate", "תאריך שחק"), ("shachPeriod", "חודשים שחק") })
                grid.Columns.Add(name, heading);
            stack.Controls.Add(grid);
            squad.SelectedIndexChanged += (_, _) =>
            {
                grid.Rows.Clear();
                var name = Selected(squad);
                if (!NativeCatalog.Squadrons.TryGetValue(name, out var catalog)) return;
                foreach (var plane in catalog.aircraft)
                {
                    JsonNode? blue = null, shach = null;
                    foreach (var load in loads.Where(x => V(x, "status") == "closed" && V(x, "squadron") == name).OrderByDescending(x => V(x, "completedAt")))
                    {
                        var rows = load["rows"] as JsonArray ?? new();
                        var work = load["work"]?["aircraft"] as JsonArray ?? new();
                        for (var i = 0; i < rows.Count; i++)
                        {
                            if (V(rows[i], "aircraft") != plane || i >= work.Count) continue;
                            if (blue is null && B(rows[i], "blue") && B(work[i], "blueDone")) blue = load;
                            if (shach is null && B(rows[i], "shach") && B(work[i], "shachDone")) shach = load;
                        }
                    }
                    grid.Rows.Add(plane, Date(blue), V(blue, "period"), Date(shach), V(shach, "period"));
                }
            };
            Present(stack);
            status.Text = "כל סוג מפתח מחושב לפי ההטענה האחרונה שהושלמה עבורו.";
        }
        catch (Exception ex) { Error(ex); }
    }
    private static string Date(JsonNode? node) => DateTime.TryParse(V(node, "date"), out var day) ? day.ToString("dd/MM/yyyy") : "—";

    private async Task OpenReports()
    {
        if (account is null || role != AppRole.Manager) return;
        var user = account;
        Loading("טוען נתונים לדוחות…");
        try
        {
            var (issues, keys, technicians) = await Work(() =>
                (store.List("issues", user), store.List("keyLoads", user), database.AllTechnicians()));
            currentPage = "reports"; refreshTimer.Stop();
            var stack = Stack();
            Section(stack, "הפקת דוחות");
            var report = Choice(stack, "סוג הדוח", ["יומן פעילות", "יומן טייסת", "היסטוריית מטוס", "היסטוריית תקלות טכנאי", "תקלות פתוחות", "דפים ממתינים לסגירה", "תקלות סגורות"]);
            var squadrons = new CheckedListBox { Width = 440, Height = 100, CheckOnClick = true };
            squadrons.Items.AddRange(NativeCatalog.Squadrons.Keys.Cast<object>().ToArray());
            stack.Controls.Add(Label("טייסות")); stack.Controls.Add(squadrons);
            var planes = new CheckedListBox { Width = 440, Height = 130, CheckOnClick = true };
            stack.Controls.Add(Label("מטוסים")); stack.Controls.Add(planes);
            squadrons.ItemCheck += (_, _) => BeginInvoke(new Action(() =>
            {
                var old = planes.CheckedItems.Cast<string>().ToHashSet(); planes.Items.Clear();
                foreach (var sq in squadrons.CheckedItems.Cast<string>())
                    foreach (var plane in NativeCatalog.Squadrons[sq].aircraft) planes.Items.Add($"{sq}/{plane}", old.Contains($"{sq}/{plane}"));
            }));
            stack.Controls.Add(Button("בחר את כל המטוסים", (_, _) => { for (var i = 0; i < planes.Items.Count; i++) planes.SetItemChecked(i, true); }));
            stack.Controls.Add(Button("נקה בחירה", (_, _) => { for (var i = 0; i < planes.Items.Count; i++) planes.SetItemChecked(i, false); }));
            var people = new CheckedListBox { Width = 440, Height = 130, CheckOnClick = true };
            foreach (var tech in technicians) people.Items.Add(new PersonChoice(new Account(tech.Id, tech.Name, tech.PersonalNumber, "mechanic")));
            stack.Controls.Add(Label("טכנאים")); stack.Controls.Add(people);
            stack.Controls.Add(Label("מתאריך ושעה"));
            var from = new DateTimePicker { Width = 220, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Value = DateTime.Now.AddDays(-1) };
            stack.Controls.Add(from);
            stack.Controls.Add(Label("עד תאריך ושעה"));
            var until = new DateTimePicker { Width = 220, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Value = DateTime.Now };
            stack.Controls.Add(until);
            var results = new DataGridView { Width = 745, Height = 360, ReadOnly = true, RowHeadersVisible = false,
                AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            results.Columns.Add("type", "סוג"); results.Columns.Add("serial", "מספר טופס"); results.Columns.Add("squad", "טייסת");
            results.Columns.Add("aircraft", "מטוס"); results.Columns.Add("status", "סטטוס"); results.Columns.Add("date", "תאריך");
            stack.Controls.Add(results);
            JsonNode[] found = [];
            stack.Controls.Add(Button("הצג דוח", (_, _) =>
            {
                try
                {
                    if (from.Value > until.Value) throw new ArgumentException("טווח התאריכים אינו תקין.");
                    var selected = report.SelectedIndex;
                    if (selected < 1) throw new ArgumentException("בחר סוג דוח.");
                    var sq = squadrons.CheckedItems.Cast<string>().ToHashSet();
                    var ac = planes.CheckedItems.Cast<string>().ToHashSet();
                    var ids = people.CheckedItems.Cast<PersonChoice>().Select(x => x.User.Id).ToHashSet();
                    if (selected == 3 && ac.Count != 1) throw new ArgumentException("בהיסטוריית מטוס יש לבחור מטוס יחיד.");
                    if (selected == 4 && ids.Count == 0) throw new ArgumentException("בחר טכנאי לדוח היסטוריית טכנאי.");
                    found = issues.Concat(keys).Where(item =>
                    {
                        var sheet = item["sheet"] ?? item;
                        var date = DateTime.TryParse(V(item, "createdAt"), out var parsed) ? parsed : DateTime.MinValue;
                        if (date < from.Value || date > until.Value) return false;
                        var squad = V(sheet, "squadron");
                        if (sq.Count > 0 && !sq.Contains(squad)) return false;
                        var aircraft = V(sheet, "aircraft");
                        if (ac.Count > 0 && !ac.Contains($"{squad}/{aircraft}") && !(item["rows"] as JsonArray ?? new()).Any(row => ac.Contains($"{squad}/{V(row, "aircraft")}"))) return false;
                        var assigned = item["assigneeUids"] as JsonArray ?? item["teamUids"] as JsonArray ?? new();
                        if (ids.Count > 0 && !assigned.Any(x => ids.Contains(x?.ToString() ?? ""))) return false;
                        return selected switch
                        {
                            5 => V(item, "status") == "open" && item["sheet"] is not null,
                            6 => V(item, "status") == "pendingReview",
                            7 => V(item, "status") == "closed",
                            _ => true
                        };
                    }).ToArray();
                    results.Rows.Clear();
                    foreach (var item in found)
                    {
                        var sheet = item["sheet"] ?? item; var kind = item["sheet"] is null ? "מפתחות" : "תקלה";
                        results.Rows.Add(kind, V(item, "serial"), V(sheet, "squadron"), V(sheet, "aircraft"), V(item, "status"), V(item, "createdAt"));
                    }
                    status.Text = $"נמצאו {found.Length} טפסים.";
                }
                catch (Exception ex) { Error(ex); }
            }));
            stack.Controls.Add(Button("הורד טופס שנבחר PDF", async (_, _) =>
            {
                try
                {
                    var index = results.CurrentRow?.Index ?? -1;
                    if (index < 0 || index >= found.Length) throw new ArgumentException("בחר שורה מתוך תוצאות החיפוש.");
                    status.Text = "מפיק את הטופס ל־PDF…";
                    var item = found[index]; var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    var path = await Work(() => NativePdf.SaveForm(item, desktop));
                    status.Text = $"הטופס נשמר: {path}";
                }
                catch (Exception ex) { Error(ex); }
            }));
            stack.Controls.Add(Button("הורד את תוצאות החיפוש", async (_, _) =>
            {
                try
                {
                    if (found.Length == 0) throw new ArgumentException("אין טפסים להורדה.");
                    if (found.Length > 5 && MessageBox.Show(this, "פעולה זו עלולה לקחת מעט זמן, האם ברצונך להמשיך?", "הורדה", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    var snapshot = found; var reportName = Selected(report);
                    status.Text = $"מפיק {snapshot.Length} טפסים לקובץ ZIP…";
                    var file = await Work(() => NativePdf.SaveArchive(snapshot, reportName, desktop));
                    status.Text = $"קובץ התוצאות נשמר: {file}";
                }
                catch (Exception ex) { Error(ex); }
            }));
            Present(stack);
            status.Text = "בחר דוח, סננים ותאריכים.";
        }
        catch (Exception ex) { Error(ex); }
    }
}
