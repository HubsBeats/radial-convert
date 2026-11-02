using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace RadialConvert
{
    static class Formats
    {
        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "jpeg", "jpg" }, { "jpe", "jpg" }, { "jfif", "jpg" }, { "tif", "tiff" },
            { "aif", "aiff" }, { "mpeg", "mpg" }, { "heif", "heic" }
        };

        public static string Norm(string ext)
        {
            if (ext == null) return "";
            string e = ext.Trim().TrimStart('.').ToLowerInvariant();
            string a;
            return Aliases.TryGetValue(e, out a) ? a : e;
        }

        // Built-in commands. Lookup order: "<sourceext>><target>", "<category>><target>", "*><target>".
        public static readonly Dictionary<string, string> BuiltinRecipes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Images: still formats use the built-in writer, which flattens transparency onto white for jpg/pdf/bmp.
            { "image>png", "native" }, { "image>jpg", "native" }, { "image>bmp", "native" },
            { "image>tiff", "native" }, { "image>pdf", "native" },
            { "image>gif", "ffmpeg -i {in} -vf \"split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a\" -loop 0 {out}" },
            { "image>webp", "ffmpeg -i {in} -frames:v 1 -c:v libwebp -quality 90 {out}" },
            { "gif>webp", "ffmpeg -i {in} -c:v libwebp_anim -quality 85 -loop 0 {out}" },
            { "image>avif", "ffmpeg -i {in} -frames:v 1 -c:v libaom-av1 -still-picture 1 -crf 28 {out}" },
            { "image>mp4", "ffmpeg -i {in} -movflags +faststart -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:v libx264 -crf 20 {out}" },
            { "image>webm", "ffmpeg -i {in} -pix_fmt yuv420p -c:v libvpx-vp9 -crf 32 -b:v 0 {out}" },

            // Audio
            { "*>mp3", "ffmpeg -i {in} -vn -map_metadata 0 -c:a libmp3lame -q:a 0 {out}" },
            { "*>wav", "ffmpeg -i {in} -vn -map_metadata 0 -c:a pcm_s16le {out}" },
            { "flac>wav", "ffmpeg -i {in} -vn -map_metadata 0 -c:a pcm_s24le {out}" },
            { "aiff>wav", "ffmpeg -i {in} -vn -map_metadata 0 -c:a pcm_s24le {out}" },
            { "*>flac", "ffmpeg -i {in} -vn -map_metadata 0 -c:a flac -compression_level 8 {out}" },
            { "*>m4a", "ffmpeg -i {in} -vn -map_metadata 0 -c:a aac -b:a 256k -movflags +faststart {out}" },
            { "*>aac", "ffmpeg -i {in} -vn -c:a aac -b:a 256k {out}" },
            { "*>ogg", "ffmpeg -i {in} -vn -map_metadata 0 -c:a libvorbis -q:a 6 {out}" },
            { "*>opus", "ffmpeg -i {in} -vn -map_metadata 0 -c:a libopus -b:a 160k {out}" },
            { "*>aiff", "ffmpeg -i {in} -vn -map_metadata 0 -c:a pcm_s16be {out}" },

            // Video
            { "video>mp4", "ffmpeg -i {in} -map 0:v:0 -map 0:a? -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:a aac -b:a 192k -movflags +faststart {out}" },
            { "video>mov", "ffmpeg -i {in} -map 0:v:0 -map 0:a? -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" -c:a aac -b:a 192k {out}" },
            { "video>webm", "ffmpeg -i {in} -map 0:v:0 -map 0:a? -c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -deadline good -cpu-used 4 -c:a libopus -b:a 128k {out}" },
            { "video>gif", "ffmpeg -i {in} -vf \"fps=15,scale='min(640,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5\" -loop 0 {out}" },
            { "video>mkv", "ffmpeg -i {in} -map 0 -c copy {out}" },
            { "video>mp3", "ffmpeg -i {in} -vn -map 0:a:0 -c:a libmp3lame -q:a 2 {out}" },
            { "video>png", "ffmpeg -i {in} -frames:v 1 {out}" },
            { "video>jpg", "ffmpeg -i {in} -frames:v 1 -q:v 2 {out}" },
        };
    }

    class ConvertResult
    {
        public string Input;
        public string Output;
        public string Error;
        public bool Skipped;
        public bool Ok { get { return Error == null && !Skipped; } }
    }

    class Converter
    {
        readonly AppConfig cfg;
        public readonly string Ffmpeg;

        public Converter(AppConfig cfg)
        {
            this.cfg = cfg;
            Ffmpeg = Tools.FindFfmpeg(cfg);
        }

        public string RecipeFor(string input, Category cat, string target)
        {
            string src = Formats.Norm(Path.GetExtension(input));
            string t = Formats.Norm(target);
            string catName = cat != null ? cat.Name : "*";
            string[] keys = { src + ">" + t, catName + ">" + t, "*>" + t };
            string r;
            foreach (string k in keys) if (cfg.Recipes.TryGetValue(k, out r)) return r;
            foreach (string k in keys) if (Formats.BuiltinRecipes.TryGetValue(k, out r)) return r;
            return "ffmpeg -i {in} {out}";
        }

        public List<ConvertResult> ConvertMany(IList<string> inputs, string target, Action<int, int> progress)
        {
            var results = new ConvertResult[inputs.Count];
            int parallel = cfg.MaxParallel > 0 ? cfg.MaxParallel : Math.Max(2, Environment.ProcessorCount / 4);
            parallel = Math.Min(parallel, inputs.Count);
            int next = -1, done = 0;
            var threads = new List<Thread>();
            for (int w = 0; w < parallel; w++)
            {
                var th = new Thread(() =>
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= inputs.Count) break;
                        results[i] = Convert(inputs[i], target);
                        int d = Interlocked.Increment(ref done);
                        if (progress != null) progress(d, inputs.Count);
                    }
                });
                th.IsBackground = true;
                th.Start();
                threads.Add(th);
            }
            foreach (var th in threads) th.Join();
            return new List<ConvertResult>(results);
        }

        public ConvertResult Convert(string input, string target)
        {
            var res = new ConvertResult();
            res.Input = input;
            string tmp = null;
            try
            {
                if (!File.Exists(input)) { res.Error = "file not found"; return res; }
                string t = Formats.Norm(target);
                string ext = target.Trim().TrimStart('.').ToLowerInvariant();
                if (Formats.Norm(Path.GetExtension(input)) == t) { res.Skipped = true; res.Error = "already " + ext.ToUpperInvariant(); return res; }

                Category cat = cfg.CategoryFor(input);
                string recipe = RecipeFor(input, cat, t);

                string dir = Path.GetDirectoryName(Path.GetFullPath(input));
                if (!string.IsNullOrEmpty(cfg.OutputFolder))
                    dir = Path.IsPathRooted(cfg.OutputFolder) ? cfg.OutputFolder : Path.Combine(dir, cfg.OutputFolder);
                Directory.CreateDirectory(dir);
                string baseName = Path.GetFileNameWithoutExtension(input);
                tmp = Path.Combine(dir, baseName + ".converting-" + Guid.NewGuid().ToString("N").Substring(0, 6) + "." + ext);

                if (recipe.Trim().Equals("native", StringComparison.OrdinalIgnoreCase))
                    NativeImage.Convert(input, tmp, t, Ffmpeg);
                else
                    RunFfmpeg(recipe, input, tmp);

                if (!File.Exists(tmp) || new FileInfo(tmp).Length == 0) throw new Exception("converter produced no file");
                string outPath = UniquePath(dir, baseName, ext);
                File.Move(tmp, outPath);
                tmp = null;
                res.Output = outPath;
                Log.Write("OK   " + input + " -> " + outPath);
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                Log.Write("FAIL " + input + " -> " + target + ": " + ex.Message);
            }
            finally
            {
                if (tmp != null) { try { File.Delete(tmp); } catch { } }
            }
            return res;
        }

        public static string UniquePath(string dir, string baseName, string ext)
        {
            string p = Path.Combine(dir, baseName + "." + ext);
            int n = 1;
            while (File.Exists(p)) { p = Path.Combine(dir, baseName + " (" + n + ")." + ext); n++; }
            return p;
        }

        public static string Quote(string s) { return "\"" + s + "\""; }

        void RunFfmpeg(string recipe, string input, string output)
        {
            if (Ffmpeg == null) throw new Exception("FFmpeg isn't installed. Run the installer again or set \"ffmpeg\" in settings.json.");
            string args = recipe.Trim();
            if (args.StartsWith("ffmpeg ", StringComparison.OrdinalIgnoreCase)) args = args.Substring(7);
            args = args.Replace("{in}", Quote(input)).Replace("{out}", Quote(output));
            RunTool(Ffmpeg, "-hide_banner -loglevel error -nostdin -y " + args);
        }

        public static void RunTool(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            var err = new StringBuilder();
            using (var p = new Process())
            {
                p.StartInfo = psi;
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.OutputDataReceived += (s, e) => { };
                p.Start();
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    string msg = err.ToString().Trim();
                    if (msg.Length > 400) msg = msg.Substring(msg.Length - 400);
                    throw new Exception(msg.Length > 0 ? msg : "ffmpeg exited with code " + p.ExitCode);
                }
            }
        }
    }

    static class Tools
    {
        public static string AppDir { get { return AppDomain.CurrentDomain.BaseDirectory; } }

        public static string FindFfmpeg(AppConfig cfg)
        {
            var c = new List<string>();
            if (cfg != null && !string.IsNullOrEmpty(cfg.Ffmpeg)) c.Add(Environment.ExpandEnvironmentVariables(cfg.Ffmpeg));
            c.Add(Path.Combine(AppDir, "tools", "ffmpeg.exe"));
            c.Add(Path.Combine(AppDir, "ffmpeg.exe"));
            c.Add(Path.Combine(AppConfig.HomeDir, "tools", "ffmpeg.exe"));

            // PATH as it is now in the registry, not as it was when this process started,
            // so a fresh winget install is found without logging out.
            var dirs = new List<string>();
            foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
            {
                string p = null;
                try { p = Environment.GetEnvironmentVariable("PATH", target); } catch { }
                if (p != null) dirs.AddRange(p.Split(Path.PathSeparator));
            }
            foreach (string d in dirs)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                try
                {
                    string dd = Environment.ExpandEnvironmentVariables(d.Trim().Trim('"'));
                    c.Add(Path.Combine(dd, "ffmpeg.exe"));
                    c.Add(Path.Combine(dd, "ffmpeg"));
                }
                catch { }
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                c.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
                try
                {
                    string pk = Path.Combine(local, "Microsoft", "WinGet", "Packages");
                    if (Directory.Exists(pk))
                        foreach (string d in Directory.GetDirectories(pk, "*FFmpeg*"))
                            foreach (string f in Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories)) c.Add(f);
                }
                catch { }
            }
            c.Add(@"C:\ffmpeg\bin\ffmpeg.exe");
            c.Add(@"C:\ProgramData\chocolatey\bin\ffmpeg.exe");

            // WinGet's Links\ffmpeg.exe is a symlink some launchers can't run, so prefer real files.
            foreach (string f in c)
            {
                try { if (File.Exists(f) && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0) return Path.GetFullPath(f); } catch { }
            }
            foreach (string f in c)
            {
                try { if (File.Exists(f) && CanOpen(f)) return Path.GetFullPath(f); } catch { }
            }
            return null;
        }

        // File.Exists is true for a symlink whose target was removed (e.g. an uninstalled
        // winget package), so make sure the file really opens.
        static bool CanOpen(string f)
        {
            try { using (File.OpenRead(f)) { } return true; }
            catch { return false; }
        }
    }

    static class Log
    {
        static readonly object Sync = new object();
        public static void Write(string line)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(AppConfig.HomeDir);
                    string path = Path.Combine(AppConfig.HomeDir, "log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
