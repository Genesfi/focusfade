using System;
using System.IO;
using System.Windows.Forms;

namespace FocusFade.Installer
{
    static class SetupProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exeName = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
            bool isUninstall = (args != null && args.Length > 0 && args[0].IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) ||
                               exeName.Equals("Uninstall", StringComparison.OrdinalIgnoreCase);

            if (isUninstall)
            {
                Application.Run(new UninstallerForm());
            }
            else
            {
                Application.Run(new InstallerForm());
            }
        }
    }
}
