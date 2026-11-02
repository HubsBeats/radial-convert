using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace RadialConvert
{
    static class Program
    {
        public const string Version = "1.1.0";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0) return Cli.Run(args);
            return RunTray();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int RunTray()
        {
            bool created;
            using (var m = new Mutex(true, "Local\\RadialConvert.Tray", out created))
            {
                if (!created) return 0;
                Native.MakeDpiAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI error: " + e.Exception);
                Application.Run(new TrayApp());
            }
            return 0;
        }
    }

    // Command line, so scripts and other tools can reuse the same converter:
    //   RadialConvert convert --to png [--config settings.json] file...
    //   RadialConvert menu file        (the four formats the radial menu would show)
    //   RadialConvert formats | tools | startup on|off | version
    static class Cli
    {
        public static int Run(string[] args)
        {
            Native.TryAttachConsole();
            string cmd = args[0].TrimStart('-').ToLowerInvariant();
            string configPath = null, target = null;
            var files = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--config" && i + 1 < args.Length) configPath = args[++i];
                else if (args[i] == "--to" && i + 1 < args.Length) target = args[++i];
                else files.Add(args[i]);
            }

            switch (cmd)
            {
                case "version":
                    Console.WriteLine("RadialConvert " + Program.Version);
                    return 0;

                case "tools":
                {
                    string f = Tools.FindFfmpeg(AppConfig.Load(configPath));
                    Console.WriteLine("ffmpeg: " + (f ?? "missing"));
                    return f == null ? 1 : 0;
                }

                case "formats":
                {
                    var cfg = AppConfig.Load(configPath);
                    Console.WriteLine("settings: " + cfg.FilePath);
                    foreach (var c in cfg.Categories)
                        Console.WriteLine(c.Name + ": up=" + c.Slots[0] + " right=" + c.Slots[1] + " down=" + c.Slots[2] + " left=" + c.Slots[3] +
                                          " spare=" + string.Join(",", c.Spare.ToArray()) + " | " + string.Join(" ", c.Extensions.ToArray()));
                    return 0;
                }

                case "menu":
                {
                    var cfg = AppConfig.Load(configPath);
                    int rc = 0;
                    foreach (string f in files)
                    {
                        var c = cfg.CategoryFor(f);
                        if (c == null) { Console.WriteLine(f + ": not supported"); rc = 1; continue; }
                        string[] s = AppConfig.ResolveSlots(c, Path.GetExtension(f));
                        Console.WriteLine(f + ": " + c.Name + " up=" + s[0] + " right=" + s[1] + " down=" + s[2] + " left=" + s[3]);
                    }
                    return rc;
                }

                case "convert":
                {
                    if (target == null || files.Count == 0) { Usage(); return 2; }
                    var cfg = AppConfig.Load(configPath);
                    var conv = new Converter(cfg);
                    int failed = 0;
                    foreach (var r in conv.ConvertMany(files, target, null))
                    {
                        if (r.Ok) Console.WriteLine("ok      " + r.Output);
                        else if (r.Skipped) Console.WriteLine("skipped " + r.Input + " (" + r.Error + ")");
                        else { Console.WriteLine("failed  " + r.Input + ": " + r.Error); failed++; }
                    }
                    return failed > 0 ? 1 : 0;
                }

                case "startup":
                {
                    bool on = files.Count == 0 || files[0].ToLowerInvariant() != "off";
                    Startup.Set(on);
                    Console.WriteLine("start with Windows: " + (Startup.IsOn ? "on" : "off"));
                    return 0;
                }

                case "selftest":
                    return SelfTest.Run();

                case "preview":
                {
                    // Renders the radial menu to a PNG, for docs and visual checks.
                    string outPng = files.Count > 0 ? files[0] : "radial-preview.png";
                    int sel = files.Count > 1 ? int.Parse(files[1]) : 0;
                    using (var bmp = RadialMenu.Draw(600, 2f, new[] { "png", "jpg", "pdf", "gif" }, "JPEG", sel))
                        bmp.Save(outPng, System.Drawing.Imaging.ImageFormat.Png);
                    using (var ic = IconMaker.MakeBitmap(256)) ic.Save(Path.ChangeExtension(outPng, ".icon.png"), System.Drawing.Imaging.ImageFormat.Png);
                    Console.WriteLine("wrote " + outPng);
                    return 0;
                }
            }
            Usage();
            return 2;
        }

        static void Usage()
        {
            Console.WriteLine("RadialConvert " + Program.Version);
            Console.WriteLine("  (no arguments)                   run the Shift + right-click tray app");
            Console.WriteLine("  convert --to <fmt> <files...>    convert files, saved next to the originals");
            Console.WriteLine("  menu <files...>                  show the four radial formats for each file");
            Console.WriteLine("  formats | tools | version        show settings, FFmpeg location, version");
            Console.WriteLine("  startup on|off                   start with Windows");
            Console.WriteLine("  --config <path>                  use another settings.json");
        }
    }

    static class SelfTest
    {
        public static int Run()
        {
            int fails = 0;
            var o = new System.Drawing.Point(100, 100);
            fails += Check(RadialMenu.DirectionFor(o, new System.Drawing.Point(105, 98), 22) == -1, "dead zone");
            fails += Check(RadialMenu.DirectionFor(o, new System.Drawing.Point(110, 40), 22) == 0, "up");
            fails += Check(RadialMenu.DirectionFor(o, new System.Drawing.Point(160, 120), 22) == 1, "right");
            fails += Check(RadialMenu.DirectionFor(o, new System.Drawing.Point(90, 170), 22) == 2, "down");
            fails += Check(RadialMenu.DirectionFor(o, new System.Drawing.Point(30, 110), 22) == 3, "left");

            var cfg = AppConfig.Load(Path.Combine(Path.GetTempPath(), "radialconvert-selftest-" + Guid.NewGuid().ToString("N") + ".json"));
            var img = cfg.CategoryFor("a.JPEG");
            fails += Check(img != null && img.Name == "image", "jpeg is an image");
            string[] s = AppConfig.ResolveSlots(img, ".jpeg");
            fails += Check(s[1] == "webp", "jpg slot swapped for spare on a jpeg, got " + s[1]);
            s = AppConfig.ResolveSlots(img, ".png");
            fails += Check(s[0] == "webp", "png slot swapped on a png, got " + s[0]);
            s = AppConfig.ResolveSlots(cfg.CategoryFor("x.mp3"), ".mp3");
            fails += Check(s[0] == "ogg", "mp3 slot swapped on an mp3, got " + s[0]);
            fails += Check(cfg.CategoryFor("x.mkv").Name == "video", "mkv is a video");
            fails += Check(cfg.CategoryFor("x.txt") == null, "txt unsupported");
            Console.WriteLine(fails == 0 ? "selftest passed" : fails + " selftest failures");
            return fails == 0 ? 0 : 1;
        }

        static int Check(bool ok, string what)
        {
            if (!ok) Console.WriteLine("FAIL: " + what);
            return ok ? 0 : 1;
        }
    }
}
