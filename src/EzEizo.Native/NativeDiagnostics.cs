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
                    Capture(window, "native-new-key.png");
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
        window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, window.Size));
        screenshot.Save(Path.Combine(AppContext.BaseDirectory, file));
    }
    public static void VerifyTechnicianUi()
    {
        new Database().CreateAccount("טכנאי בדיקה", "7654321", "mechanic", "123456");
        using var window = new NativeWindow(AppRole.Mechanic);
        var stage = 0;
        var started = DateTime.UtcNow;
        string? failure = null;
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(45))
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
                { Capture(window, "native-technician-queue.png"); stage = 2; window.Close(); }
            }
            catch (Exception ex) { failure = ex.ToString(); window.Close(); }
        };
        timer.Start(); Application.Run(window);
        if (stage != 2) throw new InvalidOperationException(failure ?? "Technician UI did not complete.");
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
