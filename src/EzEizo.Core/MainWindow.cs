using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EzEizo.Core;

public sealed class MainWindow : Form
{
    private readonly AppRole role;
    private readonly Database db = new();
    private readonly OfflineStore store = new();
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly Label startup = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 16), Text = "טוען את המסד המקומי…" };
    private readonly BlockingCollection<(Func<object?> work, TaskCompletionSource<object?> done)> jobs = new();
    private Account? account;
    private DateTime accountVerifiedAt = DateTime.MinValue;

    public MainWindow(AppRole role)
    {
        this.role = role;
        Text = role == AppRole.Manager ? "EzEizo · Metis" : "EzEizo · טכנאי";
        MinimumSize = new Size(760, 600);
        Size = new Size(1100, 800);
        Controls.Add(web);
        Controls.Add(startup);
        var worker = new Thread(() =>
        {
            foreach (var (work, done) in jobs.GetConsumingEnumerable())
            {
                try { done.SetResult(work()); }
                catch (Exception ex) { done.SetException(ex); }
            }
        }) { IsBackground = true, Name = "EzEizo Access worker" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Shown += async (_, _) => await StartAsync();
        FormClosed += (_, _) => jobs.CompleteAdding();
    }
    private async Task<T> WithDatabase<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add((() => work(), done));
        return (T)(await done.Task)!;
    }

    private async Task StartAsync()
    {
        try
        {
            var databaseReady = WithDatabase(() => { SharedDatabaseStartup.Initialize(); return true; });
            var assets = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!File.Exists(Path.Combine(assets, "index.html")))
                throw new FileNotFoundException("קובצי הממשק חסרים. יש להעתיק גם את תיקיית wwwroot.");
            await web.EnsureCoreWebView2Async();
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("ezeizo.local", assets,
                CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.WebMessageReceived += MessageReceived;
            web.CoreWebView2.DownloadStarting += (_, e) =>
            {
                var suggested = Path.GetFileName(e.ResultFilePath);
                if (!suggested.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return;
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop)) return;
                var name = Path.GetFileNameWithoutExtension(suggested);
                var destination = Path.Combine(desktop, suggested);
                for (var i = 1; File.Exists(destination); i++)
                    destination = Path.Combine(desktop, $"{name} ({i}).pdf");
                e.ResultFilePath = destination;
            };
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://ezeizo.local/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            web.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.CoreWebView2.WebResourceRequested += (_, e) =>
            {
                if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) &&
                    ((uri.Scheme == "https" && uri.Host.Equals("ezeizo.local", StringComparison.OrdinalIgnoreCase)) ||
                     uri.Scheme is "blob" or "data")) return;
                e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Offline application", "Content-Type: text/plain");
            };
            web.Source = new Uri($"https://ezeizo.local/index.html?role={(role == AppRole.Manager ? "manager" : "mechanic")}");
            Controls.Remove(startup);
            startup.Dispose();
            await databaseReady;
        }
        catch (Exception ex)
        {
            Controls.Remove(web);
            web.Dispose();
            Controls.Remove(startup);
            startup.Dispose();
            Controls.Add(new Label { Dock = DockStyle.Fill, Padding = new Padding(30),
                Font = new Font("Segoe UI", 13), TextAlign = ContentAlignment.MiddleCenter,
                Text = $"לא ניתן לטעון את EzEizo: {ex.Message}\nמסד הנתונים: {Database.FilePath}" });
        }
    }

    private async void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string id = "";
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var request = doc.RootElement;
            id = Get(request, "id");
            var action = Get(request, "action");
            var p = request.GetProperty("payload").Clone();
            object? data = await WithDatabase(() => Process(action, p));
            Reply(id, true, data, null);
        }
        catch (Exception ex) { Reply(id, false, null, ex.Message); }
    }

    private object? Process(string action, JsonElement p)
    {
        return action switch
            {
                "login" => Login(Get(p, "number"), Get(p, "password")),
                "logout" => Logout(),
                "home" => Home(),
                "users" => Users(),
                "createUser" => CreateUser(p),
                "faults" => Faults(),
                "createFault" => CreateFault(p),
                "outcomes" => Outcomes(p),
                "saveFault" => SaveFault(p),
                "reviewFault" => ReviewFault(p),
                "offline.list" => store.List(Get(p,"collection"), RequireLogin()),
                "offline.version" => Version(p),
                "offline.get" => store.Get(Get(p,"collection"),Get(p,"id"),RequireLogin()),
                "offline.createIssues" => store.CreateIssues(p.GetProperty("sheet"),p.GetProperty("assignees"),RequireManager()),
                "offline.createKey" => store.CreateKey(p.GetProperty("load"),RequireManager()),
                "offline.update" => store.Update(Get(p,"collection"),Get(p,"id"),Get(p,"operation"),p,RequireLogin()),
                "offline.lock" => store.Lock(Get(p,"collection"),Get(p,"id"),Get(p,"session"),RequireLogin()),
                "offline.readLock" => store.ReadLock(Get(p,"id")),
                "offline.releaseLock" => ReleaseLock(p),
                "offline.constants" => store.Constants(RequireManager()),
                "offline.saveConstants" => SaveConstants(p),
                "offline.technicians" => OfflineTechnicians(),
                "offline.createTechnician" => CreateTechnician(p),
                "offline.changeTechnicianPassword" => ChangeTechnicianPassword(p),
                "offline.setTechnicianActive" => SetTechnicianActive(p),
                "offline.deleteTechnician" => DeleteTechnician(p),
                "offline.drafts" => store.Drafts(RequireLogin()),
                "offline.saveDraft" => SaveLocalDraft(p),
                "offline.removeDraft" => RemoveLocalDraft(p),
                _ => throw new InvalidOperationException("פעולה לא מוכרת.")
            };
    }

    private static string Get(JsonElement p, string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private Account RequireLogin()
    {
        var current = account ?? throw new UnauthorizedAccessException("יש להתחבר מחדש.");
        if (DateTime.UtcNow - accountVerifiedAt > TimeSpan.FromSeconds(4))
        {
            if (db.SignIn(current.PersonalNumber, role)?.Id != current.Id) { account = null; throw new UnauthorizedAccessException("המשתמש אינו פעיל. התחבר מחדש."); }
            accountVerifiedAt = DateTime.UtcNow;
        }
        return current;
    }
    private Account RequireManager()
    {
        var current = RequireLogin();
        if (role != AppRole.Manager || !db.IsPrimaryManager(current))
            throw new UnauthorizedAccessException("פעולה זו מותרת למנהל הראשי בלבד.");
        return current;
    }
    private object Login(string number, string password)
    {
        if (!Database.ValidPersonalNumber(number)) throw new ArgumentException("מספר אישי חייב להכיל בדיוק 7 ספרות.");
        if (password.Length != 6) throw new ArgumentException("סיסמה חייבת להכיל שישה תווים.");
        var found = db.Authenticate(number, password, role) ?? throw new UnauthorizedAccessException("מספר אישי או סיסמה שגויים.");
        if (role == AppRole.Manager && !db.IsPrimaryManager(found))
            throw new UnauthorizedAccessException("הכניסה למנהל הראשי בלבד.");
        account = found;
        accountVerifiedAt = DateTime.UtcNow;
        return new { found.Name, found.Role };
    }
    private object Logout() { account = null; accountVerifiedAt = DateTime.MinValue; return new { }; }
    private object Home() { var current = RequireLogin(); return new { current.Name, current.Role, uid = current.Id, email = current.PersonalNumber }; }
    private object Users() { RequireManager(); return db.Technicians(); }
    private object CreateUser(JsonElement p)
    {
        RequireManager();
        return db.CreateAccount(Get(p, "name"), Get(p, "number"), "mechanic", Get(p, "password"));
    }
    private object Faults() { var current = RequireLogin(); return db.Faults(role == AppRole.Mechanic ? current : null); }
    private object CreateFault(JsonElement p)
    {
        var manager = RequireManager();
        var ids = p.GetProperty("assignees").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        var assigned = db.Technicians().Where(x => ids.Contains(x.Id)).ToList();
        if (assigned.Count != ids.Count || ids.Count == 0) throw new ArgumentException("בחר לפחות טכנאי פעיל אחד.");
        return db.CreateFault(manager, Get(p, "squadron"), Get(p, "aircraft"), Get(p, "description"), assigned);
    }
    private Ticket FindFault(JsonElement p)
    {
        var current = RequireLogin();
        return db.Faults(role == AppRole.Mechanic ? current : null)
            .FirstOrDefault(x => x.Id == Get(p, "id")) ?? throw new UnauthorizedAccessException("אין גישה לטופס.");
    }
    private object Outcomes(JsonElement p) { var ticket = FindFault(p); return db.ActionOutcomes(ticket.Id); }
    private object SaveFault(JsonElement p)
    {
        var current = RequireLogin();
        if (role != AppRole.Mechanic) throw new UnauthorizedAccessException("רק טכנאי שהוקצה יכול לערוך.");
        var ticket = FindFault(p);
        var outcomes = p.GetProperty("outcomes").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (outcomes.Length != 12 || outcomes.Where((x, i) => x != "" && x != "V" && !(i == 10 && x == "X")).Any())
            throw new ArgumentException("נתוני המשימות אינם תקינים.");
        db.SaveFault(ticket, current, Get(p, "repair"), outcomes, p.GetProperty("close").GetBoolean());
        return new { saved = true };
    }
    private object ReviewFault(JsonElement p)
    {
        var manager = RequireManager();
        db.Review(FindFault(p), manager);
        return new { saved = true };
    }
    private object ReleaseLock(JsonElement p) { store.ReleaseLock(Get(p,"id"),Get(p,"session")); return new { }; }
    private object SaveConstants(JsonElement p) { store.SaveConstants(p,RequireManager()); return new { }; }
    private object OfflineTechnicians()
    {
        RequireManager(); return db.AllTechnicians().Select(x => new { uid=x.Id, name=x.Name, personalNumber=x.PersonalNumber, email=x.PersonalNumber, role="technician", active=x.Active, deleted=x.Deleted });
    }
    private object Version(JsonElement p)
    {
        RequireLogin();
        return store.Version(Get(p,"collection"));
    }
    private object CreateTechnician(JsonElement p)
    {
        RequireManager(); var created=db.CreateAccount(Get(p,"name"),Get(p,"number"),"mechanic",Get(p,"password"));return new { uid=created.Id };
    }
    private object ChangeTechnicianPassword(JsonElement p)
    { RequireManager(); db.ChangeTechnicianPassword(Get(p,"uid"),Get(p,"password")); return new { }; }
    private object SetTechnicianActive(JsonElement p)
    { RequireManager(); db.SetTechnicianActive(Get(p,"uid"),p.GetProperty("active").GetBoolean());return new { }; }
    private object DeleteTechnician(JsonElement p)
    { RequireManager();db.DeleteTechnician(Get(p,"uid"));return new { }; }
    private object SaveLocalDraft(JsonElement p)
    { store.SaveDraft(Get(p,"key"),Get(p,"value"),RequireLogin()); return new { }; }
    private object RemoveLocalDraft(JsonElement p)
    { store.RemoveDraft(Get(p,"key"),RequireLogin()); return new { }; }

    private void Reply(string id, bool ok, object? data, string? error)
    {
        if (web.IsDisposed || web.CoreWebView2 is null) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, data, error }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
