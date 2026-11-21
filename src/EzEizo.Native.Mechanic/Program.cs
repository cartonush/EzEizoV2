using EzEizo.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        NativeStartup.Mark("Mechanic", "Main started");
        try { ApplicationConfiguration.Initialize(); }
        catch (Exception ex) { NativeStartup.Failure("Mechanic", ex); return; }
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
        try
        {
            Application.ThreadException += (_, eventArgs) => NativeStartup.Failure("Mechanic", eventArgs.Exception);
            NativeStartup.Mark("Mechanic", "Opening native window");
            Application.Run(new EzEizo.Core.NativeWindow(AppRole.Mechanic));
            NativeStartup.Mark("Mechanic", "Window closed normally");
        }
        catch (Exception ex) { NativeStartup.Failure("Mechanic", ex); }
    }
}
