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
            refreshTimer.Stop();
            var selectedSquadron = "101";

            void ShowEditor(string squadron, string aircraft)
            {
                currentPage = "constants-editor";
                var stack = Stack();
                Section(stack, $"טייסת {squadron} · מטוס {aircraft}", "קבועים למטוס");
                var back = (NativeButton)Button("→ חזרה לרשימת המטוסים", (_, _) => ShowPicker());
                back.Secondary = true;
                stack.Controls.Add(back);
                stack.Controls.Add(Label("עדכן את ערכי הקבועים של המטוס. השינויים יחולו על תקלות חדשות.", 44));
                var record = records.FirstOrDefault(x => V(x, "squadron") == squadron && V(x, "aircraft") == aircraft);
                var constants = Object(record?["constants"] ?? SampleConstants());
                var fields = new List<(string group, string key, TextBox input)>();
                var groups = new FlowLayoutPanel
                {
                    Width = 745, Height = 402, FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = true, BackColor = Color.White, Margin = new Padding(0, 0, 0, 12)
                };
                foreach (var (groupName, entries) in constants)
                {
                    var values = entries as JsonArray ?? new JsonArray();
                    var rows = Math.Max(1, (values.Count + 2) / 3);
                    var card = new NativeCard { Width = 234, Height = 58 + rows * 62,
                        Margin = new Padding(0, 0, 10, 12), BackColor = Color.FromArgb(251, 253, 254) };
                    card.Controls.Add(new Label
                    {
                        Text = groupName, Location = new Point(12, 10), Size = new Size(210, 27),
                        TextAlign = ContentAlignment.MiddleLeft, ForeColor = Blue,
                        Font = NativeFonts.Create(10, FontStyle.Bold)
                    });
                    for (var i = 0; i < values.Count; i++)
                    {
                        var key = values[i]?[0]?.ToString() ?? "";
                        var value = values[i]?[1]?.ToString() ?? "";
                        var x = 234 - 14 - 64 - i % 3 * 72;
                        var y = 47 + i / 3 * 62;
                        card.Controls.Add(new Label
                        {
                            Text = key, Location = new Point(x, y), Size = new Size(64, 18),
                            ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter,
                            Font = NativeFonts.Create(8.25f, FontStyle.Bold)
                        });
                        var frame = new NativeInputFrame
                        {
                            Location = new Point(x, y + 20), Size = new Size(64, 34)
                        };
                        var input = new TextBox
                        {
                            Text = value, Tag = key, Location = new Point(5, 6), Size = new Size(54, 22),
                            AutoSize = false, MaxLength = 80, RightToLeft = RightToLeft.No,
                            TextAlign = HorizontalAlignment.Center, BorderStyle = BorderStyle.None,
                            BackColor = Color.White, ForeColor = Navy, Font = NativeFonts.Create(9.75f)
                        };
                        input.Enter += (_, _) => frame.Invalidate();
                        input.Leave += (_, _) => frame.Invalidate();
                        frame.Controls.Add(input);
                        card.Controls.Add(frame);
                        fields.Add((groupName, key, input));
                    }
                    groups.Controls.Add(card);
                }
                stack.Controls.Add(groups);
                var message = record is null ? "ערכי הפתיחה זהים לקבועים שנמסרו לכל המטוסים."
                    : "ערכי המטוס נטענו מהרשומה השמורה.";
                stack.Controls.Add(Label(message, 38));
                stack.Controls.Add(Button("שמור קבועים למטוס", async (_, _) =>
                {
                    var updated = new JsonObject();
                    foreach (var (group, key, input) in fields)
                    {
                        var value = input.Text.Trim();
                        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d{1,18}$"))
                        {
                            status.Text = $"יש להזין מספר תקין עבור {group} {key}.";
                            input.Focus();
                            return;
                        }
                        if (updated[group] is null) updated[group] = new JsonArray();
                        ((JsonArray)updated[group]!).Add(Strings([key, value]));
                    }
                    try
                    {
                        await Work(() =>
                        {
                            store.SaveConstants(Json(new JsonObject
                            {
                                ["squadron"] = squadron, ["aircraft"] = aircraft, ["constants"] = updated
                            }), user);
                            return true;
                        });
                        records = await Work(() => store.Constants(user));
                        status.Text = $"קבועי מטוס {aircraft} נשמרו. תקלות חדשות ישתמשו בערכים המעודכנים.";
                    }
                    catch (Exception ex) { Error(ex); }
                }));
                Present(stack);
                status.Text = $"עריכת קבועים · טייסת {squadron} · מטוס {aircraft}";
            }

            void ShowPicker()
            {
                currentPage = "constants";
                var stack = Stack();
                Section(stack, "עדכון קבועים לפי מטוס", "ניהול");
                stack.Controls.Add(Label("בחר טייסת ומטוס כדי לראות ולערוך את הקבועים שלו. לכל מטוס נשמרת רשומה משלו.", 48));
                var tabs = new FlowLayoutPanel { Width = 745, Height = 62, WrapContents = false,
                    FlowDirection = FlowDirection.RightToLeft, BackColor = Color.White };
                var buttons = new List<NativeButton>();
                var aircraft = new FlowLayoutPanel { Width = 745, Height = 320,
                    FlowDirection = FlowDirection.RightToLeft, WrapContents = true, BackColor = Color.White };
                void SelectSquadron(string name)
                {
                    selectedSquadron = name;
                    foreach (var button in buttons) { button.Active = button.Text == "טייסת " + name; button.Invalidate(); }
                    aircraft.Controls.Clear();
                    if (!NativeCatalog.Squadrons.TryGetValue(name, out var catalog)) return;
                    foreach (var plane in catalog.aircraft)
                    {
                        var updated = records.Any(x => V(x, "squadron") == name && V(x, "aircraft") == plane);
                        var chip = (NativeButton)Button("מטוס " + plane + (updated ? " · עודכן" : ""), (_, _) => ShowEditor(name, plane));
                        chip.Secondary = true;
                        chip.Width = 135; chip.Height = 42; chip.Margin = new Padding(0, 0, 9, 9);
                        aircraft.Controls.Add(chip);
                    }
                    aircraft.Height = ((catalog.aircraft.Length + 4) / 5) * 51 + 8;
                }
                foreach (var squadron in NativeCatalog.Squadrons.Keys)
                {
                    var tab = (NativeButton)Button("טייסת " + squadron, (_, _) => SelectSquadron(squadron));
                    tab.Secondary = true; tab.Width = 176; tab.Height = 45;
                    tab.Margin = new Padding(0, 0, 8, 8);
                    buttons.Add(tab); tabs.Controls.Add(tab);
                }
                stack.Controls.Add(tabs);
                stack.Controls.Add(Label("בחר מטוס מהרשימה", 38, true));
                stack.Controls.Add(aircraft);
                SelectSquadron(selectedSquadron);
                Present(stack);
                status.Text = "בחר מטוס כדי לראות ולערוך את הקבועים שלו.";
            }
            ShowPicker();
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
            Section(stack, "היסטוריית מפתחות", "שלוש טייסות");
            stack.Controls.Add(Label("ארז כחול ושחק מתעדכנים בנפרד לאחר הטענה שהושלמה. סוג מפתח שלא נטען מסומן ״טרם הוטען״.", 48));
            var tabs = new FlowLayoutPanel { Width = 745, Height = 62, WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft, BackColor = Color.White };
            stack.Controls.Add(tabs);
            var grid = new DataGridView
            {
                Width = 745, Height = 650, ReadOnly = true, RowHeadersVisible = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 64, RowTemplate = { Height = 46 },
                Tag = "key-history"
            };
            foreach (var (name, heading) in new[]
            {
                ("aircraft", "מספר מטוס"), ("blueDate", "תאריך הטענת מפתחות ארז כחול"),
                ("bluePeriod", "חודשים ארז כחול"), ("shachDate", "תאריך הטענת שחק"),
                ("shachPeriod", "חודשים שחק")
            }) grid.Columns.Add(name, heading);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.ColumnHeadersDefaultCellStyle.Font = NativeFonts.Create(9, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(234, 243, 248);
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Navy;
            grid.Font = NativeFonts.Create(9.5f);
            stack.Controls.Add(grid);
            var buttons = new List<NativeButton>();
            void SelectSquadron(string name)
            {
                foreach (var button in buttons) { button.Active = button.Text == "טייסת " + name; button.Invalidate(); }
                grid.Rows.Clear();
                if (!NativeCatalog.Squadrons.TryGetValue(name, out var catalog)) return;
                var completed = loads.Where(x => V(x, "status") == "closed" && V(x, "squadron") == name)
                    .OrderByDescending(x => V(x, "completedAt")).ToArray();
                foreach (var plane in catalog.aircraft)
                {
                    JsonNode? blue = null, shach = null;
                    foreach (var load in completed)
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
                    var latest = blue is null ? shach : shach is null ? blue :
                        string.CompareOrdinal(V(blue, "completedAt"), V(shach, "completedAt")) >= 0 ? blue : shach;
                    var bluePeriod = blue is null ? "—" : V(blue, "period");
                    var shachPeriod = shach is null ? "—" : V(shach, "period");
                    if (blue is not null && latest is not null && V(latest, "period") != bluePeriod)
                        bluePeriod += Environment.NewLine + "נדרש להשלים";
                    if (shach is not null && latest is not null && V(latest, "period") != shachPeriod)
                        shachPeriod += Environment.NewLine + "נדרש להשלים";
                    var index = grid.Rows.Add(plane, blue is null ? "טרם הוטען" : Date(blue), bluePeriod,
                        shach is null ? "טרם הוטען" : Date(shach), shachPeriod);
                    var row = grid.Rows[index];
                    foreach (var cellIndex in blue is null ? new[] { 1, 2 } : Array.Empty<int>())
                    { row.Cells[cellIndex].Style.BackColor = Color.FromArgb(255, 248, 237); row.Cells[cellIndex].Style.ForeColor = Color.FromArgb(164, 84, 25); }
                    foreach (var cellIndex in shach is null ? new[] { 3, 4 } : Array.Empty<int>())
                    { row.Cells[cellIndex].Style.BackColor = Color.FromArgb(255, 248, 237); row.Cells[cellIndex].Style.ForeColor = Color.FromArgb(164, 84, 25); }
                    if (bluePeriod.Contains("נדרש להשלים")) row.Cells[2].Style.ForeColor = Color.FromArgb(164, 84, 25);
                    if (shachPeriod.Contains("נדרש להשלים")) row.Cells[4].Style.ForeColor = Color.FromArgb(164, 84, 25);
                }
                grid.Height = 66 + grid.RowCount * 46;
                grid.ClearSelection();
                grid.CurrentCell = null;
                status.Text = $"היסטוריית מפתחות · טייסת {name} · {grid.RowCount} מטוסים";
            }
            foreach (var name in new[] { "101", "105", "109" })
            {
                var tab = (NativeButton)Button("טייסת " + name, (_, _) => SelectSquadron(name));
                tab.Secondary = true; tab.Width = 230; tab.Height = 44;
                tab.Margin = new Padding(0, 0, 9, 8);
                buttons.Add(tab); tabs.Controls.Add(tab);
            }
            SelectSquadron("101");
            grid.HandleCreated += (_, _) => grid.BeginInvoke(new Action(() => { grid.ClearSelection(); grid.CurrentCell = null; }));
            Present(stack);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(234, 243, 248);
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
            Section(stack, "סינון הדוח");
            var squadrons = new NativeCheckedListBox { Width = 440, Height = 100, CheckOnClick = true };
            squadrons.Items.AddRange(NativeCatalog.Squadrons.Keys.Cast<object>().ToArray());
            var squadCaption = Label("טייסות"); stack.Controls.Add(squadCaption); stack.Controls.Add(squadrons);
            var planes = new NativeCheckedListBox { Width = 440, Height = 130, CheckOnClick = true };
            var planeCaption = Label("מטוסים"); stack.Controls.Add(planeCaption); stack.Controls.Add(planes);
            void RefreshPlanes()
            {
                var old = planes.CheckedItems.Cast<string>().ToHashSet(); planes.Items.Clear();
                foreach (var sq in squadrons.CheckedItems.Cast<string>())
                    foreach (var plane in NativeCatalog.Squadrons[sq].aircraft) planes.Items.Add($"{sq}/{plane}", old.Contains($"{sq}/{plane}"));
            }
            squadrons.ItemCheck += (_, _) => BeginInvoke(new Action(RefreshPlanes));
            var selectAll = Button("בחר את כל המטוסים", (_, _) =>
            {
                for (var i = 0; i < squadrons.Items.Count; i++) squadrons.SetItemChecked(i, true);
                BeginInvoke(new Action(() => { RefreshPlanes(); for (var i = 0; i < planes.Items.Count; i++) planes.SetItemChecked(i, true); }));
            });
            var clearAll = Button("נקה בחירה", (_, _) => { for (var i = 0; i < planes.Items.Count; i++) planes.SetItemChecked(i, false); });
            stack.Controls.Add(selectAll); stack.Controls.Add(clearAll);
            var people = new NativeCheckedListBox { Width = 440, Height = 130, CheckOnClick = true };
            foreach (var tech in technicians) people.Items.Add(new PersonChoice(new Account(tech.Id, tech.Name, tech.PersonalNumber, "mechanic")));
            var peopleCaption = Label("טכנאים"); stack.Controls.Add(peopleCaption); stack.Controls.Add(people);
            var allDates = new NativeCheckBox { Width = 440, Height = 40, Text = "כל התקופות" }; stack.Controls.Add(allDates);
            var dates = FieldGrid(); dates.Height = 112;
            var from = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Value = DateTime.Now.AddDays(-1) };
            var fromCell = FieldCell(); AddField(fromCell, "מתאריך ושעה", from); dates.Controls.Add(fromCell);
            var until = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm", Value = DateTime.Now };
            var untilCell = FieldCell(); AddField(untilCell, "עד תאריך ושעה", until); dates.Controls.Add(untilCell);
            stack.Controls.Add(dates);
            allDates.CheckedChanged += (_, _) => { from.Enabled = !allDates.Checked; until.Enabled = !allDates.Checked; };
            var typeAndStatus = FieldGrid(); typeAndStatus.Height = 112;
            var typeCell = FieldCell(); typeAndStatus.Controls.Add(typeCell);
            var formType = Choice(typeCell, "סוג טופס", ["תקלות והטענות מפתחות", "תקלות", "הטענות מפתחות"]);
            formType.SelectedIndex = 1;
            var statusCell = FieldCell(); typeAndStatus.Controls.Add(statusCell);
            var stateFilter = Choice(statusCell, "סטטוס", ["כל המצבים", "פתוח", "ממתין לסגירה", "סגור"]);
            stateFilter.SelectedIndex = 1;
            stack.Controls.Add(typeAndStatus);
            void LayoutFilters()
            {
                var kind = report.SelectedIndex;
                people.Visible = peopleCaption.Visible = kind == 4;
                squadrons.Visible = squadCaption.Visible = kind != 0;
                planes.Visible = planeCaption.Visible = selectAll.Visible = clearAll.Visible = kind != 0;
                if (kind == 2) squadCaption.Text = "טייסת אחת ליומן טייסת";
                else squadCaption.Text = "טייסות";
            }
            report.SelectedIndexChanged += (_, _) => LayoutFilters(); LayoutFilters();
            var reportHeading = Section(stack, "תצוגה מקדימה לדוח");
            var reportOverview = Label("בחר סוג דוח והצג תוצאות כדי לראות סיכום וטפסים להורדה.", 48);
            stack.Controls.Add(reportOverview);
            var results = new DataGridView { Width = 745, Height = 360, ReadOnly = true, RowHeadersVisible = false,
                AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            results.Columns.Add("type", "סוג"); results.Columns.Add("serial", "מספר טופס"); results.Columns.Add("squad", "טייסת");
            results.Columns.Add("aircraft", "מטוס"); results.Columns.Add("status", "סטטוס"); results.Columns.Add("date", "תאריך");
            results.Visible = false; // The result cards below follow the original report layout.
            stack.Controls.Add(results);
            var summary = new FlowLayoutPanel { Width = 745, Height = 82, WrapContents = false, Visible = false };
            stack.Controls.Add(summary);
            var resultCards = new FlowLayoutPanel { Width = 745, Height = 120, WrapContents = true,
                AutoScroll = false, Visible = false };
            stack.Controls.Add(resultCards);
            JsonNode[] found = [];
            stack.Controls.Add(Button("הצג דוח", (_, _) =>
            {
                try
                {
                    if (!allDates.Checked && from.Value > until.Value) throw new ArgumentException("טווח התאריכים אינו תקין.");
                    var selected = report.SelectedIndex;
                    if (selected < 1) throw new ArgumentException("בחר סוג דוח.");
                    var sq = squadrons.CheckedItems.Cast<string>().ToHashSet();
                    var ac = planes.CheckedItems.Cast<string>().ToHashSet();
                    var ids = people.CheckedItems.Cast<PersonChoice>().Select(x => x.User.Id).ToHashSet();
                    if (selected == 2 && sq.Count != 1) throw new ArgumentException("ביומן טייסת יש לבחור טייסת אחת.");
                    if (selected == 3 && ac.Count != 1) throw new ArgumentException("בהיסטוריית מטוס יש לבחור מטוס יחיד.");
                    if (selected == 4 && ids.Count == 0) throw new ArgumentException("בחר טכנאי לדוח היסטוריית טכנאי.");
                    found = issues.Concat(keys).Where(item =>
                    {
                        var sheet = item["sheet"] ?? item;
                        var date = DateTime.TryParse(V(item, "createdAt"), out var parsed) ? parsed : DateTime.MinValue;
                        if (!allDates.Checked && (date < from.Value || date > until.Value)) return false;
                        if (formType.SelectedIndex == 2 && item["sheet"] is null || formType.SelectedIndex == 3 && item["sheet"] is not null) return false;
                        var itemStatus = V(item, "status");
                        if (stateFilter.SelectedIndex > 1 && itemStatus != (stateFilter.SelectedIndex switch
                            { 2 => "open", 3 => "pendingReview", 4 => "closed", _ => "" })) return false;
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
                    reportHeading.Text = Selected(report);
                    reportOverview.Text = $"{found.Length} טפסים · " + (allDates.Checked ? "כל התקופות" :
                        $"{from.Value:dd/MM/yyyy HH:mm}–{until.Value:dd/MM/yyyy HH:mm}");
                    results.Rows.Clear();
                    foreach (var item in found)
                    {
                        var sheet = item["sheet"] ?? item; var kind = item["sheet"] is null ? "מפתחות" : "תקלה";
                        var serial = V(item, "serial").Length > 0 ? V(item, "serial") : V(sheet, "serial");
                        results.Rows.Add(kind, serial, V(sheet, "squadron"),
                            V(sheet, "aircraft").Length > 0 ? V(sheet, "aircraft") : string.Join(", ", (item["rows"] as JsonArray ?? new()).Select(x => V(x, "aircraft"))),
                            V(item, "status") switch { "open" => "פתוח", "pendingReview" => "ממתין לסגירה", "closed" => "סגור", var state => state },
                            DisplayDate(V(item, "createdAt")));
                    }
                    summary.Controls.Clear();
                    foreach (var (label, count) in new[]
                    {
                        ("תקלות", found.Count(x => x["sheet"] is not null)),
                        ("מפתחות", found.Count(x => x["sheet"] is null)),
                        ("פתוח", found.Count(x => V(x, "status") == "open")),
                        ("ממתין לסגירה", found.Count(x => V(x, "status") == "pendingReview")),
                        ("סגור", found.Count(x => V(x, "status") == "closed"))
                    }) summary.Controls.Add(DetailTile(label, count.ToString(), 135));
                    summary.Visible = true;
                    resultCards.Controls.Clear();
                    if (found.Length == 0)
                        resultCards.Controls.Add(Label("לא נמצאו טפסים התואמים לסינון. אפשר לשנות את הבחירה ולהציג שוב.", 76));
                    foreach (var item in found)
                    {
                        var sheet = item["sheet"] ?? item;
                        var serial = V(item, "serial").Length > 0 ? V(item, "serial") : V(sheet, "serial");
                        var name = item["sheet"] is null ? "הטענת מפתחות" : "תקלה";
                        var aircraft = V(sheet, "aircraft");
                        if (aircraft.Length == 0)
                            aircraft = string.Join(", ", (item["rows"] as JsonArray ?? new()).Select(row => V(row, "aircraft")));
                        var card = new NativeCard { Width = 361, Height = 148, Margin = new Padding(0, 0, 10, 10) };
                        card.Controls.Add(new Label { Text = $"{name} {serial}", Location = new Point(12, 12), Width = 335, Height = 33,
                            TextAlign = ContentAlignment.MiddleLeft, Font = NativeFonts.Create(12, FontStyle.Bold), ForeColor = Navy });
                        card.Controls.Add(new Label { Text = $"טייסת {V(sheet, "squadron")} · מטוס {aircraft} · {V(item, "status") switch { "open" => "פתוח", "closed" => "סגור", _ => "ממתין לסגירה" }}\n{DisplayDate(V(item, "createdAt"))}",
                            Location = new Point(12, 46), Width = 335, Height = 48, TextAlign = ContentAlignment.MiddleLeft,
                            Font = NativeFonts.Create(9.75f), ForeColor = Muted });
                        var download = (NativeButton)Button("הורד טופס PDF", async (_, _) =>
                        {
                            try
                            {
                                status.Text = "מפיק את הטופס ל־PDF…";
                                var path = await Work(() => NativePdf.SaveForm(item, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
                                status.Text = $"הטופס נשמר: {path}";
                            }
                            catch (Exception ex) { Error(ex); }
                        });
                        download.Secondary = true; download.Location = new Point(12, 101);
                        card.Controls.Add(download); resultCards.Controls.Add(card);
                    }
                    resultCards.Height = Math.Max(110, (int)Math.Ceiling(found.Length / 2.0) * 158 + 10);
                    resultCards.Visible = true;
                    status.Text = $"נמצאו {found.Length} טפסים.";
                }
                catch (Exception ex) { Error(ex); }
            }));
            stack.Controls.Add(Button("הורד PDF של הדוח", async (_, _) =>
            {
                try
                {
                    if (found.Length == 0) throw new ArgumentException("יש להציג תחילה דוח עם תוצאות.");
                    status.Text = "מפיק PDF של הדוח…";
                    var file = await Work(() => NativePdf.SaveReport(found, Selected(report), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
                    status.Text = $"הדוח נשמר: {file}";
                }
                catch (Exception ex) { Error(ex); }
            }));
            stack.Controls.Add(Button("הפק CSV", (_, _) =>
            {
                try
                {
                    if (results.Rows.Count == 0) throw new ArgumentException("אין תוצאות לייצוא.");
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    var file = Path.Combine(desktop, $"דוח - {Selected(report)} - {DateTime.Now:dd-MM-yyyy HH-mm}.csv");
                    var records = new List<string> { "סוג,מספר טופס,טייסת,מטוס,סטטוס,תאריך" };
                    foreach (DataGridViewRow row in results.Rows)
                        records.Add(string.Join(",", row.Cells.Cast<DataGridViewCell>().Select(cell => "\"" + (cell.Value?.ToString() ?? "").Replace("\"", "\"\"") + "\"")));
                    File.WriteAllLines(file, records, new System.Text.UTF8Encoding(true));
                    status.Text = $"הקובץ נשמר: {file}";
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
