namespace PCSetupCopier;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--verify-package")
            return UpdateManager.VerifyPackage();

        if (args.Length == 3 && args[0] == "--apply-staged-update")
        {
            if (!int.TryParse(args[2], out int previousPid) || previousPid <= 0)
                return 2;
            return UpdateManager.ApplyStagedAsync(args[1], previousPid).GetAwaiter().GetResult();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
