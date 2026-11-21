using EzEizo.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--probe-startup") || args.Contains("--ui-smoke-tech"))
        {
            try
            {
                SharedDatabaseStartup.Initialize();
                if (args.Contains("--ui-smoke-tech")) NativeDiagnostics.VerifyTechnicianUi();
                return;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "native-test-error.txt"), ex.ToString()); Environment.Exit(1); }
        }
        Application.Run(new EzEizo.Core.NativeWindow(AppRole.Mechanic));
    }
}
