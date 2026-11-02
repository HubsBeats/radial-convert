using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace RadialConvert
{
    class TrayApp : ApplicationContext
    {
        readonly Control ui;
        readonly NotifyIcon icon;
        readonly RadialMenu menu;
        readonly MouseHook hook;
        AppConfig cfg;
        ToolStripMenuItem pauseItem, startupItem;

        List<string> pendingFiles;
        string[] pendingSlots;
        int busy;

        public TrayApp()
        {
            ui = new Control();
            ui.CreateControl();
            IntPtr force = ui.Handle;
            cfg = AppConfig.Load();
            menu = new RadialMenu();

            icon = new NotifyIcon();
            icon.Icon = IconMaker.Make(32);
            icon.Text = "Radial Convert";
            icon.ContextMenuStrip = BuildMenu();
            icon.Visible = true;

            hook = new MouseHook(ui, OnTrigger, OnRelease, OnCancel);
            hook.Start();

            if (!hook.Installed)
                Tip("Radial Convert couldn't start", "Windows refused the mouse hook. Check log.txt.", ToolTipIcon.Error);
            else if (Tools.FindFfmpeg(cfg) == null)
                GetFfmpeg();
            else if (!File.Exists(Path.Combine(AppConfig.HomeDir, ".welcomed")))
            {
                Tip("Radial Convert is running", "Select files in Explorer or on the desktop, hold Shift and right-click, then flick toward a format.", ToolTipIcon.Info);
                try { File.WriteAllText(Path.Combine(AppConfig.HomeDir, ".welcomed"), DateTime.Now.ToString("o")); } catch { }
            }
            Log.Write("started, ffmpeg=" + (Tools.FindFfmpeg(cfg) ?? "missing"));
        }

        // Standalone exe with no FFmpeg yet: fetch it once in the background.
        void GetFfmpeg()
        {
            Tip("Getting FFmpeg (one time)", "Downloading the converter for audio, video, GIF and WebP (about 100 MB). Images to PNG, JPG and PDF already work.", ToolTipIcon.Info);
            var th = new Thread(() =>
            {
                string err = null;
                try { FfmpegDownload.Run(); }
                catch (Exception ex) { err = ex.Message; Log.Write("FFmpeg download failed: " + ex); }
                ui.BeginInvoke((Action)(() =>
                {
                    if (err == null) Tip("Radial Convert is ready", "Select files, hold Shift and right-click, then flick toward a format.", ToolTipIcon.Info);
                    else Tip("FFmpeg download failed", err + " Audio and video need FFmpeg; restart Radial Convert to try again.", ToolTipIcon.Warning);
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            var title = new ToolStripMenuItem("Radial Convert " + Program.Version);
            title.Enabled = false;
            m.Items.Add(title);
            m.Items.Add(new ToolStripSeparator());
            pauseItem = new ToolStripMenuItem("Pause", null, delegate
            {
                hook.Paused = !hook.Paused;
                pauseItem.Text = hook.Paused ? "Resume" : "Pause";
                icon.Text = hook.Paused ? "Radial Convert (paused)" : "Radial Convert";
            });
            m.Items.Add(pauseItem);
            m.Items.Add(new ToolStripMenuItem("Edit formats...", null, delegate { Open("notepad.exe", Converter.Quote(cfg.FilePath)); }));
            m.Items.Add(new ToolStripMenuItem("Open log", null, delegate { Open("notepad.exe", Converter.Quote(Path.Combine(AppConfig.HomeDir, "log.txt"))); }));
            startupItem = new ToolStripMenuItem("Start with Windows", null, delegate
            {
                Startup.Set(!Startup.IsOn);
                startupItem.Checked = Startup.IsOn;
            });
            startupItem.Checked = Startup.IsOn;
            m.Items.Add(startupItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Exit", null, delegate { ExitThread(); }));
            return m;
        }

        static void Open(string exe, string args)
        {
            try { Process.Start(exe, args); } catch (Exception ex) { Log.Write("open failed: " + ex.Message); }
        }

        void Tip(string title, string text, ToolTipIcon kind)
        {
            icon.BalloonTipTitle = title;
            icon.BalloonTipText = text;
            icon.BalloonTipIcon = kind;
            icon.ShowBalloonTip(4000);
        }

        // UI thread: Shift + right-button-down landed on Explorer or the desktop.
        void OnTrigger(Point pt)
        {
            try { Trigger(pt); }
            catch (Exception ex)
            {
                Log.Write("trigger failed: " + ex);
                hook.ResolvePending(false);
                menu.Finish();
                pendingFiles = null;
            }
        }

        void Trigger(Point pt)
        {
            if (cfg.IsStale()) cfg = AppConfig.Load(cfg.FilePath);

            List<string> files = ShellSelection.At(pt);
            Category cat = null;
            var picked = new List<string>();
            foreach (string f in files)
            {
                Category c = cfg.CategoryFor(f);
                if (c == null) continue;
                if (cat == null) cat = c;
                if (c == cat) picked.Add(f);
            }
            if (cat == null)
            {
                hook.ResolvePending(false); // nothing to convert: let Windows show its normal menu
                return;
            }

            string[] slots = AppConfig.ResolveSlots(cat, Path.GetExtension(picked[0]));
            pendingFiles = picked;
            pendingSlots = slots;

            if (hook.ResolvePending(true))
            {
                // Released before the menu could appear: treat it as a flick.
                int d = RadialMenu.DirectionFor(pt, Native.CursorPos(), (int)(cfg.DeadZone * Native.DpiScaleAt(pt)));
                if (d >= 0) Run(picked, slots[d]);
                pendingFiles = null;
                return;
            }

            string centre = picked.Count == 1
                ? Path.GetExtension(picked[0]).TrimStart('.').ToUpperInvariant()
                : picked.Count + " files";
            if (files.Count > picked.Count) centre += "\n+" + (files.Count - picked.Count) + " skipped";
            menu.Open(pt, slots, centre, cfg.DeadZone);
        }

        void OnRelease()
        {
            int d = menu.Finish();
            if (d >= 0 && pendingFiles != null) Run(pendingFiles, pendingSlots[d]);
            pendingFiles = null;
        }

        void OnCancel()
        {
            menu.Finish();
            pendingFiles = null;
        }

        void Run(List<string> files, string target)
        {
            var conv = new Converter(cfg);
            string label = target.ToUpperInvariant();
            Interlocked.Increment(ref busy);
            icon.Text = "Radial Convert: making " + label + "...";
            var th = new Thread(() =>
            {
                List<ConvertResult> results = conv.ConvertMany(files, target, (d, n) =>
                {
                    if (n > 1) ui.BeginInvoke((Action)(() => { icon.Text = "Radial Convert: " + label + " " + d + "/" + n; }));
                });
                ui.BeginInvoke((Action)(() => Report(results, label)));
            });
            th.IsBackground = true;
            th.Start();
        }

        void Report(List<ConvertResult> results, string label)
        {
            if (Interlocked.Decrement(ref busy) == 0) icon.Text = hook.Paused ? "Radial Convert (paused)" : "Radial Convert";
            int ok = 0, skipped = 0;
            string firstError = null, firstOut = null;
            foreach (var r in results)
            {
                if (r.Ok) { ok++; if (firstOut == null) firstOut = r.Output; }
                else if (r.Skipped) skipped++;
                else if (firstError == null) firstError = Path.GetFileName(r.Input) + ": " + r.Error;
            }
            int failed = results.Count - ok - skipped;
            if (failed == 0)
            {
                string text = ok == 1 ? Path.GetFileName(firstOut) : ok + " files saved next to the originals";
                if (skipped > 0) text += " (" + skipped + " already " + label + ")";
                if (ok == 0) text = "Already " + label + ", nothing to do.";
                Tip("Converted to " + label, text, ToolTipIcon.None);
            }
            else
            {
                Tip(ok + " converted, " + failed + " failed", firstError, ToolTipIcon.Warning);
            }
        }

        protected override void ExitThreadCore()
        {
            hook.Stop();
            menu.Finish();
            icon.Visible = false;
            icon.Dispose();
            base.ExitThreadCore();
        }
    }

    static class IconMaker
    {
        public static Icon Make(int px)
        {
            using (var bmp = MakeBitmap(px)) return Icon.FromHandle(bmp.GetHicon());
        }

        public static Bitmap MakeBitmap(int px)
        {
            var bmp = new Bitmap(px, px);
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float m = px * 0.06f, d = px - 2 * m;
                    using (var b = new SolidBrush(Color.FromArgb(255, 30, 31, 38))) g.FillEllipse(b, m, m, d, d);
                    using (var b = new SolidBrush(Color.FromArgb(255, 0, 145, 255))) g.FillPie(b, m, m, d, d, 225, 90);
                    float r = px * 0.17f;
                    using (var b = new SolidBrush(Color.FromArgb(255, 30, 31, 38))) g.FillEllipse(b, px / 2f - r, px / 2f - r, 2 * r, 2 * r);
                    using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1f, px / 24f))) g.DrawEllipse(p, m, m, d, d);
                }
                return bmp;
            }
        }
    }

    static class Startup
    {
        static string LinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Radial Convert.lnk"); }
        }

        public static bool IsOn { get { return File.Exists(LinkPath); } }

        public static void Set(bool on)
        {
            try
            {
                if (!on) { if (File.Exists(LinkPath)) File.Delete(LinkPath); return; }
                dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic lnk = sh.CreateShortcut(LinkPath);
                lnk.TargetPath = Application.ExecutablePath;
                lnk.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                lnk.Description = "Radial Convert: Shift + right-click file converter";
                lnk.Save();
            }
            catch (Exception ex) { Log.Write("startup shortcut: " + ex.Message); }
        }
    }
}
