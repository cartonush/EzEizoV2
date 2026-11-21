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
        if (args.Contains("--create-database") || args.Contains("--self-test") || args.Contains("--seed-manager") || args.Contains("--verify-template"))
        {
            try
            {
                if (args.Contains("--self-test")) SmokeTest.Run();
                else
                {
                    var db = new Database();
                    db.Initialize();
                    if (args.Contains("--seed-manager"))
                    {
                        db.EnsureDemoManager();
                        new OfflineStore().Initialize();
                    }
                    if (args.Contains("--verify-template"))
                    {
                        var manager = db.Authenticate("9862436", "-1-1-1", AppRole.Manager);
                        if (manager?.Name != "רועי סויסה" || !db.IsPrimaryManager(manager) || db.AllTechnicians().Count != 0 || db.Faults().Count != 0)
                            throw new InvalidOperationException("מסד ההתקנה הראשונית אינו תקין.");
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
