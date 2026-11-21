using System.Text.Json;

namespace EzEizo.Core;

public static class NativeDiagnostics
{
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    public static void VerifyUi()
    {
        using var window = new NativeWindow(AppRole.Manager);
        var stage = 0;
        var started = DateTime.UtcNow;
        string? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(90))
                { failure = "Native UI navigation timed out on " + window.DiagnosticPage; window.Close(); return; }
                var controls = Descendants(window).ToArray();
                if (stage == 0 && window.DiagnosticPage == "login")
                {
                    var file = Path.Combine(Database.DataDirectory, "initial-manager.txt");
                    if (!File.Exists(file)) return;
                    Capture(window, "native-login.png");
                    var password = File.ReadAllLines(file).Single(x => x.StartsWith("סיסמה ראשונית:")).Split(':')[1].Trim();
                    controls.OfType<TextBox>().Single(x => x.PlaceholderText == "7 ספרות").Text = "9862436";
                    controls.OfType<TextBox>().Single(x => x.PlaceholderText == "6 תווים").Text = password;
                    controls.OfType<Button>().Single(x => x.Text == "התחבר").PerformClick();
                    stage = 1;
                }
                else if (stage == 1 && window.DiagnosticPage == "home")
                {
                    Capture(window, "native-home.png");
                    controls.OfType<Button>().First(x => x.Text == "תקלות").PerformClick(); stage = 2;
                }
                else if (stage == 2 && window.DiagnosticPage == "issues")
                {
                    Capture(window, "native-issues.png");
                    controls.OfType<Button>().First(x => x.Text == "פתיחת תקלה").PerformClick(); stage = 3;
                }
                else if (stage == 3 && window.DiagnosticPage == "new-fault")
                {
                    foreach (var title in new[] { "נתוני התקלה", "תיאור תקלה", "קבועים", "12 משימות חובה", "פתיחת התקלה" })
                        if (!controls.OfType<Label>().Any(x => x.Text == title)) throw new InvalidOperationException("Missing fault section: " + title);
                    Capture(window, "native-new-fault.png");
                    controls.OfType<Button>().First(x => x.Text == "מפתחות").PerformClick(); stage = 4;
                }
                else if (stage == 4 && window.DiagnosticPage == "keys")
                {
                    Capture(window, "native-keys.png");
                    controls.OfType<Button>().First(x => x.Text == "הטענה חדשה").PerformClick(); stage = 5;
                }
                else if (stage == 5 && window.DiagnosticPage == "new-key")
                {
                    foreach (var title in new[] { "הטענת מפתחות", "רשימת הטענה", "משימות לביצוע" })
                        if (!controls.OfType<Label>().Any(x => x.Text == title)) throw new InvalidOperationException("Missing key section: " + title);
                    var keyGrid = controls.OfType<DataGridView>().Single(x => x.Columns.Contains("blue") && x.Columns.Contains("shach"));
                    if (keyGrid.Columns["blue"].FillWeight != keyGrid.Columns["shach"].FillWeight)
                        throw new InvalidOperationException("Key check columns must have equal width.");
                    Capture(window, "native-new-key.png");
                    var page = controls.OfType<FlowLayoutPanel>().FirstOrDefault(x => x.AutoScroll && x.BackColor == Color.FromArgb(242, 246, 248));
                    if (page is not null)
                    {
                        if (page.HorizontalScroll.Visible)
                            throw new InvalidOperationException($"Key form has unwanted horizontal scrolling: {page.DisplayRectangle.Width} > {page.ClientSize.Width}");
                        page.AutoScrollPosition = new Point(0, page.DisplayRectangle.Height / 2);
                        window.Update(); Capture(window, "native-new-key-middle.png");
                        page.AutoScrollPosition = new Point(0, page.DisplayRectangle.Height);
                        window.Update(); Capture(window, "native-new-key-bottom.png");
                    }
                    controls.OfType<Button>().First(x => x.Text == "ניהול").PerformClick(); stage = 6;
                }
                else if (stage == 6 && window.DiagnosticPage == "management")
                {
                    Capture(window, "native-management.png");
                    controls.OfType<Button>().First(x => x.Text.StartsWith("ניהול משתמשים")).PerformClick(); stage = 7;
                }
                else if (stage == 7 && window.DiagnosticPage == "users")
                {
                    Capture(window, "native-users.png");
                    controls.OfType<Button>().First(x => x.Text == "ניהול").PerformClick(); stage = 8;
                }
                else if (stage == 8 && window.DiagnosticPage == "management")
                { controls.OfType<Button>().First(x => x.Text.StartsWith("הפקת דוחות")).PerformClick(); stage = 9; }
                else if (stage == 9 && window.DiagnosticPage == "reports")
                {
                    Capture(window, "native-reports.png");
                    controls.OfType<Button>().First(x => x.Text == "ניהול").PerformClick(); stage = 10;
                }
                else if (stage == 10 && window.DiagnosticPage == "management")
                { controls.OfType<Button>().First(x => x.Text.StartsWith("עדכון קבועים")).PerformClick(); stage = 11; }
                else if (stage == 11 && window.DiagnosticPage == "constants")
                { Capture(window, "native-constants.png"); stage = 12; window.Close(); }
            }
            catch (Exception ex)
            {
                failure = $"Stage {stage}, page {window.DiagnosticPage}, buttons: " +
                    string.Join(" | ", Descendants(window).OfType<Button>().Select(x => x.Text)) +
                    Environment.NewLine + ex;
                window.Close();
            }
        };
        timer.Start();
        Application.Run(window);
        if (stage != 12) throw new InvalidOperationException(failure ?? "Native UI login and navigation did not complete.");
    }
    private static void Capture(Form window, string file)
    {
        using var screenshot = new Bitmap(window.Width, window.Height);
        try
        {
            window.Update();
            using (var graphics = Graphics.FromImage(screenshot))
                graphics.CopyFromScreen(window.PointToScreen(Point.Empty), Point.Empty, window.Size);
            var center = screenshot.GetPixel(window.Width / 2, window.Height / 2);
            if (center.A == 0 || center.ToArgb() == Color.Black.ToArgb())
                window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, window.Size));
        }
        catch
        {
            window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, window.Size));
        }
        screenshot.Save(Path.Combine(AppContext.BaseDirectory, file));
    }
    public static void VerifyTechnicianUi()
    {
        var db = new Database();
        var technician = db.CreateAccount("טכנאי בדיקה", "7654321", "mechanic", "123456");
        var second = db.CreateAccount("טכנאי בדיקה נוסף", "7654322", "mechanic", "123456");
        var initial = Path.Combine(Database.DataDirectory, "initial-manager.txt");
        var secret = File.ReadAllLines(initial).Single(x => x.StartsWith("סיסמה ראשונית:")).Split(':')[1].Trim();
        var manager = db.Authenticate("9862436", secret, AppRole.Manager)
            ?? throw new InvalidOperationException("The manager template is missing.");
        var store = new OfflineStore();
        using var issue = JsonDocument.Parse("""{"fault":"בדיקת ממשק תקלה","squadron":"105","shelter":"14","aircraft":"603","systemName":"מכם | FCR","systemCode":"74A","constants":{},"actions":[]}""");
        using var assignment = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { uid = technician.Id, name = technician.Name } }));
        store.CreateIssues(issue.RootElement, assignment.RootElement, manager);
        using var key = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            squadron = "105", shelter = "14", date = DateTime.Today.ToString("yyyy-MM-dd"),
            time = DateTime.Now.ToString("HH:mm"), period = "11-12/26",
            teamUids = new[] { technician.Id, second.Id }, teamNames = new[] { technician.Name, second.Name },
            rows = new[] { new { aircraft = "603", blue = true, shach = true, notes = "בדיקת ממשק" } }
        }));
        store.CreateKey(key.RootElement, manager);
        using var window = new NativeWindow(AppRole.Mechanic);
        var stage = 0;
        var started = DateTime.UtcNow;
        string? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(90))
                { failure = "Technician UI timed out on " + window.DiagnosticPage; window.Close(); return; }
                var controls = Descendants(window).ToArray();
                if (stage == 0 && window.DiagnosticPage == "login")
                {
                    Capture(window, "native-technician-login.png");
                    controls.OfType<TextBox>().Single(x => x.PlaceholderText == "7 ספרות").Text = "7654321";
                    controls.OfType<TextBox>().Single(x => x.PlaceholderText == "6 תווים").Text = "123456";
                    controls.OfType<Button>().Single(x => x.Text == "התחבר").PerformClick();
                    stage = 1;
                }
                else if (stage == 1 && window.DiagnosticPage == "queue")
                {
                    Capture(window, "native-technician-queue.png");
                    controls.OfType<Button>().First(x => x.Text.StartsWith("תקלה ")).PerformClick(); stage = 2;
                }
                else if (stage == 2 && window.DiagnosticPage == "fault-editor")
                {
                    foreach (var title in new[] { "בדיקת בטיחות המטוס", "תקלה ותיקון", "פעולות אחזקה", "יחידות שפורקו והורכבו", "כלים", "קשיחים שנלקחו", "קשיחים שנפסלו", "הצהרת הטכנאי" })
                        if (!controls.OfType<Label>().Any(x => x.Text == title))
                            throw new InvalidOperationException("Fault editor is missing: " + title);
                    Capture(window, "native-technician-fault.png");
                    CaptureScrolledPage(window, "native-technician-fault-bottom.png");
                    controls.OfType<Button>().First(x => x.Text == "→ חזרה").PerformClick(); stage = 3;
                }
                else if (stage == 3 && window.DiagnosticPage == "queue")
                {
                    controls.OfType<Button>().First(x => x.Text.StartsWith("הטענת מפתחות ")).PerformClick(); stage = 4;
                }
                else if (stage == 4 && window.DiagnosticPage == "key-editor")
                {
                    var grid = controls.OfType<DataGridView>().FirstOrDefault(x => x.Columns.Contains("blue") && x.Columns.Contains("shach"));
                    if (grid?.Rows.Count != 1) throw new InvalidOperationException("Technician key table is missing or malformed.");
                    foreach (var title in new[] { "ביצוע הטענת מפתחות", "פעולות לביצוע", "כלים", "קשיחים שנלקחו", "קשיחים שנפסלו" })
                        if (!controls.OfType<Label>().Any(x => x.Text == title))
                            throw new InvalidOperationException("Key editor is missing: " + title);
                    Capture(window, "native-technician-key.png");
                    CaptureScrolledPage(window, "native-technician-key-bottom.png");
                    stage = 5; window.Close();
                }
            }
            catch (Exception ex) { failure = ex.ToString(); window.Close(); }
        };
        timer.Start(); Application.Run(window);
        if (stage != 5) throw new InvalidOperationException(failure ?? "Technician UI did not complete.");
    }
    private static void CaptureScrolledPage(NativeWindow window, string file)
    {
        var page = Descendants(window).OfType<FlowLayoutPanel>()
            .FirstOrDefault(x => x.AutoScroll && x.BackColor == Color.FromArgb(242, 246, 248));
        if (page is null) throw new InvalidOperationException("The document has no scrollable card page.");
        if (page.HorizontalScroll.Visible)
            throw new InvalidOperationException("The document has unwanted horizontal scrolling.");
        page.AutoScrollPosition = new Point(0, page.DisplayRectangle.Height);
        window.Update(); Capture(window, file);
    }
    public static void VerifyPdf()
    {
        var db = new Database();
        var manager = db.Authenticate("1234567", "654321", AppRole.Manager)
            ?? throw new InvalidOperationException("מנהל הבדיקה לא נמצא.");
        var form = new OfflineStore().List("issues", manager).First();
        var key = new OfflineStore().List("keyLoads", manager).First();
        var folder = Path.Combine(Path.GetTempPath(), "EzEizo-pdf-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var pdf = NativePdf.SaveForm(form, folder);
            if (new FileInfo(pdf).Length < 1000) throw new InvalidOperationException("קובץ PDF ריק.");
            var keyPdf = NativePdf.SaveForm(key, folder);
            if (new FileInfo(keyPdf).Length < 1000) throw new InvalidOperationException("PDF הטענת מפתחות ריק.");
            var zip = NativePdf.SaveArchive([form, key], "יומן פעילות", folder);
            if (new FileInfo(zip).Length < 1000) throw new InvalidOperationException("קובץ ZIP ריק.");
        }
        finally { Directory.Delete(folder, true); }
    }
}
