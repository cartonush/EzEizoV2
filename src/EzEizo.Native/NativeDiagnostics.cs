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
                if (DateTime.UtcNow - started > TimeSpan.FromSeconds(45))
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
                if (stage == 1 && window.DiagnosticPage == "home")
                {
                    Capture(window, "native-home.png");
                    controls.OfType<Button>().First(x => x.Text == "תקלות").PerformClick(); stage = 2;
                }
                if (stage == 2 && window.DiagnosticPage == "issues")
                {
                    Capture(window, "native-issues.png");
                    controls.OfType<Button>().First(x => x.Text == "מפתחות").PerformClick(); stage = 3;
                }
                if (stage == 3 && window.DiagnosticPage == "keys")
                {
                    Capture(window, "native-keys.png");
                    controls.OfType<Button>().First(x => x.Text == "ניהול").PerformClick(); stage = 4;
                }
                if (stage == 4 && window.DiagnosticPage == "management")
                {
                    Capture(window, "native-management.png");
                    controls.OfType<Button>().First(x => x.Text.StartsWith("ניהול משתמשים")).PerformClick(); stage = 5;
                }
                if (stage == 5 && window.DiagnosticPage == "users") { Capture(window, "native-users.png"); stage = 6; window.Close(); }
            }
            catch (Exception ex) { failure = ex.ToString(); window.Close(); }
        };
        timer.Start();
        Application.Run(window);
        if (stage != 6) throw new InvalidOperationException(failure ?? "Native UI login and navigation did not complete.");
    }
    private static void Capture(Form window, string file)
    {
        using var screenshot = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, window.Size));
        screenshot.Save(Path.Combine(AppContext.BaseDirectory, file));
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
