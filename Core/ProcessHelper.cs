using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FocusFade.Core;

namespace FocusFade.Core
{
    public static class ProcessHelper
    {
        private static readonly Dictionary<uint, string> ProcCache = new Dictionary<uint, string>();
        private static readonly Dictionary<string, Image> RealIconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Image> FallbackIconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static DateTime lastPrune = DateTime.UtcNow;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern IntPtr GetClassLong32(IntPtr hWnd, int nIndex);

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetClassLongPtr64(hWnd, nIndex);
            return GetClassLong32(hWnd, nIndex);
        }

        private const uint WM_GETICON = 0x007F;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL2 = 2;
        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr ExitStatus;
            public IntPtr PebBaseAddress;
            public IntPtr AffinityMask;
            public IntPtr BasePriority;
            public UIntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        private const int ProcessCommandLineInformation = 60;

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

        [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
        private static extern int NtQueryInformationProcessBuf(IntPtr processHandle, int processInformationClass, IntPtr processInformation, int processInformationLength, out int returnLength);

        private static readonly Dictionary<uint, uint> ParentPidCache = new Dictionary<uint, uint>();
        private static readonly Dictionary<uint, string> WebViewHostCache = new Dictionary<uint, string>();

        public static string GetProcessName(uint pid)
        {
            if (pid == 0) return "";
            string cached;
            if (ProcCache.TryGetValue(pid, out cached)) return cached;

            // Ultra-lightweight Win32 query without loading managed Process diagnostic tables
            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                    {
                        string fullName = sb.ToString();
                        string name = Path.GetFileNameWithoutExtension(fullName);
                        if (!string.IsNullOrEmpty(name))
                        {
                            ProcCache[pid] = name;
                            return name;
                        }
                    }
                }
                finally
                {
                    CloseHandle(hProc);
                }
            }

