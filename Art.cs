using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IGM
{
    public static class IconCodec
    {
        static Bitmap Preview(string png)
        {
            using (var stream = new MemoryStream(Convert.FromBase64String(png)))
            using (var image = new Bitmap(stream))
                return new Bitmap(image);
        }

        public static byte[] ExportTexture(Alliance a)
        {
            using (var source = a.PreviewPng.Length > 0 ? Preview(a.PreviewPng) : Decode(a.Icon, 180))
            using (var target = new Bitmap(180, 180, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(target))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(source, new Rectangle(0, 0, 180, 180));
                }

                using (var stream = new MemoryStream())
                using (var b = new BinaryWriter(stream))
                {
                    b.Write((byte)'B');
                    b.Write((byte)'M');
                    b.Write(54 + 180 * 180 * 4);
                    b.Write(0);
                    b.Write(54);
                    b.Write(40);
                    b.Write(180);
                    b.Write(-180);
                    b.Write((short)1);
                    b.Write((short)32);
                    b.Write(0);
                    b.Write(180 * 180 * 4);
                    b.Write(0);
                    b.Write(0);
                    b.Write(0);
                    b.Write(0);
                    for (int y = 0; y < 180; y++)
                        for (int x = 0; x < 180; x++)
                        {
                            Color c = target.GetPixel(x, y);
                            b.Write(c.B);
                            b.Write(c.G);
                            b.Write(c.R);
                            b.Write(c.A);
                        }

                    return stream.ToArray();
                }
            }
        }

        public static void Validate(string text)
        {
            string[] rows = text.Trim().Split('\n');
            string[] h = rows[0].Trim().Split(' ');
            int count;
            int dim = h.Length == 3 && h[0] == "IGMICON2" && h[2] == "96" ? 96 : h.Length == 2 && h[0] == "IGMICON1" ? 24 : 0;
            if (dim == 0 || !int.TryParse(h[1], out count) || count < 0 || count > dim * dim || rows.Length != count + 1)
                throw new InvalidDataException("Icon header");
            for (int i = 1; i < rows.Length; i++)
            {
                string[] p = rows[i].Trim().Split(' ');
                int x, y, n;
                uint c;
                if (p.Length != 4 || !int.TryParse(p[0], out x) || !int.TryParse(p[1], out y) || !int.TryParse(p[2], out n) || !uint.TryParse(p[3], System.Globalization.NumberStyles.HexNumber, null, out c) || x < 0 || x >= dim || y < 0 || y >= dim || n < 1 || x + n > dim)
                    throw new InvalidDataException("Icon run");
            }
        }

        public static Bitmap Decode(string text, int size)
        {
            Validate(text);
            float dim = text.StartsWith("IGMICON2 ") ? 96f : 24f;
            Bitmap b = new Bitmap(size, size);
            using (var g = Graphics.FromImage(b))
            {
                foreach (string row in text.Trim().Split('\n').Skip(1))
                {
                    string[] p = row.Trim().Split(' ');
                    uint c = Convert.ToUInt32(p[3], 16);
                    using (var brush = new SolidBrush(Color.FromArgb((int)(c & 255), (int)(c >> 24), (int)((c >> 16) & 255), (int)((c >> 8) & 255))))
                        g.FillRectangle(brush, int.Parse(p[0]) * size / dim, int.Parse(p[1]) * size / dim, int.Parse(p[2]) * size / dim, size / dim + .01f);
                }
            }

            return b;
        }
    }

    public static class ImperialCore
    {
        public const string ID = "275889132_1121173938_1505499062_4230148680";
        public static bool IsAlliance(string id)
        {
            return id == ID;
        }

        public static void Apply(Project p)
        {
            var a = p.Alliances.Find(x => IsAlliance(x.ID));
            if (a == null)
                return;
            p.Alliances.Remove(a);
            p.Alliances.Insert(0, a);
            a.Name = "Imperio Español";
            a.Initials = "IESP";
            a.ColorRGB = Color.White.ToArgb();
            a.NameEdited = false;
            a.Icon = a.PreviewPng = a.ImagePng = a.SymbolPath = "";
            a.Opacity = 100;
            a.UseDiamond = true;
        }

        public static void DrawBadge(Graphics g, Rectangle r)
        {
            var save = g.Save();
            g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            g.ScaleTransform(r.Width / 26.4f, r.Height / 26.4f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(64, 64, 64)))
                g.FillPolygon(b, new[] { new PointF(0, -13.2f), new PointF(13.2f, 0), new PointF(0, 13.2f), new PointF(-13.2f, 0) });
            using (var b = new SolidBrush(Color.White))
                g.FillPolygon(b, new[] { new PointF(0, -11.6f), new PointF(11.6f, 0), new PointF(0, 11.6f), new PointF(-11.6f, 0) });
            using (var b = new SolidBrush(Color.FromArgb(196, 20, 43)))
            {
                for (int diagonal = -1; diagonal <= 1; diagonal += 2)
                {
                    Plank(g, b, -5.4f, -5.4f * diagonal, 5.4f, 5.4f * diagonal, 2.3f);
                    for (int t = -3; t <= 3; t += 3)
                        for (int side = -1; side <= 1; side += 2)
                            Plank(g, b, t, t * diagonal, t + side * 1.8f, t * diagonal - side * diagonal * 1.8f, 1.4f);
                }
            }

            g.Restore(save);
        }

        static void Plank(Graphics g, Brush b, float x1, float y1, float x2, float y2, float width)
        {
            float dx = x2 - x1, dy = y2 - y1, len = (float)Math.Sqrt(dx * dx + dy * dy), nx = -dy / len * width / 2, ny = dx / len * width / 2;
            var points = new[]
            {
                new PointF(x1 + nx, y1 + ny),
                new PointF(x2 + nx, y2 + ny),
                new PointF(x2 - nx, y2 - ny),
                new PointF(x1 - nx, y1 - ny)
            };
            for (int i = 0; i < points.Length; i++)
            {
                float span = Math.Abs(points[i].X) + Math.Abs(points[i].Y);
                if (span > 10.9f)
                {
                    points[i].X *= 10.9f / span;
                    points[i].Y *= 10.9f / span;
                }
            }

            g.FillPolygon(b, points);
        }
    }

    // Standard .NET data resources; no runtime extraction or executable payload.
    public static class Assets
    {
        static readonly Dictionary<string, byte[]> data = Load();
        static Dictionary<string, byte[]> Load()
        {
            var result = new Dictionary<string, byte[]>();
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("IGM.Assets"))
            using (var reader = new System.Resources.ResourceReader(stream))
                foreach (System.Collections.DictionaryEntry entry in reader)
                    result.Add((string)entry.Key, (byte[])entry.Value);
            return result;
        }

        public static string[] Symbols
        {
            get
            {
                return data.Keys.Where(x => x.StartsWith("Symbols.")).Select(x => x.Substring(8)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            }
        }

        public static bool HasSymbol(string file)
        {
            return data.ContainsKey("Symbols." + Path.GetFileName(file));
        }

        public static Stream Open(string name)
        {
            byte[] bytes;
            if (!data.TryGetValue(name, out bytes))
                throw new InvalidDataException("Missing resource: " + name);
            return new MemoryStream(bytes, false);
        }

        public static string Text(string name)
        {
            using (var stream = Open(name))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        public static Image Image(string name)
        {
            using (var stream = Open(name))
            using (var image = System.Drawing.Image.FromStream(stream))
                return new Bitmap(image);
        }
    }
}
