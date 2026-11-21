using EzEizo.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // WebView2 and Access COM components require an STA application thread.
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("EzEizo requires an STA application thread.");
        ApplicationConfiguration.Initialize();
        if (args.Contains("--create-database") || args.Contains("--self-test") || args.Contains("--seed-template") || args.Contains("--verify-template") || args.Contains("--verify-provision") || args.Contains("--probe-startup"))
        {
            try
            {
                if (args.Contains("--self-test")) SmokeTest.Run();
                else if (args.Contains("--probe-startup")) SharedDatabaseStartup.Initialize();
                else
                {
                    var db = new Database();
                    db.Initialize();
                    if (args.Contains("--seed-template")) new OfflineStore().Initialize();
                    if (args.Contains("--verify-template"))
                    {
                        if (db.HasManager() || db.AllTechnicians().Count != 0 || db.Faults().Count != 0)
                            throw new InvalidOperationException("מסד ההתקנה הראשונית אינו תקין.");
                    }
                    if (args.Contains("--verify-provision"))
                    {
                        db.EnsureDemoManager();
                        var path = Path.Combine(Database.DataDirectory, "initial-manager.txt");
                        var password = File.ReadAllLines(path).Single(line => line.StartsWith("סיסמה ראשונית:")).Split(':')[1].Trim();
                        var manager = db.Authenticate("9862436", password, AppRole.Manager);
                        if (manager?.Name != "רועי סויסה" || !db.IsPrimaryManager(manager))
                            throw new InvalidOperationException("החשבון הראשי לא הופעל מהסיסמה המקומית.");
                        File.Delete(path);
                    }
                }
                return;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "database-error.txt"), ex.ToString());
                Environment.Exit(1);
            }
        }
        Application.Run(new MainWindow(AppRole.Manager));
    }
}
