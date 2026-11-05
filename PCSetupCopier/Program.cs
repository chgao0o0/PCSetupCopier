namespace PCSetupCopier;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-package")
        {
            Environment.ExitCode = UpdateManager.VerifyPackage();
            return;
        }

        if (args.Length == 3 && args[0] == "--apply-staged-update")
        {
            if (!int.TryParse(args[2], out int previousPid) || previousPid <= 0)
                return;
            Environment.ExitCode = await UpdateManager.ApplyStagedAsync(args[1], previousPid);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
