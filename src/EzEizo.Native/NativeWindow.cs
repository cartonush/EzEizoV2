using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace EzEizo.Core;

// Native Windows UI backed by the local Access store; no embedded browser.
public sealed partial class NativeWindow : Form
{
    internal string DiagnosticPage => currentPage;
    private static readonly Color Navy = Color.FromArgb(13, 48, 75);
    private static readonly Color Blue = Color.FromArgb(28, 109, 153);
    private static readonly Color Pale = Color.FromArgb(238, 245, 248);
    private static readonly Color Muted = Color.FromArgb(107, 124, 139);
    private readonly AppRole role;
    private readonly Database database = new();
    private readonly OfflineStore store = new();
    private readonly BlockingCollection<(Func<object?> work, TaskCompletionSource<object?> done)> jobs = new();
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Pale };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(14, 6, 14, 6), TextAlign = ContentAlignment.MiddleRight, BackColor = Color.White };
    private readonly Label heading = new() { ForeColor = Color.White, Font = new Font("Segoe UI", 19, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
    private readonly Label subtitle = new() { ForeColor = Color.FromArgb(195, 216, 229), Font = new Font("Segoe UI", 10), TextAlign = ContentAlignment.MiddleRight };
    private readonly Label eyebrow = new() { Text = "כנף 1 - טייסת תחזוקה", ForeColor = Color.FromArgb(176, 203, 221), Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
    private readonly Panel header = new() { Dock = DockStyle.Top, Height = 89, BackColor = Navy };
    private readonly Panel bottomNav = new() { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.FromArgb(249, 252, 253) };
    private readonly NativeButton backButton = new() { Text = "→ חזרה", Secondary = true, Width = 110, Height = 37 };
    private readonly NativeButton logoutButton = new() { Text = "התנתק", Secondary = true, Width = 105, Height = 37 };
    private string documentSection = "issues";
    private NativeCard? activeCard;
    private readonly List<NativeButton> navButtons = [];
    private Account? account;
    private string currentPage = "";
    private string searchTerm = "";
    private string statusFilter = "הכל";
    private int? lastListVersion;
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 10000 };

    public NativeWindow(AppRole role)
    {
        this.role = role;
        Text = role == AppRole.Manager ? "EzEizo · Metis" : "EzEizo · טכנאי";
        MinimumSize = new Size(960, 660);
        Size = new Size(1120, 820);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 11);
        heading.Text = role == AppRole.Manager ? "Metis · דף מנהל" : "גף אוויוניקה קרב · טכנאי";
        subtitle.Text = role == AppRole.Manager ? "יצירת דפי תקלה לטכנאים" : "דף טיפול בתקלה";
        Text += " · 2.5.1";
        var brand = new Label { Text = role == AppRole.Manager ? "מ" : "ט", Size = new Size(48, 48),
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Navy, BackColor = role == AppRole.Manager ?
                Color.FromArgb(110, 204, 184) : Color.FromArgb(88, 195, 210), Font = new Font("Segoe UI", 22, FontStyle.Bold) };
        var version = new Label { Text = "v2.5.1", Size = new Size(79, 35), ForeColor = Color.FromArgb(221, 238, 246),
            BackColor = Color.FromArgb(29, 73, 103), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        header.Controls.AddRange([brand, eyebrow, heading, subtitle, version, backButton, logoutButton]);
        header.Resize += (_, _) =>
        {
            brand.Location = new Point(header.Width - 77, 21);
            eyebrow.SetBounds(header.Width - 595, 8, 500, 18);
            heading.SetBounds(header.Width - 595, 28, 500, 29);
            subtitle.SetBounds(header.Width - 595, 60, 500, 20);
            version.Location = new Point(24, 27);
            logoutButton.Location = new Point(112, 26);
            backButton.Location = new Point(225, 26);
        };
        backButton.Click += async (_, _) =>
        {
            if (currentPage is "document" or "fault-editor" or "key-editor" or "edit-definition") await OpenList(documentSection);
            else if (currentPage is "new-key" or "key-history") await OpenList("keys");
            else if (currentPage is "users" or "constants" or "reports") await OpenManagementHome();
            else await OpenList("issues");
        };
        logoutButton.Click += async (_, _) => { await ReleaseForm(); account = null; Login(); };
        bottomNav.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(213, 226, 233) });
        bottomNav.Resize += (_, _) => LayoutBottomNav();
        if (role == AppRole.Manager)
        {
            foreach (var (title, action) in new (string, Func<Task>)[]
            {
                ("בית", OpenHome), ("תקלות", () => OpenList("issues")),
                ("מפתחות", () => OpenList("keys")), ("ניהול", OpenManagementHome)
            })
            {
                var button = new NativeButton { Text = title, Secondary = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
                button.Click += async (_, _) => await action();
                bottomNav.Controls.Add(button);
                navButtons.Add(button);
            }
        }
        content.Resize += (_, _) => CenterCard();
        Controls.Add(content);
        Controls.Add(status);
        Controls.Add(bottomNav);
        Controls.Add(header);
        var worker = new Thread(() =>
        {
            foreach (var (work, done) in jobs.GetConsumingEnumerable())
                try { done.SetResult(work()); } catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "EzEizo native Access worker" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Shown += async (_, _) =>
        {
            Loading("מכין את המסד המקומי…");
            try { await Work(() => { SharedDatabaseStartup.Initialize(); return true; }); Login(); }
            catch (Exception ex) { Error(ex); }
        };
        refreshTimer.Tick += async (_, _) =>
        {
            if (account is null || currentPage is not ("issues" or "keys" or "queue")) return;
            var page = currentPage; var user = account;
            try
            {
                var revision = await Work(() => page == "queue"
                    ? HashCode.Combine(store.Version("issues"), store.Version("keyLoads"))
                    : store.Version(page == "issues" ? "issues" : "keyLoads"));
                if (account == user && currentPage == page && revision != lastListVersion) await OpenList(page, silent: true);
            }
            catch (Exception ex) { status.Text = "בדיקת עדכונים נכשלה: " + ex.Message; }
        };
        FormClosed += (_, _) => { refreshTimer.Stop(); jobs.CompleteAdding(); };
    }

    private async Task<T> Work<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add((() => action(), result));
        return (T)(await result.Task)!;
    }
    private void Loading(string message)
    {
        status.Text = message;
        var container = Stack();
        container.Controls.Add(Label(message, 46, true));
        for (var i = 0; i < 3; i++)
            container.Controls.Add(new NativeSkeletonBlock(i == 0 ? 62 : 136));
        Present(container, loading: true);
    }
    private void Error(Exception ex)
    {
        status.Text = "הפעולה נכשלה";
        MessageBox.Show(this, ex.Message, "EzEizo", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    private static FlowLayoutPanel Stack() => new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(20, 22, 20, 28), BackColor = Color.White
    };
    private static Label Label(string text, int height = 34, bool bold = false) => new()
    {
        Text = text, Width = 750, Height = height, TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Navy, Font = new Font("Segoe UI", bold ? 17 : 11, bold ? FontStyle.Bold : FontStyle.Regular)
    };
    private static Button Button(string label, EventHandler click)
    {
        var button = new NativeButton { Text = label, Width = 175, Height = 44,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(0, 0, 7, 12) };
        button.Click += click;
        return button;
    }
    private static TextBox Input(string placeholder, int max = 200, bool password = false) => new()
    {
        PlaceholderText = placeholder, Width = 440, Height = 42, MaxLength = max,
        UseSystemPasswordChar = password, Margin = new Padding(0, 0, 0, 15),
        BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(251, 253, 254),
        ForeColor = Color.FromArgb(21, 43, 64)
    };
    private void CenterCard()
    {
        if (activeCard is null || activeCard.IsDisposed) return;
        activeCard.Width = Math.Min(850, Math.Max(760, content.ClientSize.Width - 32));
        activeCard.Height = Math.Max(200, content.ClientSize.Height - 20);
        activeCard.Location = new Point((content.ClientSize.Width - activeCard.Width) / 2, 10);
    }
    private void Present(FlowLayoutPanel stack, bool loading = false)
    {
        foreach (Control previous in content.Controls) previous.Dispose();
        content.Controls.Clear();
        var card = new NativeCard { Padding = new Padding(3) };
        stack.Dock = DockStyle.Fill;
        card.Controls.Add(stack);
        activeCard = card;
        content.Controls.Add(card);
        CenterCard();
        bottomNav.Visible = !loading && account is not null && role == AppRole.Manager;
        logoutButton.Visible = !loading && account is not null;
        backButton.Visible = !loading && account is not null &&
            currentPage is not ("home" or "management" or "issues" or "keys" or "queue");
        var section = currentPage switch
        {
            "home" => 0, "issues" or "new-fault" or "fault-editor" => 1,
            "keys" or "new-key" or "key-editor" or "key-history" => 2,
            "management" or "users" or "constants" or "reports" => 3,
            "document" or "edit-definition" => documentSection == "keys" ? 2 : 1, _ => -1
        };
        for (var i = 0; i < navButtons.Count; i++)
        {
            navButtons[i].Active = i == section;
            navButtons[i].Invalidate();
        }
        LayoutBottomNav();
    }
    private void LayoutBottomNav()
    {
        if (navButtons.Count == 0) return;
        var width = Math.Min(850, bottomNav.ClientSize.Width - 24);
        var left = (bottomNav.ClientSize.Width - width) / 2;
        var each = (width - 24) / 4;
        for (var i = 0; i < navButtons.Count; i++)
            navButtons[i].SetBounds(left + (3 - i) * (each + 8), 10, each, 53);
    }
    private async Task OpenHome()
    {
        if (account is null) { Login(); return; }
        currentPage = "home"; refreshTimer.Stop();
        var stack = Stack();
        stack.Controls.Add(Label("דף בית", 29));
        stack.Controls.Add(Label("שלום " + account.Name, 58, true));
        stack.Controls.Add(Label("בחר את תחום העבודה כדי לפתוח דף או לעקוב אחר משימות הצוות.", 45));
        var tiles = new FlowLayoutPanel { Width = 785, Height = 250, WrapContents = true };
        foreach (var (title, detail, action) in new (string, string, Func<Task>)[]
        {
            ("פתיחת תקלה", "יצירת דף טיפול חדש לטכנאים", ShowCreateFault),
            ("תקלות פתוחות וסגורות", "מעקב וחתימת בודק הדף", () => OpenList("issues")),
            ("מפתחות", "פתיחת הטענה, מעקב והיסטוריה", () => OpenList("keys")),
            ("ניהול", "משתמשים, דוחות וקבועים", OpenManagementHome)
        })
        {
            var tile = (NativeButton)Button(title + Environment.NewLine + detail, async (_, _) => await action());
            tile.Secondary = true; tile.Width = 375; tile.Height = 98;
            tiles.Controls.Add(tile);
        }
        stack.Controls.Add(tiles);
        Present(stack); status.Text = "הנתונים נשמרים במסד המקומי";
        await Task.CompletedTask;
    }
    private async Task OpenManagementHome()
    {
        if (account is null || role != AppRole.Manager) return;
        currentPage = "management"; refreshTimer.Stop();
        var stack = Stack();
        stack.Controls.Add(Label("דף ראשי", 29));
        stack.Controls.Add(Label("ניהול", 58, true));
        foreach (var (title, detail, action) in new (string, string, Func<Task>)[]
        {
            ("ניהול משתמשים", "יצירה, חיפוש והסרה של משתמשים", OpenUsers),
            ("הפקת דוחות", "סינון וייצוא טפסים", OpenReports),
            ("עדכון קבועים לפי מטוס", "עריכת ערכי המערכות למטוסים", OpenConstants)
        })
        {
            var tile = (NativeButton)Button(title + Environment.NewLine + detail, async (_, _) => await action());
            tile.Secondary = true; tile.Width = 745; tile.Height = 84;
            stack.Controls.Add(tile);
        }
        Present(stack); status.Text = "ניהול המערכת";
        await Task.CompletedTask;
    }
    private void Login()
    {
        currentPage = "login";
        refreshTimer.Stop();
        var stack = Stack();
        stack.Controls.Add(Label("גישה מאובטחת", 32));
        stack.Controls.Add(Label("כניסה למערכת", 64, true));
        stack.Controls.Add(Label("התחבר עם המספר האישי והסיסמה שלך.", 43));
        stack.Controls.Add(Label("מספר אישי", 32));
        var number = Input("7 ספרות", 7);
        stack.Controls.Add(number);
        stack.Controls.Add(Label("סיסמה", 32));
        var password = Input("6 תווים", 6, true);
        stack.Controls.Add(password);
        Button? submit = null;
        submit = Button("התחבר", async (_, _) =>
        {
            if (number.Text.Length != 7 || !number.Text.All(char.IsAsciiDigit) || password.Text.Length != 6)
            {
                status.Text = "יש להזין מספר אישי בן 7 ספרות וסיסמה בת 6 תווים.";
                return;
            }
            var digits = number.Text; var secret = password.Text;
            submit!.Enabled = false;
            status.Text = "בודק פרטי כניסה במסד המקומי…";
            try
            {
                var found = await Work(() => database.Authenticate(digits, secret, role));
                if (found is null || role == AppRole.Manager && !await Work(() => database.IsPrimaryManager(found)))
                    throw new UnauthorizedAccessException("מספר אישי או סיסמה שגויים.");
                account = found;
                if (role == AppRole.Manager) await OpenHome();
                else await OpenList("queue");
            }
            catch (Exception ex) { status.Text = "מספר אישי או סיסמה שגויים."; if (ex is not UnauthorizedAccessException) Error(ex); }
            finally { submit!.Enabled = true; }
        });
        stack.Controls.Add(submit);
        Present(stack);
        AcceptButton = submit;
        status.Text = "המערכת פועלת מקומית גם ללא חיבור לאינטרנט";
    }

    private async Task OpenList(string page, bool silent = false)
    {
        if (activeFormId is not null) await ReleaseForm();
        var user = account;
        if (user is null) { Login(); return; }
        if (role == AppRole.Mechanic) { await OpenTechnicianQueue(silent); return; }
        if (!silent) Loading(page == "issues" ? "טוען תקלות…" : "טוען הטענות מפתחות…");
        try
        {
            var version = await Work(() => store.Version(page == "issues" ? "issues" : "keyLoads"));
            var data = await Work(() => store.List(page == "issues" ? "issues" : "keyLoads", user));
            lastListVersion = version;
            if (account != user || silent && currentPage != page) return;
            currentPage = page;
            refreshTimer.Start();
            var stack = Stack();
            stack.Controls.Add(Label(role == AppRole.Manager ? "מעקב צוות" : "משימות שהתקבלו", 28));
            stack.Controls.Add(Label(page == "issues" ? role == AppRole.Manager ? "תקלות" : "רשימת משימות" : "הטענות מפתחות", 56, true));
            if (role == AppRole.Manager)
            {
                var tabs = new FlowLayoutPanel { Width = 785, Height = 64, WrapContents = false,
                    FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 18) };
                if (page == "issues")
                    tabs.Controls.Add(Button("פתיחת תקלה", async (_, _) => await ShowCreateFault()));
                else
                {
                    tabs.Controls.Add(Button("הטענה חדשה", async (_, _) => await ShowCreateKey()));
                    tabs.Controls.Add(Button("היסטוריית מפתחות", async (_, _) => await OpenKeyHistory()));
                }
                stack.Controls.Add(tabs);
            }
            stack.Controls.Add(Label(page == "issues" ? "כל דפי התקלות ורשומות הטיפול" : "דפי הטענת המפתחות של הצוות", 38));
            ComboBox? statuses = null;
            if (role == AppRole.Manager)
            {
                statuses = new ComboBox { Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
                statuses.Items.AddRange(page == "issues" ? new object[] { "הכל", "פתוח", "ממתין לסגירה", "סגור" } : new object[] { "הכל", "פתוח", "סגור" });
                statuses.SelectedItem = statusFilter;
                if (statuses.SelectedIndex < 0) { statuses.SelectedIndex = 0; statusFilter = "הכל"; }
                stack.Controls.Add(Label("סטטוס", 32)); stack.Controls.Add(statuses);
            }
            var search = Input("חפש מספר סידורי", 20);
            search.Text = searchTerm;
            stack.Controls.Add(Label(page == "issues" ? "חיפוש לפי מספר תקלה" : "חיפוש לפי מספר הטענה", 32));
            stack.Controls.Add(search);
            var cards = new FlowLayoutPanel { Width = 780, Height = 530, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            stack.Controls.Add(cards);
            void Render()
            {
                cards.Controls.Clear();
                var matching = data.Where(item =>
                    (item["serial"]?.ToString() ?? item["sheet"]?["serial"]?.ToString() ?? "").Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    (statusFilter == "הכל" || statusFilter == "פתוח" && item["status"]?.ToString() == "open" ||
                     statusFilter == "סגור" && item["status"]?.ToString() == "closed" ||
                     statusFilter == "ממתין לסגירה" && item["status"]?.ToString() == "pendingReview")).ToArray();
                if (matching.Length == 0) cards.Controls.Add(Label("אין טפסים להצגה."));
                foreach (var item in matching)
                {
                    var id = item["id"]?.ToString() ?? "";
                    var sheet = item["sheet"] ?? item;
                    var serial = item["serial"]?.ToString() ?? sheet["serial"]?.ToString() ?? "";
                    var aircraft = sheet["aircraft"]?.ToString() ?? string.Join(", ", (item["rows"] as JsonArray ?? new()).Select(x => x?["aircraft"]?.ToString()));
                    var title = $"{(page == "issues" ? "תקלה " : "הטענת מפתחות ")}{serial} · מטוס {aircraft}{Environment.NewLine}{(item["status"]?.ToString() == "open" ? "פתוח" : item["status"]?.ToString() == "closed" ? "סגור" : "ממתין לסגירה")}";
                    var card = (NativeButton)Button(title, async (_, _) => await OpenDocument(page, id));
                    card.Secondary = true;
                    card.Width = 735;
                    card.Height = 77;
                    card.TextAlign = ContentAlignment.MiddleRight;
                    cards.Controls.Add(card);
                }
            }
            if (statuses is not null) statuses.SelectedIndexChanged += (_, _) => { statusFilter = statuses.SelectedItem?.ToString() ?? "הכל"; Render(); };
            search.TextChanged += (_, _) => { searchTerm = search.Text; Render(); };
            Render();
            Present(stack);
            status.Text = $"{data.Count} טפסים · עודכן {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenTechnicianQueue(bool silent = false)
    {
        var user = account;
        if (user is null) { Login(); return; }
        if (!silent) Loading("טוען משימות צוות…");
        try
        {
            var (issues, keys, version) = await Work(() =>
                (store.List("issues", user), store.List("keyLoads", user),
                 HashCode.Combine(store.Version("issues"), store.Version("keyLoads"))));
            if (account != user || silent && currentPage != "queue") return;
            currentPage = "queue"; lastListVersion = version; refreshTimer.Start();
            var stack = Stack();
            stack.Controls.Add(Label("משימות שהתקבלו", 30));
            stack.Controls.Add(Label("רשימת משימות", 58, true));
            stack.Controls.Add(Label("בחר תקלה או הטענת מפתחות פתוחה מהרשימה כדי למלא את הטופס שלה.", 52));
            var search = Input("חפש מספר טופס", 20);
            stack.Controls.Add(search);
            var cards = new FlowLayoutPanel { Width = 780, Height = 520, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false };
            stack.Controls.Add(cards);
            void Render()
            {
                cards.Controls.Clear();
                var tasks = issues.Where(x => x["status"]?.ToString() == "open")
                    .Select(x => (page: "issues", doc: x))
                    .Concat(keys.Where(x => x["status"]?.ToString() == "open").Select(x => (page: "keys", doc: x)))
                    .Where(x => (x.doc["serial"]?.ToString() ?? "").Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (tasks.Length == 0) cards.Controls.Add(Label("אין משימות פתוחות במכשיר הזה.", 58));
                foreach (var (page, doc) in tasks)
                {
                    var id = doc["id"]?.ToString() ?? "";
                    var serial = doc["serial"]?.ToString() ?? "";
                    var sheet = doc["sheet"] ?? doc;
                    var aircraft = page == "issues" ? sheet["aircraft"]?.ToString() ?? ""
                        : string.Join(", ", (doc["rows"] as JsonArray ?? new()).Select(x => x?["aircraft"]?.ToString()));
                    var card = (NativeButton)Button(
                        $"{(page == "issues" ? "תקלה" : "הטענת מפתחות")} {serial} · מטוס {aircraft}{Environment.NewLine}{sheet["squadron"]} · משימה פתוחה",
                        async (_, _) => await OpenDocument(page, id));
                    card.Secondary = true; card.Width = 735; card.Height = 80;
                    cards.Controls.Add(card);
                }
            }
            search.TextChanged += (_, _) => Render();
            Render(); Present(stack);
            status.Text = $"{issues.Count + keys.Count} דפים משויכים · עודכן {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenUsers()
    {
        if (role != AppRole.Manager || account is null) return;
        refreshTimer.Stop();
        Loading("טוען משתמשים…");
        try
        {
            var users = await Work(() => database.AllTechnicians());
            currentPage = "users";
            var stack = Stack();
            stack.Controls.Add(Button("חזרה לתקלות", async (_, _) => await OpenList("issues")));
            stack.Controls.Add(Label("ניהול משתמשים", 55, true));
            var search = Input("חפש שם או מספר אישי", 80);
            stack.Controls.Add(search);
            var list = new FlowLayoutPanel { Width = 780, Height = 320, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            stack.Controls.Add(list);
            void Render()
            {
                list.Controls.Clear();
                foreach (var person in users.Where(x => x.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || x.PersonalNumber.Contains(search.Text)))
                {
                    var row = new FlowLayoutPanel { Width = 745, Height = 54, WrapContents = false };
                    row.Controls.Add(new Label { Text = $"{person.Name} · {person.PersonalNumber} · {(person.Active ? "פעיל" : "הוסר")}", Width = person.Active ? 450 : 310, Height = 45, TextAlign = ContentAlignment.MiddleRight });
                    row.Controls.Add(Button(person.Active ? "הסר" : "שחזר", async (_, _) =>
                    {
                        try { await Work(() => { database.SetTechnicianActive(person.Id, !person.Active); return true; }); await OpenUsers(); }
                        catch (Exception ex) { Error(ex); }
                    }));
                    if (!person.Active) row.Controls.Add(Button("מחק לצמיתות", async (_, _) =>
                    {
                        if (MessageBox.Show(this, $"למחוק לצמיתות את {person.Name}?", "מחיקה סופית", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                        try { await Work(() => { database.DeleteTechnician(person.Id); return true; }); await OpenUsers(); }
                        catch (Exception ex) { Error(ex); }
                    }));
                    list.Controls.Add(row);
                }
            }
            search.TextChanged += (_, _) => Render();
            Render();
            stack.Controls.Add(Label("יצירת טכנאי", 45, true));
            var name = Input("שם מלא", 100); stack.Controls.Add(name);
            var number = Input("מספר אישי · 7 ספרות", 7); stack.Controls.Add(number);
            var password = Input("סיסמה · 6 ספרות", 6, true); stack.Controls.Add(password);
            stack.Controls.Add(Button("צור טכנאי", async (_, _) =>
            {
                try
                {
                    if (number.Text.Length != 7 || !number.Text.All(char.IsAsciiDigit) || password.Text.Length != 6 || !password.Text.All(char.IsAsciiDigit))
                        throw new ArgumentException("מספר אישי חייב להיות בן 7 ספרות וסיסמה בת 6 ספרות.");
                    await Work(() => database.CreateAccount(name.Text, number.Text, "mechanic", password.Text));
                    await OpenUsers();
                }
                catch (Exception ex) { Error(ex); }
            }));
            Present(stack);
            status.Text = $"{users.Count} משתמשים במסד המקומי";
        }
        catch (Exception ex) { Error(ex); }
    }

    private async Task OpenDocument(string page, string id)
    {
        if (account is null) return;
        refreshTimer.Stop();
        Loading("טוען טופס…");
        try
        {
            var doc = await Work(() => store.Get(page == "issues" ? "issues" : "keyLoads", id, account));
            if (doc is null) throw new InvalidOperationException("הטופס לא נמצא במסד.");
            documentSection = page;
            currentPage = "document";
            var stack = Stack();
            stack.Controls.Add(Button("חזרה לרשימה", async (_, _) => await OpenList(page)));
            var sheet = page == "issues" ? doc["sheet"] ?? doc : doc;
            stack.Controls.Add(Label($"{(page == "issues" ? "תקלה" : "הטענת מפתחות")} {doc["serial"] ?? sheet["serial"]}", 60, true));
            foreach (var (title, key) in new[] { ("טייסת", "squadron"), ("דת״ק", "shelter"), ("מטוס", "aircraft"), ("מערכת", "systemName"), ("תיאור תקלה", "fault"), ("הערות מנהל", "managerNotes") })
                if (sheet[key] is not null) stack.Controls.Add(Label($"{title}: {sheet[key]}", key == "fault" ? 75 : 38));
            if (page == "keys")
                foreach (var row in doc["rows"] as JsonArray ?? new())
                    stack.Controls.Add(Label($"מטוס {row?["aircraft"]} · ארז כחול: {row?["blue"]} · שחק: {row?["shach"]}"));
            await AddDocumentActions(stack, page, doc);
            Present(stack);
            status.Text = "הטופס נטען מתוך Access";
        }
        catch (Exception ex) { Error(ex); await OpenList(page); }
    }
}
