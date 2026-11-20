using EzEizo.Core;
ApplicationConfiguration.Initialize();
if (args.Contains("--create-database")) { new Database().Initialize(); return; }
Application.Run(new MainWindow(AppRole.Mechanic));
