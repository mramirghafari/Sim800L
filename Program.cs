namespace SIM800L_Flasher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!AtGuard.IsAllowed("AT+CSQ", out _)) return 10;
                if (AtGuard.IsAllowed("AT+QIMEI=123456789012345", out _)) return 11;
                using var form = new MainForm();
                form.CreateControl();
                return form.Controls.Count > 0 ? 0 : 12;
            }
            catch
            {
                return 99;
            }
        }

        Application.Run(new MainForm());
        return 0;
    }
}
