using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace RadialConvert
{
    // One file family (image, audio, video) and the four formats on its radial menu.
    class Category
    {
        public string Name;
        public List<string> Extensions = new List<string>();
        public string[] Slots = new string[4];          // 0 up, 1 right, 2 down, 3 left
        public List<string> Spare = new List<string>(); // used when a slot matches the source format
    }

    class AppConfig
    {
        public static readonly string[] SlotNames = { "up", "right", "down", "left" };

        public List<Category> Categories = new List<Category>();
        public Dictionary<string, string> Recipes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Ffmpeg = "";
        public string OutputFolder = "";
        public int DeadZone = 22;
        public int MaxParallel = 0;
        public string FilePath;
        public DateTime LoadedStamp;

        public static string HomeDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("RADIALCONVERT_HOME");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadialConvert");
            }
        }

        public static string DefaultPath { get { return Path.Combine(HomeDir, "settings.json"); } }

        public static AppConfig Load(string path = null)
        {
            if (string.IsNullOrEmpty(path)) path = DefaultPath;
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(path, DefaultJson);
            }
            AppConfig cfg;
            try
            {
                cfg = Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Write("settings.json could not be read, using defaults: " + ex.Message);
                cfg = Parse(DefaultJson);
            }
            cfg.FilePath = path;
            cfg.LoadedStamp = File.GetLastWriteTimeUtc(path);
            return cfg;
        }

        public bool IsStale()
        {
            try { return FilePath != null && File.GetLastWriteTimeUtc(FilePath) != LoadedStamp; }
            catch { return false; }
        }

        static AppConfig Parse(string json)
        {
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("top level must be an object");
            var cfg = new AppConfig();
            cfg.Ffmpeg = Str(root, "ffmpeg");
            cfg.OutputFolder = Str(root, "outputFolder");
            cfg.DeadZone = Int(root, "deadZone", 22);
            cfg.MaxParallel = Int(root, "maxParallel", 0);

            object cats;
            if (root.TryGetValue("categories", out cats) && cats is Dictionary<string, object>)
            {
                foreach (var kv in (Dictionary<string, object>)cats)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    if (d == null) continue;
                    var c = new Category();
                    c.Name = kv.Key.ToLowerInvariant();
                    foreach (string e in StrList(d, "extensions")) c.Extensions.Add(Formats.Norm(e));
                    for (int i = 0; i < 4; i++) c.Slots[i] = Str(d, SlotNames[i]).ToLowerInvariant().TrimStart('.');
                    foreach (string s in StrList(d, "spare")) c.Spare.Add(s.ToLowerInvariant().TrimStart('.'));
                    cfg.Categories.Add(c);
                }
            }

            object rec;
            if (root.TryGetValue("recipes", out rec) && rec is Dictionary<string, object>)
            {
                foreach (var kv in (Dictionary<string, object>)rec)
                {
                    if (kv.Key.StartsWith("_")) continue;
                    string v = kv.Value as string;
                    if (!string.IsNullOrEmpty(v)) cfg.Recipes[kv.Key.ToLowerInvariant()] = v;
                }
            }
            return cfg;
        }

        public Category CategoryFor(string path)
        {
            string ext = Formats.Norm(Path.GetExtension(path));
            foreach (var c in Categories)
                if (c.Extensions.Contains(ext)) return c;
            return null;
        }

        // The four formats to show for a file of this source format. A slot that matches the
        // source (jpg on a jpg) is swapped for the first spare format that isn't already shown.
        public static string[] ResolveSlots(Category cat, string sourceExt)
        {
            string src = Formats.Norm(sourceExt);
            var slots = (string[])cat.Slots.Clone();
            for (int i = 0; i < 4; i++)
            {
                if (Formats.Norm(slots[i]) != src) continue;
                foreach (string s in cat.Spare)
                {
                    string n = Formats.Norm(s);
                    if (n == src) continue;
                    bool taken = false;
                    foreach (string t in slots) if (Formats.Norm(t) == n) taken = true;
                    if (taken) continue;
                    slots[i] = s;
                    break;
                }
            }
            return slots;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return "";
        }

        static int Int(Dictionary<string, object> d, string key, int def)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return def;
        }

        static List<string> StrList(Dictionary<string, object> d, string key)
        {
            var list = new List<string>();
            object v;
            if (d.TryGetValue(key, out v) && v is object[])
                foreach (object o in (object[])v) if (o != null) list.Add(Convert.ToString(o));
            return list;
        }

        public const string DefaultJson = @"{
  ""_help"": ""Radial Convert settings. Each category lists the file extensions it covers and the format for each flick direction (up, right, down, left). If a file is already in a slot's format, the first 'spare' format not already shown takes its place. Save this file and the next Shift + right-click picks up the change. Any format FFmpeg can write works in a slot."",
  ""ffmpeg"": """",
  ""outputFolder"": """",
  ""deadZone"": 22,
  ""maxParallel"": 0,
  ""categories"": {
    ""image"": {
      ""extensions"": [""jpg"", ""jpeg"", ""jfif"", ""png"", ""gif"", ""webp"", ""bmp"", ""tif"", ""tiff"", ""heic"", ""heif"", ""avif"", ""tga"", ""ico""],
      ""up"": ""png"", ""right"": ""jpg"", ""down"": ""pdf"", ""left"": ""gif"",
      ""spare"": [""webp"", ""png"", ""jpg""]
    },
    ""audio"": {
      ""extensions"": [""mp3"", ""wav"", ""flac"", ""m4a"", ""aac"", ""ogg"", ""opus"", ""wma"", ""aiff"", ""aif""],
      ""up"": ""mp3"", ""right"": ""wav"", ""down"": ""flac"", ""left"": ""m4a"",
      ""spare"": [""ogg"", ""mp3"", ""wav""]
    },
    ""video"": {
      ""extensions"": [""mp4"", ""mov"", ""mkv"", ""avi"", ""webm"", ""wmv"", ""flv"", ""m4v"", ""mpg"", ""mpeg"", ""3gp"", ""ts""],
      ""up"": ""mp4"", ""right"": ""webm"", ""down"": ""gif"", ""left"": ""mp3"",
      ""spare"": [""mov"", ""mkv"", ""mp4""]
    }
  },
  ""recipes"": {
    ""_help"": ""Optional custom commands, keyed 'category>format' or 'sourceext>format'. {in} and {out} are filled in. Example: \""video>mp4\"": \""ffmpeg -i {in} -c:v libx264 -crf 18 {out}\"". Use the word native for the built-in image writer (png, jpg, bmp, tiff, pdf).""
  }
}
";
    }
}
