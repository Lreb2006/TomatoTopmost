using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("TomatoTopmost")]
[assembly: AssemblyDescription("Keep the TomatoToDo main window on top")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace TomatoTopmost
{
    internal static class Native
    {
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")]
        private static extern int GetStyle32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")]
        private static extern IntPtr GetStyle64(IntPtr window, int index);
        [DllImport("user32.dll", SetLastError=true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        internal static bool Topmost(IntPtr window)
        {
            long style = IntPtr.Size == 8 ? GetStyle64(window, -20).ToInt64() : GetStyle32(window, -20);
            return (style & 8) != 0;
        }
        internal static void SetTopmost(IntPtr window, bool enabled)
        {
            // Keep position, size and keyboard focus unchanged. NOSENDCHANGING prevents
            // a target's WM_WINDOWPOSCHANGING handler from silently rejecting the new order.
            if (!SetWindowPos(window, new IntPtr(enabled ? -1 : -2), 0, 0, 0, 0, 0x413))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        internal static bool OwnedBy(IntPtr window, int pid)
        {
            uint actual;
            return IsWindow(window) && GetWindowThreadProcessId(window, out actual) != 0 && actual == (uint)pid;
        }
    }

    internal sealed class TrackedWindow
    {
        internal IntPtr Handle;
        internal int Pid;
        internal bool OriginalTopmost;
        internal TrackedWindow(IntPtr handle, int pid)
        { Handle = handle; Pid = pid; OriginalTopmost = Native.Topmost(handle); }
        internal void Restore()
        {
            if (Native.OwnedBy(Handle, Pid)) Native.SetTopmost(Handle, OriginalTopmost);
        }
    }

    internal static class Program
    {
        internal const string MutexName = "Local\\TomatoTopmost.App.v1";
        internal const string StopName = "Local\\TomatoTopmost.Stop.v1";
        internal const string ShowName = "Local\\TomatoTopmost.Show.v1";
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--test-fixture")
            {
                var fixtureWindow = new NativeWindow();
                fixtureWindow.CreateHandle(new CreateParams {
                    Caption = "TomatoTopmost off-screen test fixture", Style = unchecked((int)0x90000000),
                    ExStyle = 0x08000080, X = -32000, Y = -32000, Width = 1, Height = 1
                });
                try
                {
                using (var timeout = new System.Windows.Forms.Timer { Interval = 50 })
                {
                    bool ready = false;
                    timeout.Tick += delegate {
                        if (!ready) {
                            ready = true;
                            File.WriteAllText(args[1], fixtureWindow.Handle.ToInt64().ToString());
                            timeout.Interval = 15000;
                        } else Application.ExitThread();
                    };
                    timeout.Start();
                    Application.Run();
                }
                }
                finally { fixtureWindow.DestroyHandle(); }
                return 0;
            }
            if (args.Length == 1 && args[0] == "--stop")
            {
                try { using (var e = EventWaitHandle.OpenExisting(StopName)) e.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    if (Array.IndexOf(args, "--background") < 0)
                        try { using (var e = EventWaitHandle.OpenExisting(ShowName)) e.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { }
                    return 0;
                }
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopName))
                    using (var context = new TrayContext(stop, Array.IndexOf(args, "--background") < 0)) Application.Run(context);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("工具启动失败。\n" + ex.Message, "番茄置顶助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
            return 0;
        }
        private static int SelfTest()
        {
            string signal = Path.GetTempFileName();
            Process fixture = null;
            try
            {
                // A separate fixture matches the real cross-process operation; its window stays hidden.
                fixture = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--test-fixture \"" + signal + "\"") { UseShellExecute = false, CreateNoWindow = true });
                long value = 0;
                for (int i = 0; i < 100 && value == 0; i++)
                {
                    Thread.Sleep(50);
                    try { Int64.TryParse(File.ReadAllText(signal), out value); } catch (IOException) { }
                }
                if (value == 0) throw new Exception("Fixture window did not start");
                    IntPtr h = new IntPtr(value);
                    var tracked = new TrackedWindow(h, fixture.Id);
                    ApplyTestFlag(h, true);
                    if (!Native.Topmost(h)) throw new Exception("Topmost flag did not apply");
                    if (Native.OwnedBy(h, -1)) throw new Exception("Ownership validation failed");
                    tracked.Restore();
                    if (Native.Topmost(h) != tracked.OriginalTopmost) throw new Exception("Restore failed");
                    ApplyTestFlag(h, true);
                    tracked = new TrackedWindow(h, fixture.Id);
                    tracked.Restore();
                    if (!Native.Topmost(h)) throw new Exception("Original topmost was not preserved");
                    ApplyTestFlag(h, false);
                    if (Native.Topmost(h)) throw new Exception("Explicit OFF did not clear topmost");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
            finally
            {
                if (fixture != null) { if (!fixture.HasExited) fixture.Kill(); fixture.Dispose(); }
                File.Delete(signal);
            }
        }
        private static void ApplyTestFlag(IntPtr h, bool enabled)
        {
            // Match the production polling repair, allowing the new window to settle.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Native.SetTopmost(h, enabled);
                if (Native.Topmost(h) == enabled) return;
                Thread.Sleep(200);
            }
            throw new Exception("Window flag did not reach the requested state within two seconds");
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly EventWaitHandle stop;
        private readonly EventWaitHandle show;
        private readonly NotifyIcon icon;
        private readonly Icon appIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly ToolStripMenuItem status;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly Dictionary<IntPtr, TrackedWindow> windows = new Dictionary<IntPtr, TrackedWindow>();
        private DateTime nextScan = DateTime.MinValue;
        private bool enabled;
        private bool disposed;
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string SettingsKey = "Software\\TomatoTopmost";
        private Form panel;
        private Button toggleButton;
        private Label panelStatus;

        internal TrayContext(EventWaitHandle stopEvent, bool showPanel)
        {
            stop = stopEvent;
            show = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowName);
            using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                enabled = key != null && Convert.ToInt32(key.GetValue("Enabled", 0)) == 1;
            var menu = new ContextMenuStrip();
            status = new ToolStripMenuItem("等待番茄ToDo启动") { Enabled = false };
            enabledItem = new ToolStripMenuItem("置顶") { Checked = enabled };
            enabledItem.Click += delegate { Toggle(); };
            startupItem = new ToolStripMenuItem("开机启动（当前用户）") { Checked = StartupEnabled() };
            startupItem.Click += delegate
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (StartupEnabled()) key.DeleteValue("TomatoTopmost", false);
                        else key.SetValue("TomatoTopmost", "\"" + Application.ExecutablePath + "\" --background");
                    }
                    startupItem.Checked = StartupEnabled();
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "无法修改开机启动"); }
            };
            menu.Items.Add(status);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开控制窗口", null, delegate { ShowPanel(); });
            menu.Items.Add(enabledItem);
            menu.Items.Add(startupItem);
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TomatoTopmost.app.ico"))
                appIcon = new Icon(stream, new Size(32, 32));
            icon = new NotifyIcon { Icon = appIcon, Text = "番茄置顶助手", ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += delegate { ShowPanel(); };
            timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += Tick;
            timer.Start();
            Tick(null, EventArgs.Empty);
            if (showPanel) ShowPanel();
        }
        private static bool StartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && String.Equals(key.GetValue("TomatoTopmost") as string,
                    "\"" + Application.ExecutablePath + "\" --background", StringComparison.OrdinalIgnoreCase);
        }
        private void Toggle()
        {
            enabled = !enabled;
            enabledItem.Checked = enabled;
            if (!enabled)
            {
                // Explicit OFF clears the flag even when the official app originally set it.
                foreach (var w in windows.Values)
                    if (Native.OwnedBy(w.Handle, w.Pid))
                        try { Native.SetTopmost(w.Handle, false); } catch (System.ComponentModel.Win32Exception) { }
                windows.Clear();
            }
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey)) key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception ex) { MessageBox.Show("当前开关已生效，但无法保存下次启动的设置。\n" + ex.Message, "番茄置顶助手"); }
            nextScan = DateTime.MinValue;
            Tick(null, EventArgs.Empty);
        }
        private void ShowPanel()
        {
            if (panel == null)
            {
                panel = new Form {
                    Text = "番茄置顶助手", Icon = appIcon, ClientSize = new Size(430, 280),
                    FormBorderStyle = FormBorderStyle.FixedSingle, MaximizeBox = false,
                    StartPosition = FormStartPosition.CenterScreen, BackColor = Color.FromArgb(250,250,250),
                    Font = new Font("Microsoft YaHei UI", 10), AutoScaleMode = AutoScaleMode.Dpi
                };
                panel.Controls.Add(new Label { Text = "让番茄ToDo保持在前面", AutoSize = true,
                    Location = new Point(24,24), Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold) });
                panel.Controls.Add(new Label { Text = "通过下方开关控制，关闭后即可取消强制置顶。", AutoSize = true,
                    Location = new Point(25,65), ForeColor = Color.DimGray });
                toggleButton = new Button { Location = new Point(25,104), Size = new Size(380,58),
                    FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold), Cursor = Cursors.Hand };
                toggleButton.FlatAppearance.BorderSize = 0;
                toggleButton.Click += delegate { Toggle(); };
                panel.Controls.Add(toggleButton);
                panelStatus = new Label { AutoSize = false, Location = new Point(25,174), Size = new Size(380,30) };
                panel.Controls.Add(panelStatus);
                panel.Controls.Add(new Label { Text = "建议关闭番茄ToDo自带的置顶，只用本工具控制。\n关闭此窗口后，仍可在任务栏托盘中开关或退出。",
                    Location = new Point(25,214), Size = new Size(380,48), ForeColor = Color.DimGray, Font = new Font("Microsoft YaHei UI", 9) });
                panel.FormClosing += delegate(object sender, FormClosingEventArgs e)
                {
                    if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; panel.Hide(); }
                };
            }
            UpdatePanel();
            panel.Show();
            panel.WindowState = FormWindowState.Normal;
            panel.Activate();
        }
        private void UpdatePanel()
        {
            if (panel == null) return;
            toggleButton.Text = enabled ? "置顶  已开启 · 点击关闭" : "置顶  已关闭 · 点击开启";
            toggleButton.BackColor = enabled ? Color.FromArgb(45,143,107) : Color.FromArgb(232,234,237);
            toggleButton.ForeColor = enabled ? Color.White : Color.FromArgb(40,40,40);
            panelStatus.Text = status.Text;
        }
        private void Scan()
        {
            foreach (var p in Process.GetProcessesByName("TomaToDo"))
            {
                using (p)
                {
                    try
                    {
                        IntPtr h = p.MainWindowHandle;
                        if (h != IntPtr.Zero && Native.OwnedBy(h, p.Id) && !windows.ContainsKey(h))
                            windows.Add(h, new TrackedWindow(h, p.Id));
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
        }
        private void Tick(object sender, EventArgs e)
        {
            if (stop.WaitOne(0)) { ExitThread(); return; }
            if (show.WaitOne(0)) ShowPanel();
            if (!enabled) { status.Text = "已关闭强制置顶"; icon.Text = "番茄置顶助手 · 已关闭"; UpdatePanel(); return; }
            try
            {
                if (DateTime.UtcNow >= nextScan) { Scan(); nextScan = DateTime.UtcNow.AddSeconds(1); }
                int visible = 0;
                foreach (var pair in new List<KeyValuePair<IntPtr, TrackedWindow>>(windows))
                {
                    var w = pair.Value;
                    if (!Native.OwnedBy(w.Handle, w.Pid)) { windows.Remove(pair.Key); continue; }
                    if (!Native.IsWindowVisible(w.Handle) || Native.IsIconic(w.Handle)) continue;
                    if (!Native.Topmost(w.Handle)) Native.SetTopmost(w.Handle, true);
                    visible++;
                }
                status.Text = visible > 0 ? "正在保持置顶" : "等待番茄ToDo窗口";
                icon.Text = "番茄置顶助手 · " + status.Text;
            }
            catch (Exception ex)
            {
                status.Text = "暂时无法置顶，请检查应用权限";
                icon.Text = "番茄置顶助手 · 置顶失败";
                Debug.WriteLine(ex.Message);
            }
            UpdatePanel();
        }
        private void RestoreAll()
        {
            foreach (var w in windows.Values)
                try { w.Restore(); } catch (System.ComponentModel.Win32Exception) { }
            windows.Clear();
        }
        protected override void ExitThreadCore()
        {
            timer.Stop();
            RestoreAll();
            icon.Visible = false;
            base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Dispose();
                show.Dispose();
                RestoreAll();
                icon.Visible = false;
                icon.ContextMenuStrip.Dispose();
                icon.Dispose();
                if (panel != null) panel.Dispose();
                appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
