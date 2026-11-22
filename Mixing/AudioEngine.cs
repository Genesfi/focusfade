using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using FocusFade.Core;

namespace FocusFade.Mixing
{
    public class AudioEngine
    {
        private Thread workerThread;
        private volatile bool isRunning = false;
        private volatile bool isStopping = false;

        private string[] triggerList = new string[0];
        private readonly object triggerLock = new object();

        private string[] targetList = new string[0];
        private readonly object targetLock = new object();
        private volatile bool requestSessionScan = false;

        private readonly Dictionary<string, float> appThresholds = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> latestAppPeaks = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly object thresholdLock = new object();

        public string TargetProcess
        {
            get
            {
                lock (targetLock)
                {
                    return targetList.Length > 0 ? targetList[0] : "";
                }
            }
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                // Safe: Never wipe out other active targets if TargetProcess is assigned
                AddTargetProcess(value);
            }
        }

        public string[] GetTargetProcesses()
        {
            lock (targetLock)
            {
                var arr = new string[targetList.Length];
                Array.Copy(targetList, arr, targetList.Length);
                return arr;
            }
        }

        public void SetTargetProcesses(IEnumerable<string> targets)
        {
            lock (targetLock)
            {
                var list = new List<string>();
                if (targets != null)
                {
                    foreach (var t in targets)
                    {
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            string item = t.Trim();
                            if (item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                item = item.Substring(0, item.Length - 4).Trim();
                            if (!list.Contains(item)) list.Add(item);
                        }
                    }
                }
                targetList = list.ToArray();
            }
            TriggerSessionScan();
        }

