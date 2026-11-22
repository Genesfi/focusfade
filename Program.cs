using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FocusFade.UI;

namespace FocusFade
{
    static class Program
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", SetLastError = true)]
        public static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        public const int HWND_BROADCAST = 0xffff;
        public static readonly uint WM_SHOWFOCUSFADE = RegisterWindowMessage("WM_SHOWFOCUSFADE_MSG_9981");

        private static Mutex appMutex;

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID("FocusFade.AudioDimmer.App");
            }
            catch { }
            bool createdNew;
            appMutex = new Mutex(true, "Global\\FocusFade_SingleInstance_Mutex_9981", out createdNew);

            if (!createdNew)
            {
                // Already running! Signal the existing instance to pop up and restore!
                PostMessage((IntPtr)HWND_BROADCAST, WM_SHOWFOCUSFADE, IntPtr.Zero, IntPtr.Zero);
                return; // Exit this second instance immediately!
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                // Silently swallow harmless ComboBox indexing/redraw race conditions
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                // Prevent unexpected crash
            };
            Application.Run(new MainForm(args));

            GC.KeepAlive(appMutex);
        }
    }
}
