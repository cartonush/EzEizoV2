using EzEizo.Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new EzEizo.Core.NativeWindow(AppRole.Mechanic));
    }
}
