// FaceUnlockAutoConfirm
// Copyright (C) 2026 NetFert <https://github.com/NetFert/FaceUnlockAutoConfirm>
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU Affero General Public License as published by the Free Software Foundation, version 3 of the
// License only.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
// even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero
// General Public License for more details. You should have received a copy of the GNU Affero General
// Public License along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Brings the Windows Hello dialog to front; clicks OK after successful face recognition.
// Needs elevation: the dialog runs above medium integrity (UIPI).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using UIA = Interop.UIAutomationClient;

[assembly: AssemblyTitle("FaceUnlockAutoConfirm")]
[assembly: AssemblyProduct("FaceUnlockAutoConfirm")]
[assembly: AssemblyDescription("Keeps the Windows Hello prompt in front and auto-confirms after face recognition")]
[assembly: AssemblyCopyright("Copyright (C) 2026 NetFert. AGPL-3.0-only")]
[assembly: AssemblyVersion("0.0.2.0")]
[assembly: AssemblyFileVersion("0.0.2.0")]
[assembly: AssemblyInformationalVersion("0.0.2")]

static class FaceUnlockAutoConfirm
{
    const uint EVENT_OBJECT_SHOW = 0x8002;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    const uint GA_ROOT = 2;
    const int SW_SHOW = 5;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_SHOWWINDOW = 0x0040;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    // Target: system Hello dialog only.
    const string DialogClass = "Credential Dialog Xaml Host";
    static readonly string BrokerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "CredentialUIBroker.exe");

    // UIA ids.
    const string OkButtonId = "OkButton";            // appears only after recognition
    const string BioFeedbackId = "BioFeedbackText";  // biometric status text
    const int UIA_ButtonControlTypeId = 50000;
    const int UIA_EditControlTypeId = 50004;
    const int UIA_InvokePatternId = 10000;
    const int UIA_ControlTypePropertyId = 30003;
    const int UIA_NamePropertyId = 30005;
    const int UIA_IsEnabledPropertyId = 30010;
    const int UIA_AutomationIdPropertyId = 30011;
    const int UIA_IsOffscreenPropertyId = 30022;
    const int PollIntervalMs = 30;

    static readonly HashSet<IntPtr> _active = new HashSet<IntPtr>();
    static readonly Lazy<UIA.IUIAutomation> _uia = new Lazy<UIA.IUIAutomation>(
        () => (UIA.IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("e22ad333-b25f-460c-83d0-0581107395c9"))));
    static WinEventProc _proc; // keep delegate alive
    static string _logPath;
    static readonly object _logLock = new object();

    delegate void WinEventProc(IntPtr hHook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y; public uint lPrivate; }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool altTab);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr proc, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [STAThread]
    static void Main()
    {
        bool created;
        var mutex = new Mutex(true, @"Local\FaceUnlockAutoConfirm_SingleInstance", out created);
        if (!created) return;

        bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        string dir = PrepareLogDirectory(admin);
        if (dir != null)
        {
            _logPath = Path.Combine(dir, "FaceUnlockAutoConfirm.log");
            try { File.WriteAllText(_logPath, ""); } catch { } // new log per start
        }
        Log(string.Format("FaceUnlockAutoConfirm {0} started, elevated={1}{2}",
            Assembly.GetExecutingAssembly().GetName().Version.ToString(3), admin,
            admin ? "" : " (bring-to-front only)"));

        _proc = OnWinEvent;
        SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        GC.KeepAlive(mutex);
    }

    // Elevated: log only to an admin-owned, non-reparse ProgramData dir (no writes to user-writable paths).
    // Not elevated: LOCALAPPDATA.
    static string PrepareLogDirectory(bool admin)
    {
        try
        {
            if (!admin)
            {
                string userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaceUnlockAutoConfirm");
                Directory.CreateDirectory(userDir);
                return userDir;
            }

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FaceUnlockAutoConfirm");
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            if (!Directory.Exists(dir))
            {
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.SetOwner(admins);
                Directory.CreateDirectory(dir, sec);
            }

            var info = new DirectoryInfo(dir);
            var owner = info.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || !(owner.Equals(admins) || owner.Equals(system)))
                return null;
            return dir;
        }
        catch { return null; }
    }

    static void OnWinEvent(IntPtr hHook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (GetAncestor(hwnd, GA_ROOT) != hwnd) return;

        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        if (sb.ToString() != DialogClass) return;

        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        string image = ProcessImagePath(pid);
        if (!string.Equals(image, BrokerPath, StringComparison.OrdinalIgnoreCase))
        {
            Log("ignored: class match, unexpected process " + (image ?? "?"));
            return;
        }

        lock (_active) { if (!_active.Add(hwnd)) return; } // SHOW may fire repeatedly
        Log("detected Windows Hello dialog, pid=" + pid);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { BringToFront(hwnd); WatchAndConfirm(hwnd); }
            catch (Exception ex) { Log("error: " + ex); }
            finally { lock (_active) _active.Remove(hwnd); }
        });
    }

    static string ProcessImagePath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    static void BringToFront(IntPtr hwnd)
    {
        for (int i = 0; i < 20; i++)
        {
            if (!IsWindow(hwnd)) return;
            if (GetForegroundWindow() == hwnd)
            {
                Log("brought to front, attempts=" + i);
                return;
            }
            ForceForeground(hwnd);
            Thread.Sleep(150);
        }
        Log("bring to front FAILED");
    }

    static void ForceForeground(IntPtr hwnd)
    {
        uint pid;
        IntPtr fg = GetForegroundWindow();
        uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out pid);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            if (!IsWindowVisible(hwnd)) ShowWindow(hwnd, SW_SHOW);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            BringWindowToTop(hwnd);
            if (!SetForegroundWindow(hwnd)) SwitchToThisWindow(hwnd, true);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
    }

    // Click OK only if ALL hold (once per dialog):
    //   a. owner = System32\CredentialUIBroker.exe (checked in OnWinEvent)
    //   b. dialog seen without OkButton first
    //   c. OkButton then present, enabled, on-screen
    //   d. BioFeedbackText present, no Edit controls (not PIN/password mode)
    //   e. dialog is foreground
    static void WatchAndConfirm(IntPtr hwnd)
    {
        var uia = _uia.Value;
        var trueCond = uia.CreateTrueCondition();
        var cache = uia.CreateCacheRequest(); // one cross-process call per scan
        foreach (int id in new[] { UIA_ControlTypePropertyId, UIA_NamePropertyId, UIA_IsEnabledPropertyId, UIA_AutomationIdPropertyId, UIA_IsOffscreenPropertyId })
            cache.AddProperty(id);

        var since = Stopwatch.StartNew();
        bool sawNoOk = false;
        string lastState = null;
        long maxScanMs = 0;
        UIA.IUIAutomationElement root = null;
        while (since.Elapsed < TimeSpan.FromMinutes(2) && IsWindow(hwnd))
        {
            try
            {
                var scan = Stopwatch.StartNew();
                if (root == null) root = uia.ElementFromHandle(hwnd);
                var all = root.FindAllBuildCache(UIA.TreeScope.TreeScope_Descendants, trueCond, cache);
                UIA.IUIAutomationElement okButton = null;
                bool okPresent = false, hasEdit = false;
                string bioText = null;
                for (int i = 0; i < all.Length; i++)
                {
                    var e = all.GetElement(i);
                    string aid = e.CachedAutomationId ?? "";
                    int type = e.CachedControlType;

                    if (type == UIA_EditControlTypeId) hasEdit = true;
                    else if (aid == BioFeedbackId) bioText = e.CachedName;
                    else if (aid == OkButtonId && type == UIA_ButtonControlTypeId)
                    {
                        okPresent = true;
                        if (e.CachedIsEnabled != 0 && e.CachedIsOffscreen == 0) okButton = e;
                    }
                }
                long scanMs = scan.ElapsedMilliseconds;
                if (scanMs > maxScanMs) maxScanMs = scanMs;

                if (!okPresent) sawNoOk = true;

                string state = string.Format("ok={0} edit={1} bio='{2}'", okPresent, hasEdit, (bioText ?? "-").Replace("\r", " ").Replace("\n", " "));
                if (state != lastState) { Log(string.Format("state: {0}  (t+{1}ms, scan {2}ms)", state, since.ElapsedMilliseconds, scanMs)); lastState = state; }

                if (sawNoOk && okButton != null && !string.IsNullOrEmpty(bioText) && !hasEdit && GetForegroundWindow() == hwnd)
                {
                    var p = okButton.GetCurrentPattern(UIA_InvokePatternId) as UIA.IUIAutomationInvokePattern;
                    if (p != null)
                    {
                        var click = Stopwatch.StartNew();
                        p.Invoke();
                        Log(string.Format("auto-confirmed (clicked OK)  (t+{0}ms, invoke {1}ms, max scan {2}ms)", since.ElapsedMilliseconds, click.ElapsedMilliseconds, maxScanMs));
                        LogWhenClosed(hwnd, click);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                string err = "!" + ex.GetType().Name;
                if (lastState != err) { Log("ui read error: " + ex.Message); lastState = err; }
                root = null;
            }
            Thread.Sleep(PollIntervalMs);
        }
    }

    // Dialog stays up while Windows finishes verification; log how long.
    static void LogWhenClosed(IntPtr hwnd, Stopwatch sinceClick)
    {
        while (sinceClick.ElapsedMilliseconds < 10000)
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd))
            {
                Log(string.Format("dialog closed {0}ms after click", sinceClick.ElapsedMilliseconds));
                return;
            }
            Thread.Sleep(20);
        }
        Log("dialog still visible 10s after click");
    }

    static void Log(string s)
    {
        if (_logPath == null) return;
        lock (_logLock)
        {
            try { File.AppendAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + s + Environment.NewLine); } catch { }
        }
    }
}
