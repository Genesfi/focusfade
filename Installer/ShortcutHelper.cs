using System;
using System.IO;

namespace FocusFade.Installer
{
    public static class ShortcutHelper
    {
        public static void CreateShortcut(string shortcutPath, string targetPath, string arguments = "", string description = "", string iconPath = "")
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(shortcutPath);

                shortcut.TargetPath = targetPath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(arguments)) shortcut.Arguments = arguments;
                if (!string.IsNullOrEmpty(description)) shortcut.Description = description;
                if (!string.IsNullOrEmpty(iconPath)) shortcut.IconLocation = iconPath + ",0";

                shortcut.Save();
            }
            catch { }
        }
    }
}
