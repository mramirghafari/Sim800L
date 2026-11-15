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
                var failures = SelfTests.Run(line => Console.WriteLine(line));
                using var form = new MainForm();
                form.CreateControl();
                if (form.Controls.Count == 0)
                {
                    Console.WriteLine("FAIL  فرم اصلی کنترل نساخت");
                    failures++;
                }
                return failures;
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
