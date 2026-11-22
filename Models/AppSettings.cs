using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FocusFade.Models
{
    public class AppSettings
    {
        public List<string> TriggerApps { get; set; }
        public List<string> TargetApps { get; set; }
        public string TargetApp
        {
            get { return (TargetApps != null && TargetApps.Count > 0) ? TargetApps[0] : ""; }
            set
            {
                if (TargetApps == null) TargetApps = new List<string>();
                if (!string.IsNullOrEmpty(value) && !TargetApps.Contains(value))
                {
                    TargetApps.Insert(0, value);
                }
            }
        }
        public int DuckVolumePercent { get; set; }
        public int ReleaseDelayMs { get; set; }
        public int SensitivityPercent { get; set; }
        public bool SmoothFade { get; set; }
        public bool MinimizeToTray { get; set; }
        public int PipX { get; set; }
        public int PipY { get; set; }
        public bool PipIsVertical { get; set; }

        public bool StartWithWindows { get; set; }
        public bool StartMinimized { get; set; }
        public bool AutoStartDucking { get; set; }
        public bool DuckOnMicrophone { get; set; }
        public Dictionary<string, int> AppSensitivities { get; set; }

        public int GetAppSensitivity(string appName, int defaultPercent = 20)
        {
            if (string.IsNullOrEmpty(appName)) return defaultPercent;
            int val;
            if (AppSensitivities != null && AppSensitivities.TryGetValue(appName, out val)) return val;
            return defaultPercent;
        }

        public void SetAppSensitivity(string appName, int percent)
        {
            if (string.IsNullOrEmpty(appName)) return;
            if (AppSensitivities == null) AppSensitivities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            AppSensitivities[appName] = Math.Max(1, Math.Min(90, percent));
        }

        public AppSettings()
        {
            TriggerApps = new List<string>();
            TargetApps = new List<string>();
            DuckVolumePercent = 15;
            ReleaseDelayMs = 700;
            SensitivityPercent = 20;
            SmoothFade = true;
            MinimizeToTray = false; // By default [X] exits completely unless user checks this!
            PipX = -1;
            PipY = -1;
            PipIsVertical = false;

            StartWithWindows = false;
            StartMinimized = false;
            AutoStartDucking = true; // Auto-activate dimmer on launch
            DuckOnMicrophone = false;
            AppSensitivities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private static string GetConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focusfade_settings.ini");
        }

        public void Save()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("Triggers=" + string.Join(";", TriggerApps.ToArray()));
                lines.Add("Targets=" + string.Join(";", TargetApps.ToArray()));
                lines.Add("Target=" + TargetApp);
                lines.Add("DuckVolume=" + DuckVolumePercent);
                lines.Add("ReleaseDelay=" + ReleaseDelayMs);
                lines.Add("Sensitivity=" + SensitivityPercent);
                lines.Add("SmoothFade=" + SmoothFade);
                lines.Add("MinimizeToTray=" + MinimizeToTray);
                lines.Add("PipX=" + PipX);
                lines.Add("PipY=" + PipY);
                lines.Add("PipIsVertical=" + PipIsVertical);
                lines.Add("StartWithWindows=" + StartWithWindows);
                lines.Add("StartMinimized=" + StartMinimized);
                lines.Add("AutoStartDucking=" + AutoStartDucking);
                lines.Add("DuckOnMicrophone=" + DuckOnMicrophone);

                if (AppSensitivities != null && AppSensitivities.Count > 0)
                {
                    var sensList = new List<string>();
                    foreach (var kv in AppSensitivities)
                    {
                        sensList.Add(kv.Key + ":" + kv.Value);
                    }
                    lines.Add("AppSensitivities=" + string.Join(";", sensList.ToArray()));
                }

                File.WriteAllLines(GetConfigPath(), lines.ToArray());

                // Sync Windows Startup Registry
                SetStartupRegistry(StartWithWindows);
            }
            catch { }
        }

        public static void SetStartupRegistry(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exe = Application.ExecutablePath;
                            key.SetValue("FocusFade", "\"" + exe + "\" --minimized");
                        }
                        else
                        {
                            key.DeleteValue("FocusFade", false);
                        }
                    }
                }
            }
            catch { }
        }

        public static bool IsStartupRegistryEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        return key.GetValue("FocusFade") != null;
                    }
                }
            }
            catch { }
            return false;
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            string path = GetConfigPath();
            if (!File.Exists(path))
            {
                s.TriggerApps.Add("AfterFX");
                s.TriggerApps.Add("Discord");
                return s;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("Triggers", StringComparison.OrdinalIgnoreCase))
                    {
                        s.TriggerApps.Clear();
                        string[] parts = val.Split(';');
                        foreach (var p in parts)
                        {
                            string item = p.Trim();
                            if (!string.IsNullOrEmpty(item) && !s.TriggerApps.Contains(item))
                            {
                                s.TriggerApps.Add(item);
                            }
                        }
                    }
                    else if (key.Equals("Trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val) && !s.TriggerApps.Contains(val))
                        {
                            s.TriggerApps.Add(val);
                        }
                    }
                    else if (key.Equals("Targets", StringComparison.OrdinalIgnoreCase))
                    {
                        s.TargetApps.Clear();
                        string[] parts = val.Split(';');
                        foreach (var p in parts)
                        {
                            string item = p.Trim();
                            if (!string.IsNullOrEmpty(item) && !s.TargetApps.Contains(item))
                            {
                                s.TargetApps.Add(item);
                            }
                        }
                    }
                    else if (key.Equals("Target", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(val) && !s.TargetApps.Contains(val))
                        {
                            s.TargetApps.Add(val);
                        }
                    }
                    else if (key.Equals("DuckVolume", StringComparison.OrdinalIgnoreCase))
                    {
                        int dv;
                        if (int.TryParse(val, out dv)) s.DuckVolumePercent = Math.Max(0, Math.Min(60, dv));
                    }
                    else if (key.Equals("ReleaseDelay", StringComparison.OrdinalIgnoreCase))
                    {
                        int rd;
                        if (int.TryParse(val, out rd)) s.ReleaseDelayMs = Math.Max(200, Math.Min(2000, rd));
                    }
                    else if (key.Equals("Sensitivity", StringComparison.OrdinalIgnoreCase))
                    {
                        int sens;
                        if (int.TryParse(val, out sens)) s.SensitivityPercent = Math.Max(1, Math.Min(60, sens));
                    }
                    else if (key.Equals("SmoothFade", StringComparison.OrdinalIgnoreCase))
                    {
                        bool sf;
                        if (bool.TryParse(val, out sf)) s.SmoothFade = sf;
                    }
                    else if (key.Equals("MinimizeToTray", StringComparison.OrdinalIgnoreCase))
                    {
                        bool mt;
                        if (bool.TryParse(val, out mt)) s.MinimizeToTray = mt;
                    }
                    else if (key.Equals("PipX", StringComparison.OrdinalIgnoreCase))
                    {
                        int px;
                        if (int.TryParse(val, out px)) s.PipX = px;
                    }
                    else if (key.Equals("PipY", StringComparison.OrdinalIgnoreCase))
                    {
                        int py;
                        if (int.TryParse(val, out py)) s.PipY = py;
                    }
                    else if (key.Equals("PipIsVertical", StringComparison.OrdinalIgnoreCase))
                    {
                        bool pv;
                        if (bool.TryParse(val, out pv)) s.PipIsVertical = pv;
                    }
                    else if (key.Equals("StartWithWindows", StringComparison.OrdinalIgnoreCase))
                    {
                        bool sw;
                        if (bool.TryParse(val, out sw)) s.StartWithWindows = sw;
                    }
                    else if (key.Equals("StartMinimized", StringComparison.OrdinalIgnoreCase))
                    {
                        bool sm;
                        if (bool.TryParse(val, out sm)) s.StartMinimized = sm;
                    }
                    else if (key.Equals("AutoStartDucking", StringComparison.OrdinalIgnoreCase))
                    {
                        bool asd;
                        if (bool.TryParse(val, out asd)) s.AutoStartDucking = asd;
                    }
                    else if (key.Equals("DuckOnMicrophone", StringComparison.OrdinalIgnoreCase))
                    {
                        bool dm;
                        if (bool.TryParse(val, out dm)) s.DuckOnMicrophone = dm;
                    }
                    else if (key.Equals("AppSensitivities", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = val.Split(';');
                        foreach (var p in parts)
                        {
                            int colon = p.IndexOf(':');
                            if (colon > 0)
                            {
                                string aName = p.Substring(0, colon).Trim();
                                int aVal;
                                if (int.TryParse(p.Substring(colon + 1).Trim(), out aVal))
                                {
                                    s.AppSensitivities[aName] = aVal;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (s.TriggerApps.Count == 0)
            {
                s.TriggerApps.Add("AfterFX");
                s.TriggerApps.Add("Discord");
            }

            if (s.TargetApps.Count == 0)
            {
                s.TargetApps.Add("ytm-tauri");
            }

            return s;
        }
    }
}
