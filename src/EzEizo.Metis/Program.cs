using EzEizo.Core;
ApplicationConfiguration.Initialize();
if (args.Contains("--create-database"))
{
    try { new Database().Initialize(); return; }
    catch (Exception ex)
    {
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "database-error.txt"), ex.ToString());
        Environment.Exit(1);
    }
}
Application.Run(new MainWindow(AppRole.Manager));
