using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace EzEizo.Core;

// Native Windows UI prototype. This project deliberately does not reference WebView2.
// It is built alongside the existing release until every workflow has been ported and tested.
public sealed class NativeWindow : Form
{
    private static readonly Color Navy = Color.FromArgb(13, 48, 75);
    private static readonly Color Blue = Color.FromArgb(28, 109, 153);
    private static readonly Color Pale = Color.FromArgb(238, 245, 248);
    private readonly AppRole role;
    private readonly Database database = new();
    private readonly OfflineStore store = new();
    private readonly BlockingCollection<(Func<object?> work, TaskCompletionSource<object?> done)> jobs = new();
    private readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Pale };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(14, 6, 14, 6), TextAlign = ContentAlignment.MiddleRight, BackColor = Color.White };
    private readonly Label heading = new() { Dock = DockStyle.Fill, ForeColor = Color.White, Font = new Font("Segoe UI", 19, FontStyle.Bold), TextAlign = ContentAlignment.MiddleRight };
    private Account? account;
    private string currentPage = "";
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 10000 };

    public NativeWindow(AppRole role)
    {
        this.role = role;
        Text = role == AppRole.Manager ? "EzEizo · Metis" : "EzEizo · טכנאי";
        MinimumSize = new Size(810, 620);
        Size = new Size(1120, 820);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = new Font("Segoe UI", 11);
        var header = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = Navy, Padding = new Padding(26, 12, 26, 12) };
        heading.Text = role == AppRole.Manager ? "Metis · דף מנהל" : "גף אוויוניקה קרב · טכנאי";
        header.Controls.Add(heading);
        Controls.Add(content);
        Controls.Add(status);
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
            if (account is null || currentPage is not ("issues" or "keys")) return;
            await OpenList(currentPage, silent: true);
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
        content.Controls.Clear();
        var container = Stack();
        for (var i = 0; i < 3; i++)
            container.Controls.Add(new Panel { Width = 760, Height = 70, Margin = new Padding(0, 0, 0, 18), BackColor = Color.FromArgb(218, 230, 236) });
        content.Controls.Add(container);
    }
    private void Error(Exception ex)
    {
        status.Text = "הפעולה נכשלה";
        MessageBox.Show(this, ex.Message, "EzEizo", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    private static FlowLayoutPanel Stack() => new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(30), BackColor = Pale
    };
    private static Label Label(string text, int height = 34, bool bold = false) => new()
    {
        Text = text, Width = 750, Height = height, TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Navy, Font = new Font("Segoe UI", bold ? 14 : 11, bold ? FontStyle.Bold : FontStyle.Regular)
    };
    private static Button Button(string label, EventHandler click)
    {
        var button = new Button { Text = label, Width = 175, Height = 44, FlatStyle = FlatStyle.Flat,
            BackColor = Blue, ForeColor = Color.White, Margin = new Padding(0, 0, 0, 12) };
        button.FlatAppearance.BorderSize = 0;
        button.Click += click;
        return button;
    }
    private static TextBox Input(string placeholder, int max = 200, bool password = false) => new()
    {
        PlaceholderText = placeholder, Width = 440, Height = 40, MaxLength = max,
        UseSystemPasswordChar = password, Margin = new Padding(0, 0, 0, 14)
    };
    private void Login()
    {
        currentPage = "login";
        refreshTimer.Stop();
        content.Controls.Clear();
        var stack = Stack();
        stack.Controls.Add(Label("כניסה למערכת", 65, true));
        stack.Controls.Add(Label("מספר אישי"));
        var number = Input("7 ספרות", 7);
        stack.Controls.Add(number);
        stack.Controls.Add(Label("סיסמה"));
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
            submit.Enabled = false;
            status.Text = "בודק פרטי כניסה במסד המקומי…";
            try
            {
                var found = await Work(() => database.Authenticate(digits, secret, role));
                if (found is null || role == AppRole.Manager && !await Work(() => database.IsPrimaryManager(found)))
                    throw new UnauthorizedAccessException("מספר אישי או סיסמה שגויים.");
                account = found;
                await OpenList("issues");
            }
            catch (Exception ex) { status.Text = "מספר אישי או סיסמה שגויים."; if (ex is not UnauthorizedAccessException) Error(ex); }
            finally { submit.Enabled = true; }
        });
        stack.Controls.Add(submit);
        content.Controls.Add(stack);
        AcceptButton = submit;
        status.Text = "הנתונים נשמרים במחשב זה בלבד";
    }

    private async Task OpenList(string page, bool silent = false)
    {
        var user = account;
        if (user is null) { Login(); return; }
        if (!silent) Loading(page == "issues" ? "טוען תקלות…" : "טוען הטענות מפתחות…");
        try
        {
            var data = await Work(() => store.List(page == "issues" ? "issues" : "keyLoads", user));
            if (account != user || silent && currentPage != page) return;
            currentPage = page;
            refreshTimer.Start();
            var stack = Stack();
            var nav = new FlowLayoutPanel { Width = 780, Height = 65, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            nav.Controls.Add(Button("תקלות", async (_, _) => await OpenList("issues")));
            nav.Controls.Add(Button("מפתחות", async (_, _) => await OpenList("keys")));
            nav.Controls.Add(Button("התנתק", (_, _) => { account = null; Login(); }));
            stack.Controls.Add(nav);
            stack.Controls.Add(Label(page == "issues" ? "תקלות" : "הטענות מפתחות", 55, true));
            var search = Input("חפש מספר סידורי", 20);
            stack.Controls.Add(search);
            var cards = new FlowLayoutPanel { Width = 780, Height = 530, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            stack.Controls.Add(cards);
            void Render()
            {
                cards.Controls.Clear();
                var matching = data.Where(item => (item["serial"]?.ToString() ?? item["sheet"]?["serial"]?.ToString() ?? "").Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Length == 0) cards.Controls.Add(Label("אין טפסים להצגה."));
                foreach (var item in matching)
                {
                    var id = item["id"]?.ToString() ?? "";
                    var sheet = item["sheet"] ?? item;
                    var serial = item["serial"]?.ToString() ?? sheet["serial"]?.ToString() ?? "";
                    var aircraft = sheet["aircraft"]?.ToString() ?? string.Join(", ", (item["rows"] as JsonArray ?? new()).Select(x => x?["aircraft"]?.ToString()));
                    var title = $"{serial} · מטוס {aircraft} · {item["status"]}";
                    var card = Button(title, async (_, _) => await OpenDocument(page, id));
                    card.Width = 735;
                    card.Height = 65;
                    card.TextAlign = ContentAlignment.MiddleRight;
                    cards.Controls.Add(card);
                }
            }
            search.TextChanged += (_, _) => Render();
            Render();
            content.Controls.Clear(); content.Controls.Add(stack);
            status.Text = $"{data.Count} טפסים · עודכן {DateTime.Now:HH:mm:ss}";
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
            stack.Controls.Add(Label("תצוגה מקומית ראשונית: עריכת טפסים, חתימות ודוחות עדיין בבנייה.", 70));
            content.Controls.Clear(); content.Controls.Add(stack);
            status.Text = "הטופס נטען מתוך Access";
        }
        catch (Exception ex) { Error(ex); await OpenList(page); }
    }
}