        public void AddTargetProcess(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            string item = target.Trim();
            if (item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                item = item.Substring(0, item.Length - 4).Trim();

            lock (targetLock)
            {
                var list = new List<string>(targetList);
                if (!list.Contains(item))
                {
                    list.Add(item);
                    targetList = list.ToArray();
                }
            }
            TriggerSessionScan();
        }

        public void RemoveTargetProcess(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            string item = target.Trim();
            if (item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                item = item.Substring(0, item.Length - 4).Trim();

            lock (targetLock)
            {
                var list = new List<string>(targetList);
                if (list.Remove(item))
                {
                    targetList = list.ToArray();
                }
            }
            TriggerSessionScan();
        }

        public void TriggerSessionScan()
        {
            requestSessionScan = true;
        }

        public volatile float DuckVolume;
        public volatile int ReleaseDelayMs;
        public volatile float TriggerThreshold;
        public volatile bool SmoothFade;
        public volatile bool MonitorMicrophone = false;

        // Preview State
        public volatile bool IsPreviewing = false;
        public volatile float PreviewVolume = 0.15f;
        private DateTime previewEndTime = DateTime.MinValue;

        // Live Telemetry
        public volatile float TriggerPeak;
        public volatile float TargetPeak;
        public volatile float CurrentTargetVolume;
        public volatile bool IsDucked;
        public volatile string ActiveTriggerName;

        public bool IsActive
        {
            get { return isRunning; }
        }

        public AudioEngine()
        {
            DuckVolume = 0.15f;
            ReleaseDelayMs = 700;
            TriggerThreshold = 0.20f; // 20% default
            SmoothFade = true;
            TriggerPeak = 0.0f;
            TargetPeak = 0.0f;
            CurrentTargetVolume = 1.0f;
            IsDucked = false;
            ActiveTriggerName = "";
        }

        public void SetAppThreshold(string appName, float threshold)
        {
            if (string.IsNullOrEmpty(appName)) return;
            lock (thresholdLock)
            {
                appThresholds[appName] = Math.Max(0.01f, Math.Min(0.95f, threshold));
            }
        }

        public float GetAppThreshold(string appName, float defaultThreshold = 0.20f)
        {
            if (string.IsNullOrEmpty(appName)) return defaultThreshold;
            lock (thresholdLock)
            {
                float val;
                if (appThresholds.TryGetValue(appName, out val)) return val;
            }
            return defaultThreshold;
        }

        public float GetAppPeak(string appName)
        {
            if (string.IsNullOrEmpty(appName)) return TriggerPeak;
            lock (thresholdLock)
            {
                float p;
                if (latestAppPeaks.TryGetValue(appName, out p)) return p;
            }
            return 0.0f;
        }

        public void TriggerPreview(float volume, int durationMs = 3500)
        {
            PreviewVolume = volume;
            previewEndTime = durationMs > 0 ? DateTime.UtcNow.AddMilliseconds(durationMs) : DateTime.MaxValue;
            IsPreviewing = true;
        }

        public void StopPreview()
        {
            IsPreviewing = false;
            previewEndTime = DateTime.MinValue;
        }

        public void SetTriggerProcesses(IEnumerable<string> triggers)
        {
            lock (triggerLock)
            {
                var list = new List<string>();
                if (triggers != null)
                {
                    foreach (var t in triggers)
                    {
                        if (!string.IsNullOrEmpty(t)) list.Add(t.Trim());
                    }
                }
                triggerList = list.ToArray();
            }
        }

        public string[] GetTriggerProcesses()
        {
            lock (triggerLock)
            {
                return (string[])triggerList.Clone();
            }
        }

        public void Start()
        {
            isRunning = true;
        }

        public void Stop()
        {
            isRunning = false;
        }

        public void Initialize()
        {
            workerThread = new Thread(Loop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest, // Keeps CPU minimal
                Name = "FocusFadeAudioLoop"
            };
            workerThread.Start();
        }

        public void Shutdown()
        {
            isStopping = true;
            isRunning = false;
            IsPreviewing = false;
        }

        private class TargetSessionInfo
        {
            public ISimpleAudioVolume Volume;
            public IAudioMeterInformation Meter;
            public string ProcessName;
            public uint ProcessId;
            public string SessionKey;
            public bool IsDefaultDevice;
            public float OriginalVolume = 1.0f;
            public float CurrentVolume = 1.0f;
            public float LastAppliedVolume = -1.0f;
        }

        private struct TriggerMeterInfo
        {
            public IAudioMeterInformation Meter;
            public IAudioSessionControl2 SessionControl;
            public string ProcessName;
        }

        private void Loop()
        {
            IMMDeviceEnumerator enumerator = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
            }
            catch { }

            DateTime lastAudioTime = DateTime.MinValue;
            DateTime lastSessionScan = DateTime.MinValue;
            DateTime lastDeviceCheck = DateTime.MinValue;
            bool wasActive = false;
            string lastTargetsSignature = "";
            string lastDefaultRenderDevId = "";
            string lastDefaultCaptureDevId = "";
            bool needsSessionScan = true;

            // Cached session COM pointers & remembered levels
            var cachedTriggerMeters = new List<TriggerMeterInfo>();
            var cachedTargetSessions = new List<TargetSessionInfo>();
            var rememberedOriginalVolumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var cachedMicMeters = new List<IAudioMeterInformation>();
            var cachedMicClients = new List<IAudioClient>();

            while (!isStopping)
            {
                try
                {
                    if (enumerator == null)
                    {
                        try
                        {
                            enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                        }
                        catch
                        {
                            Thread.Sleep(100);
                            continue;
                        }
                    }

                    // 1. Detect Default Output / Input Device changes (User plugged/switched headset or speakers)
                    if ((DateTime.UtcNow - lastDeviceCheck).TotalMilliseconds > 80)
                    {
                        lastDeviceCheck = DateTime.UtcNow;
                        string curDefaultRenderId = GetDefaultAudioDeviceId(enumerator, EDataFlow.eRender);
                        if (!string.IsNullOrEmpty(curDefaultRenderId) && !string.Equals(curDefaultRenderId, lastDefaultRenderDevId, StringComparison.OrdinalIgnoreCase))
                        {
                            lastDefaultRenderDevId = curDefaultRenderId;
                            needsSessionScan = true;
                        }

                        string curDefaultCaptureId = GetDefaultAudioDeviceId(enumerator, EDataFlow.eCapture);
                        if (!string.IsNullOrEmpty(curDefaultCaptureId) && !string.Equals(curDefaultCaptureId, lastDefaultCaptureDevId, StringComparison.OrdinalIgnoreCase))
                        {
                            lastDefaultCaptureDevId = curDefaultCaptureId;
                            ClearMicCaches(cachedMicClients, cachedMicMeters);
                        }
                    }

                    if (requestSessionScan)
                    {
                        requestSessionScan = false;
                        needsSessionScan = true;
                    }

                    string[] activeTriggers = GetTriggerProcesses();
                    string[] activeTargets = GetTargetProcesses();
                    bool active = isRunning || IsPreviewing;

                    if (!active)
                    {
                        if (wasActive)
                        {
                            RestoreAllTargetVolumes(enumerator, cachedTargetSessions, activeTargets, rememberedOriginalVolumes);
                            wasActive = false;
                            IsDucked = false;
                            ActiveTriggerName = "";
                        }
                        TriggerPeak = 0.0f;
                        TargetPeak = 0.0f;
                        Thread.Sleep(60);
                        continue;
                    }

                    wasActive = true;

                    // Check if Target Process list was switched or modified
                    string curTargetsSignature = string.Join(";", activeTargets);
                    if (!string.Equals(curTargetsSignature, lastTargetsSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        lastTargetsSignature = curTargetsSignature;
                        needsSessionScan = true;
                    }

                    // Check if microphone is requested via setting or triggers
                    bool hasMicInTriggers = false;
                    for (int mIdx = 0; mIdx < activeTriggers.Length; mIdx++)
                    {
                        if (activeTriggers[mIdx].IndexOf("Mic", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasMicInTriggers = true;
                            break;
                        }
                    }
                    bool shouldMonitorMic = MonitorMicrophone || hasMicInTriggers;

                    // Enumerate active capture endpoints (VoiceMeeter Output, USB Focusrite, etc.)
                    if (shouldMonitorMic && cachedMicMeters.Count == 0)
                    {
                        ScanMicrophoneDevices(enumerator, cachedMicClients, cachedMicMeters);
                    }
                    else if (!shouldMonitorMic && cachedMicMeters.Count > 0)
                    {
                        ClearMicCaches(cachedMicClients, cachedMicMeters);
                    }

                    // Responsive scan: 250ms interval ensures newly started audio streams are captured immediately without perceptible delay
                    int scanInterval = 250;
                    if (needsSessionScan || (DateTime.UtcNow - lastSessionScan).TotalMilliseconds > scanInterval)
                    {
                        needsSessionScan = false;
                        lastSessionScan = DateTime.UtcNow;
                        ProcessHelper.PruneCacheIfNeeded();

                        if (string.IsNullOrEmpty(lastDefaultRenderDevId))
                        {
                            lastDefaultRenderDevId = GetDefaultAudioDeviceId(enumerator, EDataFlow.eRender);
                        }

                        ScanAllActiveRenderSessions(enumerator, lastDefaultRenderDevId, activeTriggers, activeTargets, cachedTriggerMeters, cachedTargetSessions, rememberedOriginalVolumes, IsDucked || IsPreviewing);
                    }

                    // 1. Measure Priority Apps Peak (Digital Audio) with Per-App Thresholds
                    float maxAppPeak = 0.0f;
                    string loudAppName = "";
                    bool anyAppTriggered = false;

                    for (int i = 0; i < cachedTriggerMeters.Count; i++)
                    {
                        try
                        {
                            // Inactive / paused sessions must never trigger ducking!
                            if (cachedTriggerMeters[i].SessionControl != null)
                            {
                                AudioSessionState sState;
                                if (cachedTriggerMeters[i].SessionControl.GetState(out sState) == 0 &&
                                    sState != AudioSessionState.AudioSessionStateActive)
                                {
                                    string inactiveName = cachedTriggerMeters[i].ProcessName;
                                    lock (thresholdLock)
                                    {
                                        latestAppPeaks[inactiveName] = 0.0f;
                                    }
                                    continue;
                                }
                            }

                            float peak;
                            if (cachedTriggerMeters[i].Meter.GetPeakValue(out peak) == 0)
                            {
                                string pName = cachedTriggerMeters[i].ProcessName;

                                lock (thresholdLock)
                                {
                                    latestAppPeaks[pName] = peak;
                                }

                                if (peak > maxAppPeak) maxAppPeak = peak;

                                float thresh = GetAppThreshold(pName, TriggerThreshold);
                                if (peak >= thresh)
                                {
                                    anyAppTriggered = true;
                                    if (string.IsNullOrEmpty(loudAppName) || peak > maxAppPeak)
                                    {
                                        loudAppName = pName;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            needsSessionScan = true;
                        }
                    }

                    // 2. Measure Microphone Voice Peak (Analog Acoustic Input via VoiceMeeter/USB)
                    float rawMicPeak = 0.0f;
                    float micMeterLevel = 0.0f;

                    if (shouldMonitorMic && cachedMicMeters.Count > 0)
                    {
                        bool anyMicFailed = false;
                        for (int m = 0; m < cachedMicMeters.Count; m++)
                        {
                            try
                            {
                                float p;
                                if (cachedMicMeters[m].GetPeakValue(out p) == 0)
                                {
                                    if (p > rawMicPeak) rawMicPeak = p;
                                }
                                else
                                {
                                    anyMicFailed = true;
                                }
                            }
                            catch
                            {
                                anyMicFailed = true;
                            }
                        }

                        if (anyMicFailed)
                        {
                            ClearMicCaches(cachedMicClients, cachedMicMeters);
                        }

                        if (rawMicPeak > 0.008f)
                        {
                            micMeterLevel = Math.Min(1.0f, (rawMicPeak - 0.008f) * 2.2f);
                        }
                    }

                    lock (thresholdLock)
                    {
                        latestAppPeaks["Microphone"] = micMeterLevel;
                    }

                    float micGate = GetAppThreshold("Microphone", TriggerThreshold);
                    bool voiceIsActive = shouldMonitorMic && (micMeterLevel >= micGate);

                    // Target Peak (Music output level across all target apps)
                    float maxTargPeak = 0.0f;
                    for (int i = 0; i < cachedTargetSessions.Count; i++)
                    {
                        try
                        {
                            float peak;
                            if (cachedTargetSessions[i].Meter.GetPeakValue(out peak) == 0)
                            {
                                if (peak > maxTargPeak) maxTargPeak = peak;
                            }
                        }
                        catch
                        {
                            needsSessionScan = true;
                        }
                    }

                    // Telemetry for UI meters
                    TriggerPeak = Math.Max(micMeterLevel, maxAppPeak);
                    TargetPeak = maxTargPeak;

                    // Ducking Evaluation
                    bool shouldDuck = false;

                    if (IsPreviewing)
                    {
                        if (previewEndTime != DateTime.MinValue && DateTime.UtcNow > previewEndTime)
                        {
                            IsPreviewing = false;
                            shouldDuck = false;
                        }
                        else
                        {
                            shouldDuck = true;
                            ActiveTriggerName = "Volume Preview";
                        }
                    }
                    else if (isRunning)
                    {
                        if (voiceIsActive)
                        {
                            lastAudioTime = DateTime.UtcNow;
                            shouldDuck = true;
                            ActiveTriggerName = "Microphone";
                        }
                        else if (anyAppTriggered)
                        {
                            lastAudioTime = DateTime.UtcNow;
                            shouldDuck = true;
                            ActiveTriggerName = !string.IsNullOrEmpty(loudAppName) ? loudAppName : "Priority App";
                        }
                        else
                        {
                            if ((DateTime.UtcNow - lastAudioTime).TotalMilliseconds <= ReleaseDelayMs)
                            {
                                shouldDuck = true;
                            }
                            else
                            {
                                ActiveTriggerName = "";
                            }
                        }
                    }

                    IsDucked = shouldDuck;

                    // Smooth Fade Ease & Volume Application per target session
                    float targetDisplayVol = 1.0f;
                    bool hasTargetDisplayVol = false;
                    Guid empty = Guid.Empty;

                    for (int v = 0; v < cachedTargetSessions.Count; v++)
                    {
                        var sInfo = cachedTargetSessions[v];
                        try
                        {
                            float actualMixerVol;
                            if (sInfo.Volume.GetMasterVolume(out actualMixerVol) == 0)
                            {
                                if (sInfo.LastAppliedVolume < 0.0f)
                                {
                                    float initVol = (sInfo.OriginalVolume > 0.20f) ? sInfo.OriginalVolume : (actualMixerVol > 0.20f ? actualMixerVol : 1.0f);
                                    sInfo.OriginalVolume = initVol;
                                    sInfo.CurrentVolume = actualMixerVol;
                                    sInfo.LastAppliedVolume = actualMixerVol;
                                    if (!string.IsNullOrEmpty(sInfo.SessionKey))
                                    {
                                        rememberedOriginalVolumes[sInfo.SessionKey] = initVol;
                                    }
                                    if (!string.IsNullOrEmpty(sInfo.ProcessName))
                                    {
                                        rememberedOriginalVolumes[sInfo.ProcessName] = initVol;
                                    }
                                }
                                else
                                {
                                    // ONLY register a manual volume adjustment if the engine is sitting at rest (not ducked, not previewing, and finished fading!)
                                    bool isAtRest = !shouldDuck && !IsPreviewing && Math.Abs(sInfo.CurrentVolume - sInfo.OriginalVolume) < 0.02f;
                                    if (isAtRest && Math.Abs(actualMixerVol - sInfo.LastAppliedVolume) > 0.04f)
                                    {
                                        if (actualMixerVol > 0.20f)
                                        {
                                            sInfo.OriginalVolume = actualMixerVol;
                                            sInfo.CurrentVolume = actualMixerVol;
                                            sInfo.LastAppliedVolume = actualMixerVol;
                                            if (!string.IsNullOrEmpty(sInfo.SessionKey))
                                            {
                                                rememberedOriginalVolumes[sInfo.SessionKey] = actualMixerVol;
                                            }
                                            if (!string.IsNullOrEmpty(sInfo.ProcessName))
                                            {
                                                rememberedOriginalVolumes[sInfo.ProcessName] = actualMixerVol;
                                            }
                                        }
                                    }
                                }
                            }

                            float targetDuckVol = IsPreviewing ? PreviewVolume : DuckVolume;
                            float desiredVol = shouldDuck ? Math.Min(targetDuckVol, sInfo.OriginalVolume) : sInfo.OriginalVolume;

                            if (SmoothFade)
                            {
                                float step = shouldDuck ? 0.22f : 0.08f;
                                if (Math.Abs(sInfo.CurrentVolume - desiredVol) > 0.01f)
                                {
                                    if (sInfo.CurrentVolume < desiredVol) sInfo.CurrentVolume = Math.Min(desiredVol, sInfo.CurrentVolume + step);
                                    else sInfo.CurrentVolume = Math.Max(desiredVol, sInfo.CurrentVolume - step);
                                }
                                else
                                {
                                    sInfo.CurrentVolume = desiredVol;
                                }
                            }
                            else
                            {
                                sInfo.CurrentVolume = desiredVol;
                            }

                            if (sInfo.IsDefaultDevice || !hasTargetDisplayVol)
                            {
                                targetDisplayVol = sInfo.CurrentVolume;
                                hasTargetDisplayVol = true;
                            }

                            if (Math.Abs(sInfo.CurrentVolume - sInfo.LastAppliedVolume) > 0.002f)
                            {
                                sInfo.Volume.SetMasterVolume(sInfo.CurrentVolume, ref empty);
                                sInfo.LastAppliedVolume = sInfo.CurrentVolume;
                            }
                        }
                        catch
                        {
                            needsSessionScan = true;
                        }
                    }

                    CurrentTargetVolume = hasTargetDisplayVol ? targetDisplayVol : (shouldDuck ? (IsPreviewing ? PreviewVolume : DuckVolume) : 1.0f);
                }
                catch
                {
                    needsSessionScan = true;
                }

                Thread.Sleep(40);
            }

            try
            {
                string[] finalTargets = GetTargetProcesses();
                RestoreAllTargetVolumes(enumerator, cachedTargetSessions, finalTargets, rememberedOriginalVolumes);
                ClearMicCaches(cachedMicClients, cachedMicMeters);
                for (int m = 0; m < cachedTriggerMeters.Count; m++)
                {
                    try { Marshal.ReleaseComObject(cachedTriggerMeters[m].Meter); } catch { }
                    if (cachedTriggerMeters[m].SessionControl != null)
                    {
                        try { Marshal.ReleaseComObject(cachedTriggerMeters[m].SessionControl); } catch { }
                    }
                }
                cachedTriggerMeters.Clear();
                for (int v = 0; v < cachedTargetSessions.Count; v++)
                {
                    try { Marshal.ReleaseComObject(cachedTargetSessions[v].Volume); } catch { }
                    try { Marshal.ReleaseComObject(cachedTargetSessions[v].Meter); } catch { }
                }
                cachedTargetSessions.Clear();
                if (enumerator != null) try { Marshal.ReleaseComObject(enumerator); } catch { }
            }
            catch { }
        }

        private static string GetDefaultAudioDeviceId(IMMDeviceEnumerator enumerator, EDataFlow flow)
        {
            if (enumerator == null) return null;
            IMMDevice dev = null;
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(flow, ERole.eMultimedia, out dev) == 0 && dev != null)
                {
                    string id;
                    if (dev.GetId(out id) == 0) return id;
                }
            }
            catch { }
            finally
            {
                if (dev != null) try { Marshal.ReleaseComObject(dev); } catch { }
            }
            return null;
        }

        private static void ClearMicCaches(List<IAudioClient> clients, List<IAudioMeterInformation> meters)
        {
            if (clients != null)
            {
                for (int c = 0; c < clients.Count; c++)
                {
                    try { clients[c].Stop(); Marshal.ReleaseComObject(clients[c]); } catch { }
                }
                clients.Clear();
            }

            if (meters != null)
            {
                for (int m = 0; m < meters.Count; m++)
                {
                    try { Marshal.ReleaseComObject(meters[m]); } catch { }
                }
                meters.Clear();
            }
        }

        private void ScanMicrophoneDevices(IMMDeviceEnumerator enumerator, List<IAudioClient> cachedMicClients, List<IAudioMeterInformation> cachedMicMeters)
        {
            if (enumerator == null) return;
            try
            {
                IMMDeviceCollection captureDevs;
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eCapture, 1 /* DEVICE_STATE_ACTIVE */, out captureDevs);
                if (hr == 0 && captureDevs != null)
                {
                    int devCount;
                    captureDevs.GetCount(out devCount);
                    Guid IID_IAudioMeterInfo = typeof(IAudioMeterInformation).GUID;
                    Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

                    for (int d = 0; d < devCount; d++)
                    {
                        IMMDevice micDev = null;
                        try
                        {
                            if (captureDevs.Item(d, out micDev) == 0 && micDev != null)
                            {
                                string devId = "";
                                try { micDev.GetId(out devId); } catch { }

                                string devName = "";
                                try
                                {
                                    IPropertyStore store;
                                    if (micDev.OpenPropertyStore(0 /* STGM_READ */, out store) == 0 && store != null)
                                    {
                                        var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
                                        PropVariant pv;
                                        if (store.GetValue(ref key, out pv) == 0 && pv.pszVal != IntPtr.Zero)
                                        {
                                            devName = Marshal.PtrToStringUni(pv.pszVal) ?? "";
                                        }
                                        Marshal.ReleaseComObject(store);
                                    }
                                }
                                catch { }

                                bool isBluetooth = (devId != null && devId.IndexOf("BTH", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                   (devName != null && (
                                                       devName.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                       devName.IndexOf("Hands-Free", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                       devName.IndexOf("Handsfree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                       devName.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0
                                                   ));

                                if (isBluetooth) continue;

                                try
                                {
                                    object oClient;
                                    if (micDev.Activate(ref IID_IAudioClient, 0, IntPtr.Zero, out oClient) == 0 && oClient != null)
                                    {
                                        var client = oClient as IAudioClient;
                                        if (client != null)
                                        {
                                            IntPtr format;
                                            client.GetMixFormat(out format);
                                            Guid empty = Guid.Empty;
                                            client.Initialize(0 /* SHARED */, 0, 10000000 /* 1s buffer */, 0, format, ref empty);
                                            client.Start();
                                            cachedMicClients.Add(client);
                                        }
                                    }
                                }
                                catch { }

                                try
                                {
                                    object oMeter;
                                    if (micDev.Activate(ref IID_IAudioMeterInfo, 0, IntPtr.Zero, out oMeter) == 0 && oMeter != null)
                                    {
                                        var meter = oMeter as IAudioMeterInformation;
                                        if (meter != null)
                                        {
                                            cachedMicMeters.Add(meter);
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                        finally
                        {
                            if (micDev != null) try { Marshal.ReleaseComObject(micDev); } catch { }
                        }
                    }
                    try { Marshal.ReleaseComObject(captureDevs); } catch { }
                }
            }
            catch
            {
                ClearMicCaches(cachedMicClients, cachedMicMeters);
            }
        }

        private void ScanAllActiveRenderSessions(
            IMMDeviceEnumerator enumerator,
            string defaultRenderDeviceId,
            string[] activeTriggers,
            string[] activeTargets,
            List<TriggerMeterInfo> cachedTriggerMeters,
            List<TargetSessionInfo> cachedTargetSessions,
            Dictionary<string, float> rememberedOriginalVolumes,
            bool isCurrentlyDucked)
        {
            // Release existing cached COM interfaces
            for (int m = 0; m < cachedTriggerMeters.Count; m++)
            {
                try { Marshal.ReleaseComObject(cachedTriggerMeters[m].Meter); } catch { }
                if (cachedTriggerMeters[m].SessionControl != null)
                {
                    try { Marshal.ReleaseComObject(cachedTriggerMeters[m].SessionControl); } catch { }
                }
            }
            cachedTriggerMeters.Clear();

            var oldTargetStates = new Dictionary<uint, float[]>();
            for (int v = 0; v < cachedTargetSessions.Count; v++)
            {
                oldTargetStates[cachedTargetSessions[v].ProcessId] = new float[] { cachedTargetSessions[v].CurrentVolume, cachedTargetSessions[v].LastAppliedVolume };
                try { Marshal.ReleaseComObject(cachedTargetSessions[v].Volume); } catch { }
                try { Marshal.ReleaseComObject(cachedTargetSessions[v].Meter); } catch { }
            }
            cachedTargetSessions.Clear();

            if (enumerator == null) return;

            try
            {
                IMMDeviceCollection renderDevs;
                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out renderDevs);
                if (hr == 0 && renderDevs != null)
                {
                    int devCount;
                    renderDevs.GetCount(out devCount);
                    Guid IID_IAudioSessionManager2 = typeof(IAudioSessionManager2).GUID;

                    for (int d = 0; d < devCount; d++)
                    {
                        IMMDevice dev = null;
                        IAudioSessionManager2 mgr = null;
                        IAudioSessionEnumerator sessionEnum = null;

                        try
                        {
                            if (renderDevs.Item(d, out dev) == 0 && dev != null)
                            {
                                string devId = "";
                                dev.GetId(out devId);
                                bool isDefaultDev = !string.IsNullOrEmpty(defaultRenderDeviceId) &&
                                                    string.Equals(defaultRenderDeviceId, devId, StringComparison.OrdinalIgnoreCase);

                                object o;
                                if (dev.Activate(ref IID_IAudioSessionManager2, 0, IntPtr.Zero, out o) == 0 && o != null)
                                {
                                    mgr = o as IAudioSessionManager2;
                                    if (mgr != null && mgr.GetSessionEnumerator(out sessionEnum) == 0 && sessionEnum != null)
                                    {
                                        int sCount;
                                        sessionEnum.GetCount(out sCount);

                                        for (int i = 0; i < sCount; i++)
                                        {
                                            IAudioSessionControl ctl = null;
                                            try
                                            {
                                                if (sessionEnum.GetSession(i, out ctl) == 0 && ctl != null)
                                                {
                                                    var ctl2 = ctl as IAudioSessionControl2;
                                                    if (ctl2 == null)
                                                    {
                                                        Marshal.ReleaseComObject(ctl);
                                                        continue;
                                                    }

                                                    uint pid;
                                                    ctl2.GetProcessId(out pid);
                                                    if (pid == 0)
                                                    {
                                                        Marshal.ReleaseComObject(ctl);
                                                        continue;
                                                    }

                                                    AudioSessionState state;
                                                    ctl2.GetState(out state);

                                                    // On non-default devices, ignore inactive/expired ghost sessions
                                                    if (!isDefaultDev && state != AudioSessionState.AudioSessionStateActive)
                                                    {
                                                        Marshal.ReleaseComObject(ctl);
                                                        continue;
                                                    }

                                                    string pName = ProcessHelper.GetHostProcessName(pid);
                                                    if (string.IsNullOrEmpty(pName))
                                                    {
                                                        Marshal.ReleaseComObject(ctl);
                                                        continue;
                                                    }

                                                    bool isSystemHost = pName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
                                                                        pName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                                                                        pName.Equals("ScreenClippingHost", StringComparison.OrdinalIgnoreCase) ||
                                                                        pName.Equals("SnippingTool", StringComparison.OrdinalIgnoreCase) ||
                                                                        pName.Equals("audiodg", StringComparison.OrdinalIgnoreCase) ||
                                                                        pName.Equals("svchost", StringComparison.OrdinalIgnoreCase);

                                                    if (isSystemHost)
                                                    {
                                                        Marshal.ReleaseComObject(ctl);
                                                        continue;
                                                    }

                                                    bool isMatch = false;
                                                    bool isTargetMatch = false;

                                                    // 1. Check Target Controls (Background Music app) FIRST!
                                                    // Any session matching an active target app is treated as target background music!
                                                    if (activeTargets != null && activeTargets.Length > 0)
                                                    {
                                                        foreach (var targ in activeTargets)
                                                        {
                                                            if (!string.IsNullOrEmpty(targ) && ProcessHelper.IsProcessMatch(pName, targ, pid))
                                                            {
                                                                isTargetMatch = true;
                                                                break;
                                                            }
                                                        }
                                                    }

                                                    if (isTargetMatch)
                                                    {
                                                        var vol = ctl as ISimpleAudioVolume;
                                                        var meter = ctl as IAudioMeterInformation;
                                                        if (vol != null && meter != null)
                                                        {
                                                            float actualVol = 1.0f;
                                                            try { vol.GetMasterVolume(out actualVol); } catch { }

                                                            string sessionKey = (isDefaultDev ? "default:" : (devId + ":")) + pName;

                                                            float origVol = 1.0f;
                                                            float savedOrig;
                                                            if (rememberedOriginalVolumes != null && (rememberedOriginalVolumes.TryGetValue(sessionKey, out savedOrig) || rememberedOriginalVolumes.TryGetValue(pName, out savedOrig)) && savedOrig > 0.20f)
                                                            {
                                                                origVol = savedOrig;
                                                            }
                                                            else if (actualVol > 0.20f)
                                                            {
                                                                origVol = actualVol;
                                                                if (rememberedOriginalVolumes != null)
                                                                {
                                                                    rememberedOriginalVolumes[sessionKey] = actualVol;
                                                                    rememberedOriginalVolumes[pName] = actualVol;
                                                                }
                                                            }
                                                            else
                                                            {
                                                                origVol = 1.0f;
                                                                if (rememberedOriginalVolumes != null)
                                                                {
                                                                    rememberedOriginalVolumes[sessionKey] = 1.0f;
                                                                    rememberedOriginalVolumes[pName] = 1.0f;
                                                                }
                                                            }

                                                            float curVol = actualVol;
                                                            float lastApplied = -1.0f;
                                                            float[] oldState;
                                                            if (oldTargetStates.TryGetValue(pid, out oldState))
                                                            {
                                                                curVol = oldState[0];
                                                                lastApplied = oldState[1];
                                                            }

                                                            cachedTargetSessions.Add(new TargetSessionInfo
                                                            {
                                                                Volume = vol,
                                                                Meter = meter,
                                                                ProcessName = pName,
                                                                ProcessId = pid,
                                                                SessionKey = sessionKey,
                                                                IsDefaultDevice = isDefaultDev,
                                                                OriginalVolume = origVol,
                                                                CurrentVolume = curVol,
                                                                LastAppliedVolume = lastApplied
                                                            });
                                                        }

                                                        try
                                                        {
                                                            ctl2.SetDuckingPreference(true);
                                                        }
                                                        catch { }
                                                    }

                                                    // 2. Check Trigger Meters ONLY if this session is NOT a target music session (prevent self-ducking!)
                                                    // AND ONLY if the session is currently ACTIVE (sound is actively playing)!
                                                    if (!isTargetMatch && activeTriggers != null && state == AudioSessionState.AudioSessionStateActive)
                                                    {
                                                        foreach (var trig in activeTriggers)
                                                        {
                                                            if (string.IsNullOrEmpty(trig)) continue;

                                                            if (ProcessHelper.IsProcessMatch(pName, trig, pid))
                                                            {
                                                                isMatch = true;
                                                                var meter = ctl as IAudioMeterInformation;
                                                                if (meter != null)
                                                                {
                                                                    cachedTriggerMeters.Add(new TriggerMeterInfo
                                                                    {
                                                                        Meter = meter,
                                                                        SessionControl = ctl2,
                                                                        ProcessName = trig
                                                                    });
                                                                }
                                                                break;
                                                            }
                                                        }
                                                    }

                                                    if (!isMatch && !isTargetMatch)
                                                    {
                                                        Marshal.ReleaseComObject(ctl);
                                                    }
                                                }
                                            }
                                            catch
                                            {
                                                if (ctl != null) try { Marshal.ReleaseComObject(ctl); } catch { }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            if (sessionEnum != null) try { Marshal.ReleaseComObject(sessionEnum); } catch { }
                            if (mgr != null) try { Marshal.ReleaseComObject(mgr); } catch { }
                            if (dev != null) try { Marshal.ReleaseComObject(dev); } catch { }
                        }
                    }

                    try { Marshal.ReleaseComObject(renderDevs); } catch { }
                }
            }
            catch { }
        }

        private void RestoreAllTargetVolumes(
            IMMDeviceEnumerator enumerator,
            List<TargetSessionInfo> cachedSessions,
            string[] targets,
            Dictionary<string, float> rememberedOriginalVolumes)
        {
            Guid empty = Guid.Empty;

            // 1. Immediately restore cached volume instances
            if (cachedSessions != null)
            {
                for (int i = 0; i < cachedSessions.Count; i++)
                {
                    try
                    {
                        float restoreVal = cachedSessions[i].OriginalVolume > 0.05f ? cachedSessions[i].OriginalVolume : 1.0f;
                        cachedSessions[i].Volume.SetMasterVolume(restoreVal, ref empty);
                    }
                    catch { }
                }
            }

            // 2. Also ensure all active render endpoints have all targets restored
            if (enumerator != null && targets != null && targets.Length > 0)
            {
                try
                {
                    IMMDeviceCollection renderDevs;
                    if (enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1 /* DEVICE_STATE_ACTIVE */, out renderDevs) == 0 && renderDevs != null)
                    {
                        int devCount;
                        renderDevs.GetCount(out devCount);
                        Guid IID_IAudioSessionManager2 = typeof(IAudioSessionManager2).GUID;

                        for (int d = 0; d < devCount; d++)
                        {
                            IMMDevice dev = null;
                            IAudioSessionManager2 mgr = null;
                            IAudioSessionEnumerator sessionEnum = null;
                            try
                            {
                                if (renderDevs.Item(d, out dev) == 0 && dev != null)
                                {
                                    object o;
                                    if (dev.Activate(ref IID_IAudioSessionManager2, 0, IntPtr.Zero, out o) == 0 && o != null)
                                    {
                                        mgr = o as IAudioSessionManager2;
                                        if (mgr != null && mgr.GetSessionEnumerator(out sessionEnum) == 0 && sessionEnum != null)
                                        {
                                            int count;
                                            sessionEnum.GetCount(out count);
                                            for (int i = 0; i < count; i++)
                                            {
                                                IAudioSessionControl ctl = null;
                                                try
                                                {
                                                    if (sessionEnum.GetSession(i, out ctl) == 0 && ctl != null)
                                                    {
                                                        var ctl2 = ctl as IAudioSessionControl2;
                                                        if (ctl2 != null)
                                                        {
                                                            uint pid;
                                                            ctl2.GetProcessId(out pid);
                                                            string pName = ProcessHelper.GetHostProcessName(pid);
                                                            foreach (var target in targets)
                                                            {
                                                                if (!string.IsNullOrEmpty(target) && ProcessHelper.IsProcessMatch(pName, target, pid))
                                                                {
                                                                    var v = ctl as ISimpleAudioVolume;
                                                                    if (v != null)
                                                                    {
                                                                        float restoreVal = 1.0f;
                                                                        float saved;
                                                                        if (rememberedOriginalVolumes != null && (rememberedOriginalVolumes.TryGetValue("default:" + pName, out saved) || rememberedOriginalVolumes.TryGetValue(pName, out saved)))
                                                                        {
                                                                            restoreVal = saved > 0.05f ? saved : 1.0f;
                                                                        }
                                                                        v.SetMasterVolume(restoreVal, ref empty);
                                                                    }
                                                                    break;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                catch { }
                                                finally
                                                {
                                                    if (ctl != null) try { Marshal.ReleaseComObject(ctl); } catch { }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch { }
                            finally
                            {
                                if (sessionEnum != null) try { Marshal.ReleaseComObject(sessionEnum); } catch { }
                                if (mgr != null) try { Marshal.ReleaseComObject(mgr); } catch { }
                                if (dev != null) try { Marshal.ReleaseComObject(dev); } catch { }
                            }
                        }
                        try { Marshal.ReleaseComObject(renderDevs); } catch { }
                    }
                }
                catch { }
            }
        }
    }
}