            try
            {
                using (var proc = Process.GetProcessById((int)pid))
                {
                    string name = proc.ProcessName;
                    ProcCache[pid] = name;
                    return name;
                }
            }
            catch
            {
                return "";
            }
        }

        public static Image GetProcessIcon(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return null;

            // 1. Check if we already have the REAL extracted icon
            Image real;
            if (RealIconCache.TryGetValue(procName, out real))
            {
                return real;
            }

            // 2. Try extracting real icon from running window / process / executable
            Image extracted = TryExtractIcon(procName);
            if (extracted != null)
            {
                RealIconCache[procName] = extracted;
                return extracted;
            }

            // 3. Check fallback cache (temporary until app starts running)
            Image fallback;
            if (FallbackIconCache.TryGetValue(procName, out fallback))
            {
                return fallback;
            }

            fallback = GenerateFallbackIcon(procName);
            FallbackIconCache[procName] = fallback;
            return fallback;
        }

        public static bool HasRealIcon(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return false;
            return RealIconCache.ContainsKey(procName);
        }

        public static Image TryExtractIcon(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return null;
            if (string.Equals(procName, "Microphone", StringComparison.OrdinalIgnoreCase)) return null;

            string cleanName = procName;
            if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName = cleanName.Substring(0, cleanName.Length - 4).Trim();

            // Method 1: Check running process windows & image paths
            try
            {
                Process[] procs = Process.GetProcessesByName(cleanName);
                if (procs != null && procs.Length > 0)
                {
                    try
                    {
                        foreach (var proc in procs)
                        {
                            // A. Try window icon directly (works even when raw Rust/custom exe has no embedded icon resource!)
                            if (proc.MainWindowHandle != IntPtr.Zero)
                            {
                                Image wndIcon = ExtractIconFromWindow(proc.MainWindowHandle);
                                if (wndIcon != null) return wndIcon;
                            }

                            // B. Query executable file image path
                            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, proc.Id);
                            if (hProc != IntPtr.Zero)
                            {
                                try
                                {
                                    var sb = new StringBuilder(1024);
                                    int size = sb.Capacity;
                                    if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                                    {
                                        string qPath = sb.ToString();
                                        if (File.Exists(qPath))
                                        {
                                            Image exeIcon = ExtractIconFromFile(qPath);
                                            if (exeIcon != null) return exeIcon;
                                        }
                                    }
                                }
                                finally
                                {
                                    CloseHandle(hProc);
                                }
                            }

                            // C. Try MainModule.FileName as fallback
                            try
                            {
                                string mPath = proc.MainModule != null ? proc.MainModule.FileName : null;
                                if (!string.IsNullOrEmpty(mPath) && File.Exists(mPath))
                                {
                                    Image mIcon = ExtractIconFromFile(mPath);
                                    if (mIcon != null) return mIcon;
                                }
                            }
                            catch { }
                        }
                    }
                    finally
                    {
                        for (int i = 0; i < procs.Length; i++)
                        {
                            try { procs[i].Dispose(); } catch { }
                        }
                    }
                }
            }
            catch { }

            // Method 2: Check Known Executable Paths on Disk
            try
            {
                string knownPath = FindKnownExecutablePath(cleanName);
                if (!string.IsNullOrEmpty(knownPath) && File.Exists(knownPath))
                {
                    Image kIcon = ExtractIconFromFile(knownPath);
                    if (kIcon != null) return kIcon;
                }
            }
            catch { }

            return null;
        }

        private static Image ExtractIconFromWindow(IntPtr hWnd)
        {
            try
            {
                IntPtr hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = GetClassLongPtr(hWnd, GCLP_HICONSM);
                if (hIcon == IntPtr.Zero)
                    hIcon = GetClassLongPtr(hWnd, GCLP_HICON);

                if (hIcon != IntPtr.Zero)
                {
                    using (var ico = Icon.FromHandle(hIcon))
                    {
                        var bmp = new Bitmap(18, 18);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawIcon(ico, new Rectangle(0, 0, 18, 18));
                        }
                        return bmp;
                    }
                }
            }
            catch { }
            return null;
        }

        private static Image ExtractIconFromFile(string filePath)
        {
            try
            {
                using (Icon ico = Icon.ExtractAssociatedIcon(filePath))
                {
                    if (ico != null)
                    {
                        var bmp = new Bitmap(18, 18);
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawIcon(ico, new Rectangle(0, 0, 18, 18));
                        }
                        return bmp;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string FindKnownExecutablePath(string procName)
        {
            try
            {
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string roamApp = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string progFiles86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                if (procName.IndexOf("discord", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string discordDir = Path.Combine(localApp, "Discord");
                    if (Directory.Exists(discordDir))
                    {
                        string[] appDirs = Directory.GetDirectories(discordDir, "app-*");
                        if (appDirs.Length > 0)
                        {
                            string exe = Path.Combine(appDirs[appDirs.Length - 1], "Discord.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
                else if (procName.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string edge = Path.Combine(progFiles86, @"Microsoft\Edge\Application\msedge.exe");
                    if (File.Exists(edge)) return edge;
                    edge = Path.Combine(progFiles, @"Microsoft\Edge\Application\msedge.exe");
                    if (File.Exists(edge)) return edge;
                }
                else if (procName.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string chrome = Path.Combine(progFiles, @"Google\Chrome\Application\chrome.exe");
                    if (File.Exists(chrome)) return chrome;
                    chrome = Path.Combine(progFiles86, @"Google\Chrome\Application\chrome.exe");
                    if (File.Exists(chrome)) return chrome;
                }
                else if (procName.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string spot = Path.Combine(roamApp, @"Spotify\Spotify.exe");
                    if (File.Exists(spot)) return spot;
                }
                else if (procName.IndexOf("ytm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         procName.IndexOf("youtube", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string[] candidates = new string[]
                    {
                        Path.Combine(localApp, @"Programs\YouTube Music\YouTube Music.exe"),
                        Path.Combine(roamApp, @"YouTube Music\YouTube Music.exe"),
                        Path.Combine(localApp, @"youtube-music-desktop-app\YouTube Music Desktop App.exe"),
                        Path.Combine(localApp, @"Programs\ytm-desktop\YouTube Music.exe"),
                        Path.Combine(progFiles, @"YouTube Music\YouTube Music.exe"),
                        Path.Combine(progFiles86, @"YouTube Music\YouTube Music.exe")
                    };
                    foreach (var c in candidates)
                    {
                        if (File.Exists(c)) return c;
                    }
                }
                else if (procName.IndexOf("afterfx", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string adobe = Path.Combine(progFiles, "Adobe");
                    if (Directory.Exists(adobe))
                    {
                        string[] aeDirs = Directory.GetDirectories(adobe, "*After Effects*");
                        if (aeDirs.Length > 0)
                        {
                            string exe = Path.Combine(aeDirs[aeDirs.Length - 1], @"Support Files\AfterFX.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
                else if (procName.IndexOf("premiere", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string adobe = Path.Combine(progFiles, "Adobe");
                    if (Directory.Exists(adobe))
                    {
                        string[] prDirs = Directory.GetDirectories(adobe, "*Premiere*");
                        if (prDirs.Length > 0)
                        {
                            string exe = Path.Combine(prDirs[prDirs.Length - 1], "Adobe Premiere Pro.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
                else if (procName.IndexOf("vlc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string vlc = Path.Combine(progFiles, @"VideoLAN\VLC\vlc.exe");
                    if (File.Exists(vlc)) return vlc;
                    vlc = Path.Combine(progFiles86, @"VideoLAN\VLC\vlc.exe");
                    if (File.Exists(vlc)) return vlc;
                }
            }
            catch { }

            return null;
        }

        private static readonly HashSet<string> KnownAppNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "vlc", "spotify", "discord", "msedge", "chrome", "brave", "firefox", "opera",
            "wmplayer", "mpv", "foobar2000", "aimp", "musicbee", "itunes", "applemusic",
            "tidal", "deezer", "winamp", "potplayer", "kmplayer", "gom", "gomp64",
            "telegram", "whatsapp", "zoom", "obs64", "obs", "afterfx", "adobe premiere pro",
            "capcut", "resolve", "vegas", "audition", "blender",
            "ytm-rs", "ytm-tauri", "nabytune-gpui-proto", "youtube music"
        };

        public static bool IsKnownApp(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return false;
            return KnownAppNames.Contains(procName);
        }

        private static Image GenerateFallbackIcon(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "App";

            // 1. YouTube Music / YTM Dedicated Authentic Vector Logo
            if (name.IndexOf("ytm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("youtube", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var ytmBmp = new Bitmap(18, 18);
                using (var g = Graphics.FromImage(ytmBmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    // Vibrant YouTube Music Red Circle
                    using (var b = new SolidBrush(Color.FromArgb(239, 68, 68)))
                    {
                        g.FillEllipse(b, 1, 1, 16, 16);
                    }

                    // White Concentric Ring
                    using (var p = new Pen(Color.White, 1.2f))
                    {
                        g.DrawEllipse(p, 4.2f, 4.2f, 9.6f, 9.6f);
                    }

                    // White Play Triangle
                    using (var b = new SolidBrush(Color.White))
                    {
                        var pts = new PointF[] {
                            new PointF(7.2f, 6.0f),
                            new PointF(12.2f, 9.0f),
                            new PointF(7.2f, 12.0f)
                        };
                        g.FillPolygon(b, pts);
                    }
                }
                return ytmBmp;
            }

            // 2. Spotify Dedicated Authentic Vector Logo
            if (name.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var spotBmp = new Bitmap(18, 18);
                using (var g = Graphics.FromImage(spotBmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    // Spotify Green Circle (#1DB954)
                    using (var b = new SolidBrush(Color.FromArgb(29, 185, 84)))
                    {
                        g.FillEllipse(b, 1, 1, 16, 16);
                    }

                    // 3 Black Curved Arcs
                    using (var p = new Pen(Color.FromArgb(25, 20, 20), 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawArc(p, 4.2f, 4.2f, 9.6f, 9.6f, -65, 45);
                        g.DrawArc(p, 4.8f, 6.2f, 8.4f, 8.4f, -65, 45);
                        g.DrawArc(p, 5.5f, 8.2f, 7.0f, 7.0f, -65, 45);
                    }
                }
                return spotBmp;
            }

            // 3. Elegant Colored Badge with First Letter
            var bmp = new Bitmap(18, 18);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                int hash = Math.Abs(name.GetHashCode());
                Color[] palette = new Color[]
                {
                    Color.FromArgb(99, 102, 241),
                    Color.FromArgb(139, 92, 246),
                    Color.FromArgb(59, 130, 246),
                    Color.FromArgb(16, 185, 129),
                    Color.FromArgb(236, 72, 153),
                    Color.FromArgb(245, 158, 11)
                };
                Color bg = palette[hash % palette.Length];

                using (var b = new SolidBrush(bg))
                {
                    g.FillEllipse(b, 1, 1, 16, 16);
                }

                string letter = name.Length > 0 ? name.Substring(0, 1).ToUpper() : "A";
                using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
                using (var tb = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString(letter, font, tb, new RectangleF(0, 0, 18, 18), sf);
                }
            }
            return bmp;
        }

        public static void PruneCacheIfNeeded()
        {
            if ((DateTime.UtcNow - lastPrune).TotalSeconds > 30)
            {
                ProcCache.Clear();
                ParentPidCache.Clear();
                WebViewHostCache.Clear();
                FallbackIconCache.Clear(); // Allow re-attempting real icon extraction
                lastPrune = DateTime.UtcNow;
            }
        }

        public static string GetWebViewHostExeName(uint pid)
        {
            if (pid == 0) return null;
            string cached;
            if (WebViewHostCache.TryGetValue(pid, out cached)) return cached;

            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (hProc == IntPtr.Zero) return null;
            try
            {
                int retLen;
                int status = NtQueryInformationProcessBuf(hProc, ProcessCommandLineInformation, IntPtr.Zero, 0, out retLen);
                if (retLen > 0)
                {
                    IntPtr buf = Marshal.AllocHGlobal(retLen);
                    try
                    {
                        status = NtQueryInformationProcessBuf(hProc, ProcessCommandLineInformation, buf, retLen, out retLen);
                        if (status == 0)
                        {
                            UNICODE_STRING uStr = (UNICODE_STRING)Marshal.PtrToStructure(buf, typeof(UNICODE_STRING));
                            if (uStr.Length > 0 && uStr.Buffer != IntPtr.Zero)
                            {
                                string cmd = Marshal.PtrToStringUni(uStr.Buffer, uStr.Length / 2);
                                if (!string.IsNullOrEmpty(cmd))
                                {
                                    int idx = cmd.IndexOf("--webview-exe-name=", StringComparison.OrdinalIgnoreCase);
                                    if (idx >= 0)
                                    {
                                        string sub = cmd.Substring(idx + "--webview-exe-name=".Length);
                                        int term = sub.IndexOfAny(new char[] { ' ', '"' });
                                        string exe = term > 0 ? sub.Substring(0, term).Trim() : sub.Trim();
                                        if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                        {
                                            exe = exe.Substring(0, exe.Length - 4);
                                        }
                                        if (!string.IsNullOrEmpty(exe))
                                        {
                                            WebViewHostCache[pid] = exe;
                                            return exe;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buf);
                    }
                }
            }
            catch { }
            finally
            {
                CloseHandle(hProc);
            }
            return null;
        }

        public static string GetHostProcessName(uint pid)
        {
            if (pid == 0) return "";
            string name = GetProcessName(pid);
            if (string.Equals(name, "msedgewebview2", StringComparison.OrdinalIgnoreCase))
            {
                string host = GetWebViewHostExeName(pid);
                if (!string.IsNullOrEmpty(host))
                {
                    return host;
                }

                // If this is a child process of a known host (e.g. utility/audio/renderer spawned by ytm-tauri),
                // check ancestor processes to find the true host application name!
                uint curPid = pid;
                for (int depth = 0; depth < 5; depth++)
                {
                    uint ppid = GetParentProcessId(curPid);
                    if (ppid == 0 || ppid == curPid) break;
                    string pName = GetProcessName(ppid);
                    if (!string.IsNullOrEmpty(pName))
                    {
                        if (string.Equals(pName, "msedgewebview2", StringComparison.OrdinalIgnoreCase))
                        {
                            string pHost = GetWebViewHostExeName(ppid);
                            if (!string.IsNullOrEmpty(pHost))
                            {
                                WebViewHostCache[pid] = pHost;
                                return pHost;
                            }
                        }
                        else
                        {
                            // Found non-webview host executable (e.g. "ytm-tauri" or "FocusGrow")!
                            WebViewHostCache[pid] = pName;
                            return pName;
                        }
                    }
                    curPid = ppid;
                }
            }
            return name;
        }

        public static uint GetParentProcessId(uint pid)
        {
            if (pid == 0) return 0;
            uint parentPid;
            if (ParentPidCache.TryGetValue(pid, out parentPid)) return parentPid;

            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                    int returnLength;
                    int status = NtQueryInformationProcess(hProc, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
                    if (status == 0)
                    {
                        parentPid = (uint)pbi.InheritedFromUniqueProcessId.ToInt64();
                        ParentPidCache[pid] = parentPid;
                        return parentPid;
                    }
                }
                catch { }
                finally
                {
                    CloseHandle(hProc);
                }
            }
            return 0;
        }

        public static bool HasAncestorProcessName(uint pid, string targetName, int maxDepth = 4)
        {
            if (pid == 0 || string.IsNullOrEmpty(targetName)) return false;

            string cleanTarget = targetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? targetName.Substring(0, targetName.Length - 4)
                : targetName;

            bool targetIsEdgeBrowser = string.Equals(cleanTarget, "msedge", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(cleanTarget, "edge", StringComparison.OrdinalIgnoreCase);
            bool targetIsWebview = cleanTarget.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0;

            uint curPid = pid;
            for (int depth = 0; depth < maxDepth; depth++)
            {
                uint ppid = GetParentProcessId(curPid);
                if (ppid == 0 || ppid == curPid) break;
                string pName = GetProcessName(ppid);
                if (!string.IsNullOrEmpty(pName))
                {
                    string cleanParent = pName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? pName.Substring(0, pName.Length - 4)
                        : pName;

                    bool parentIsWebview = cleanParent.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0;

                    // Edge browser must NEVER match webview runtime ancestors!
                    if (targetIsEdgeBrowser && parentIsWebview)
                    {
                        curPid = ppid;
                        continue;
                    }

                    // Non-webview targets should not match generic webview ancestor
                    if (!targetIsWebview && parentIsWebview)
                    {
                        curPid = ppid;
                        continue;
                    }

                    if (string.Equals(cleanParent, cleanTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                curPid = ppid;
            }
            return false;
        }

        public static bool IsProcessMatch(string sessionProcName, string searchName, uint sessionPid = 0)
        {
            if (string.IsNullOrEmpty(sessionProcName) || string.IsNullOrEmpty(searchName)) return false;

            string proc = sessionProcName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? sessionProcName.Substring(0, sessionProcName.Length - 4)
                : sessionProcName;

            string search = searchName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? searchName.Substring(0, searchName.Length - 4)
                : searchName;

            bool procIsWebview = proc.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0;
            bool searchIsWebview = search.IndexOf("webview", StringComparison.OrdinalIgnoreCase) >= 0;
            bool searchIsEdge = string.Equals(search, "msedge", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(search, "edge", StringComparison.OrdinalIgnoreCase);

            // 0. Strict Browser vs WebView Isolation:
            // A webview process must NEVER match standard browser names (msedge, chrome, etc.)
            if (procIsWebview && !searchIsWebview)
            {
                if (searchIsEdge || string.Equals(search, "chrome", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!procIsWebview && searchIsWebview)
            {
                return false;
            }

            // 1. Native WebView2 Host Application Match (e.g. searchName is "ytm-tauri" or "FocusGrow")
            if (sessionPid > 0)
            {
                string webViewHost = GetHostProcessName(sessionPid);
                if (!string.IsNullOrEmpty(webViewHost) && !string.Equals(webViewHost, "msedgewebview2", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(webViewHost, search, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (searchIsWebview || searchIsEdge)
                    {
                        return false;
                    }
                }
            }

            // 2. Exact Match (e.g. "msedge" == "msedge", "chrome" == "chrome", "ytm-tauri" == "ytm-tauri")
            if (string.Equals(proc, search, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Ancestor matching (e.g. audio stream is spawned by Tauri app "ytm-tauri")
            if (sessionPid > 0 && HasAncestorProcessName(sessionPid, search))
            {
                return true;
            }

            // 4. Strict browser matching: "msedge" or "edge" must strictly be "msedge"
            if (searchIsEdge)
            {
                return string.Equals(proc, "msedge", StringComparison.OrdinalIgnoreCase);
            }

            // Strict browser matching: "chrome" must strictly be "chrome"
            if (string.Equals(search, "chrome", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(proc, "chrome", StringComparison.OrdinalIgnoreCase);
            }

            // 5. Fallback substring match for other applications
            return proc.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static HashSet<string> GetActiveAudioProcessNames(IAudioSessionManager2 mgr)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mgr == null) return result;

            try
            {
                IAudioSessionEnumerator sessionEnum;
                mgr.GetSessionEnumerator(out sessionEnum);
                int count;
                sessionEnum.GetCount(out count);

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl ctl;
                    sessionEnum.GetSession(i, out ctl);
                    var ctl2 = ctl as IAudioSessionControl2;
                    if (ctl2 != null)
                    {
                        uint pid;
                        ctl2.GetProcessId(out pid);
                        if (pid > 0)
                        {
                            string name = GetHostProcessName(pid);
                            if (!string.IsNullOrEmpty(name) && !name.Equals("audiodg", StringComparison.OrdinalIgnoreCase))
                            {
                                result.Add(name);
                            }
                        }
                    }
                    if (ctl != null) try { Marshal.ReleaseComObject(ctl); } catch { }
                }
                if (sessionEnum != null) try { Marshal.ReleaseComObject(sessionEnum); } catch { }
            }
            catch { }

            return result;
        }

        public static HashSet<string> GetActiveAudioProcessNames(IMMDeviceEnumerator enumerator)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (enumerator == null) return result;

            try
            {
                IMMDeviceCollection devs;
                if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out devs) == 0 && devs != null)
                {
                    int devCount;
                    devs.GetCount(out devCount);
                    Guid IID_IAudioSessionManager2 = typeof(IAudioSessionManager2).GUID;

                    for (int d = 0; d < devCount; d++)
                    {
                        IMMDevice dev = null;
                        IAudioSessionManager2 mgr = null;
                        try
                        {
                            if (devs.Item(d, out dev) == 0 && dev != null)
                            {
                                object o;
                                if (dev.Activate(ref IID_IAudioSessionManager2, 0, IntPtr.Zero, out o) == 0 && o != null)
                                {
                                    mgr = o as IAudioSessionManager2;
                                    if (mgr != null)
                                    {
                                        var apps = GetActiveAudioProcessNames(mgr);
                                        foreach (var app in apps) result.Add(app);
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            if (mgr != null) try { Marshal.ReleaseComObject(mgr); } catch { }
                            if (dev != null) try { Marshal.ReleaseComObject(dev); } catch { }
                        }
                    }
                    try { Marshal.ReleaseComObject(devs); } catch { }
                }
            }
            catch { }

            return result;
        }
    }
}
