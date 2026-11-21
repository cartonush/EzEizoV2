using EzEizo.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test") || args.Contains("--verify-pdf") || args.Contains("--probe-startup") || args.Contains("--seed-template"))
        {
            try
            {
                if (args.Contains("--self-test")) SmokeTest.Run();
                else if (args.Contains("--verify-pdf")) NativeDiagnostics.VerifyPdf();
                else if (args.Contains("--seed-template")) { new Database().Initialize(); new OfflineStore().Initialize(); }
                else SharedDatabaseStartup.Initialize();
                return;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "native-test-error.txt"), ex.ToString());
                Environment.Exit(1);
            }
        }
        Application.Run(new EzEizo.Core.NativeWindow(AppRole.Manager));
    }
}
