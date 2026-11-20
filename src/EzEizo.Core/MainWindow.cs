using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EzEizo.Core;

public sealed class MainWindow : Form
{
    private readonly AppRole role;
    private readonly Database db = new();
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private Account? account;

    public MainWindow(AppRole role)
    {
        this.role = role;
        Text = role == AppRole.Manager ? "EzEizo · Metis" : "EzEizo · טכנאי";
        MinimumSize = new Size(760, 600);
        Size = new Size(1100, 800);
        Controls.Add(web);
        Shown += async (_, _) => await StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            db.Initialize();
            var assets = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!File.Exists(Path.Combine(assets, "index.html")))
                throw new FileNotFoundException("קובצי הממשק חסרים. יש להעתיק גם את תיקיית wwwroot.");
            await web.EnsureCoreWebView2Async();
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("ezeizo.local", assets,
                CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.WebMessageReceived += MessageReceived;
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://ezeizo.local/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            web.Source = new Uri($"https://ezeizo.local/index.html?role={(role == AppRole.Manager ? "manager" : "mechanic")}");
        }
        catch (Exception ex)
        {
            Controls.Remove(web);
            web.Dispose();
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
            var p = request.GetProperty("payload");
            object? data = action switch
            {
                "login" => Login(Get(p, "number")),
                "logout" => Logout(),
                "home" => Home(),
                "users" => Users(),
                "createUser" => CreateUser(p),
                "faults" => Faults(),
                "createFault" => CreateFault(p),
                "outcomes" => Outcomes(p),
                "saveFault" => SaveFault(p),
                "reviewFault" => ReviewFault(p),
                _ => throw new InvalidOperationException("פעולה לא מוכרת.")
            };
            Reply(id, true, data, null);
        }
        catch (Exception ex) { Reply(id, false, null, ex.Message); }
        await Task.CompletedTask;
    }

    private static string Get(JsonElement p, string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private Account RequireLogin() => account ?? throw new UnauthorizedAccessException("יש להתחבר מחדש.");
    private Account RequireManager()
    {
        var current = RequireLogin();
        if (role != AppRole.Manager || !db.IsPrimaryManager(current))
            throw new UnauthorizedAccessException("פעולה זו מותרת למנהל הראשי בלבד.");
        return current;
    }
    private object Login(string number)
    {
        if (!Database.ValidPersonalNumber(number)) throw new ArgumentException("מספר אישי חייב להכיל בדיוק 7 ספרות.");
        var found = db.SignIn(number, role) ?? throw new UnauthorizedAccessException("לא נמצא משתמש פעיל בתפקיד המתאים.");
        if (role == AppRole.Manager && !db.IsPrimaryManager(found))
            throw new UnauthorizedAccessException("הכניסה למנהל הראשי בלבד.");
        account = found;
        return new { found.Name, found.Role };
    }
    private object Logout() { account = null; return new { }; }
    private object Home() { var current = RequireLogin(); return new { current.Name, current.Role }; }
    private object Users() { RequireManager(); return db.Technicians(); }
    private object CreateUser(JsonElement p)
    {
        RequireManager();
        return db.CreateAccount(Get(p, "name"), Get(p, "number"), "mechanic");
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
    private void Reply(string id, bool ok, object? data, string? error)
    {
        if (web.IsDisposed || web.CoreWebView2 is null) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok, data, error }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
