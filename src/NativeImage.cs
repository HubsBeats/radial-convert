using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace RadialConvert
{
    // Still-image writer built on GDI+, so png/jpg/bmp/tiff/pdf need no extra tools.
    // Inputs GDI+ can't read (webp, heic, avif, tga) are decoded by FFmpeg first.
    static class NativeImage
    {
        static readonly string[] GdiReadable = { "jpg", "png", "gif", "bmp", "tiff", "ico" };

        public static void Convert(string input, string output, string target, string ffmpeg)
        {
            using (Bitmap src = Load(input, ffmpeg))
            {
                switch (target)
                {
                    case "png":
                        src.Save(output, ImageFormat.Png);
                        break;
                    case "tiff":
                        src.Save(output, ImageFormat.Tiff);
                        break;
                    case "bmp":
                        using (var flat = Flatten(src)) flat.Save(output, ImageFormat.Bmp);
                        break;
                    case "jpg":
                        using (var flat = Flatten(src)) SaveJpeg(flat, output, 92);
                        break;
                    case "pdf":
                        using (var flat = Flatten(src))
                        using (var ms = new MemoryStream())
                        {
                            SaveJpeg(flat, ms, 94);
                            PdfWriter.WriteImagePage(output, ms.ToArray(), flat.Width, flat.Height);
                        }
                        break;
                    default:
                        throw new Exception("the built-in image writer can't make " + target);
                }
            }
        }

        static Bitmap Load(string input, string ffmpeg)
        {
            string ext = Formats.Norm(Path.GetExtension(input));
            if (Array.IndexOf(GdiReadable, ext) >= 0)
            {
                try { return LoadGdi(input); }
                catch (Exception) { if (ffmpeg == null) throw; }
            }
            if (ffmpeg == null) throw new Exception("reading ." + ext + " needs FFmpeg, which isn't installed");
            string tmp = Path.Combine(Path.GetTempPath(), "radialconvert-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                Converter.RunTool(ffmpeg, "-hide_banner -loglevel error -nostdin -y -i " + Converter.Quote(input) + " -frames:v 1 " + Converter.Quote(tmp));
                return LoadGdi(tmp);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        // Reads the file into memory (so it isn't locked), takes the first frame and applies EXIF rotation.
        static Bitmap LoadGdi(string path)
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            using (var img = Image.FromStream(ms))
            {
                RotateFlipType rot = RotateFlipType.RotateNoneFlipNone;
                try
                {
                    foreach (int id in img.PropertyIdList)
                    {
                        if (id != 0x0112) continue;
                        int o = img.GetPropertyItem(id).Value[0];
                        switch (o)
                        {
                            case 2: rot = RotateFlipType.RotateNoneFlipX; break;
                            case 3: rot = RotateFlipType.Rotate180FlipNone; break;
                            case 4: rot = RotateFlipType.Rotate180FlipX; break;
                            case 5: rot = RotateFlipType.Rotate90FlipX; break;
                            case 6: rot = RotateFlipType.Rotate90FlipNone; break;
                            case 7: rot = RotateFlipType.Rotate270FlipX; break;
                            case 8: rot = RotateFlipType.Rotate270FlipNone; break;
                        }
                    }
                }
                catch { }
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                bmp.SetResolution(96, 96);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                }
                if (rot != RotateFlipType.RotateNoneFlipNone) bmp.RotateFlip(rot);
                return bmp;
            }
        }

        static Bitmap Flatten(Bitmap src)
        {
            var flat = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            flat.SetResolution(96, 96);
            using (var g = Graphics.FromImage(flat))
            {
                g.Clear(Color.White);
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return flat;
        }

        static ImageCodecInfo JpegCodec()
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            return null;
        }

        static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            using (var fs = File.Create(path)) SaveJpeg(bmp, fs, quality);
        }

        static void SaveJpeg(Bitmap bmp, Stream s, long quality)
        {
            var codec = JpegCodec();
            if (codec == null) { bmp.Save(s, ImageFormat.Jpeg); return; }
            using (var ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bmp.Save(s, codec, ep);
            }
        }
    }

    // Minimal one-page PDF holding a single JPEG, sized so the image prints at 96 dpi.
    static class PdfWriter
    {
        public static void WriteImagePage(string path, byte[] jpeg, int w, int h)
        {
            var inv = CultureInfo.InvariantCulture;
            string pw = (w * 0.75).ToString("0.##", inv);
            string ph = (h * 0.75).ToString("0.##", inv);
            string content = "q " + pw + " 0 0 " + ph + " 0 0 cm /Im0 Do Q";

            using (var fs = File.Create(path))
            {
                var offsets = new long[6];
                Action<string> put = s => { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); };
                put("%PDF-1.4\n");
                offsets[1] = fs.Position;
                put("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                offsets[2] = fs.Position;
                put("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
                offsets[3] = fs.Position;
                put("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + pw + " " + ph + "] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");
                offsets[4] = fs.Position;
                put("4 0 obj\n<< /Type /XObject /Subtype /Image /Width " + w + " /Height " + h +
                    " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpeg.Length + " >>\nstream\n");
                fs.Write(jpeg, 0, jpeg.Length);
                put("\nendstream\nendobj\n");
                offsets[5] = fs.Position;
                put("5 0 obj\n<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream\nendobj\n");
                long xref = fs.Position;
                var sb = new StringBuilder();
                sb.Append("xref\n0 6\n0000000000 65535 f \n");
                for (int i = 1; i <= 5; i++) sb.Append(offsets[i].ToString("0000000000", inv) + " 00000 n \n");
                sb.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
                put(sb.ToString());
            }
        }
    }
}
