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
        if (args.Contains("--create-database") || args.Contains("--self-test"))
        {
            try
            {
                if (args.Contains("--self-test")) SmokeTest.Run();
                else new Database().Initialize();
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
