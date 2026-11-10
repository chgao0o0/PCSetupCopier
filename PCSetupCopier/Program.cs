namespace PCSetupCopier;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-package")
            return UpdateManager.VerifyPackage();

        if (args.Length == 1 && args[0] == "--verify-ui")
        {
            try
            {
                ApplicationConfiguration.Initialize();
                GamingSetupPage.VerifyVisualGeometry();
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,
                        "ui-layout-error.txt"), ex.ToString());
                }
                catch { }
                return 4;
            }
        }

        if (args.Length == 3 && args[0] == "--apply-staged-update")
        {
            if (!int.TryParse(args[2], out int previousPid) || previousPid <= 0)
                return 2;
            return UpdateManager.ApplyStagedAsync(args[1], previousPid).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) =>
            DiagnosticLog.Record("UIUnhandledException", "Error", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticLog.Record("UnhandledException", "Fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            DiagnosticLog.Record("UnobservedTaskException", "Error", e.Exception);
        DiagnosticLog.Record("Application", "Started");
        try { Application.Run(new MainForm()); }
        finally { DiagnosticLog.Record("Application", "Closed"); }
        return 0;
    }
}
