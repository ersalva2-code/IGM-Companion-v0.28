using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Reflection;

namespace IGM
{
    public class Hit
    {
        public string ID;
        public Rectangle R;
        public Action Run;
    }

    public class ModernCanvas : UserControl
    {
        readonly Color bg = Color.FromArgb(8, 13, 23), surface = Color.FromArgb(15, 23, 39), raised = Color.FromArgb(21, 33, 52), line = Color.FromArgb(35, 49, 69), text = Color.FromArgb(230, 237, 246), muted = Color.FromArgb(139, 157, 181), accent = Color.FromArgb(255, 163, 66), cyan = Color.FromArgb(74, 200, 191);
        Project project = new Project();
        List<Hit> hits = new List<Hit>();
        Dictionary<string, Image> images = new Dictionary<string, Image>();
        Dictionary<string, string[]> words;
        List<Alliance> styles = new List<Alliance>();
        string[] symbols;
        string hover = "", folder = "", notice = "", source = "", myAlliance = "";
        int language, tab, selected = -1, page, symbolPage, alliancePage, symbolPageSize = 24;
        SystemRecord activeSystem;
        TextBox nameField = new TextBox(), initialField = new TextBox();
        bool editing;
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImperialGalacticMap");
        Image logo, atlas;
        public ModernCanvas()
        {
            ImperialCore.Apply(project);
            selected = 0;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Font = new Font("Segoe UI", 10);
            BackColor = bg;
            words = Data.Json().Deserialize<Dictionary<string, string[]>>(Assets.Text("Languages.json"));
            symbols = Assets.Symbols;
            logo = Assets.Image("Logo");
            using (var s = Assets.Open("Glyphs"))
                atlas = new Bitmap(s);
            try
            {
                styles = Data.Json().Deserialize<List<Alliance>>(File.ReadAllText(Path.Combine(dataDir, "styles_v015.json"))) ?? styles;
            }
            catch
            {
            }

            language = 1;
            try
            {
                language = Data.Num(Data.Get(Data.Json().DeserializeObject(File.ReadAllText(Path.Combine(dataDir, "companion.json"))), "language"));
                if (language < 0 || language > 5)
                    language = 1;
            }
            catch
            {
            }

            foreach (var f in new[]
            {
                nameField,
                initialField
            }

            )
            {
                f.Font = new Font("Segoe UI", 11);
                f.BackColor = raised;
                f.ForeColor = text;
                f.BorderStyle = BorderStyle.FixedSingle;
                Controls.Add(f);
                f.Visible = false;
            }

            initialField.MaxLength = 6;
            initialField.CharacterCasing = CharacterCasing.Upper;
            nameField.TextChanged += (s, e) =>
            {
                if (!editing)
                    return;
                var a = Current;
                if (a != null && !ImperialCore.IsAlliance(a.ID))
                {
                    a.Name = nameField.Text;
                    a.NameEdited = true;
                    SaveStyles();
                }
            };
            initialField.TextChanged += (s, e) =>
            {
                if (!editing)
                    return;
                var a = Current;
                if (a != null && !ImperialCore.IsAlliance(a.ID))
                {
                    a.Initials = initialField.Text;
                    UpdateArt(a);
                }
            };
            MouseMove += (s, e) =>
            {
                var h = hits.LastOrDefault(x => x.R.Contains(e.Location));
                string id = h == null ? "" : h.ID;
                if (id != hover)
                {
                    hover = id;
                    Cursor = h == null ? Cursors.Default : Cursors.Hand;
                    Invalidate();
                }
            };
            MouseLeave += (s, e) =>
            {
                hover = "";
                Invalidate();
            };
            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left)
                    return;
                var h = hits.LastOrDefault(x => x.R.Contains(e.Location));
                if (h != null)
                    Guard(h.Run);
            };
            MouseWheel += (s, e) =>
            {
                if (tab == 1)
                {
                    symbolPage = Math.Max(0, Math.Min((Math.Max(0, symbols.Length - 1)) / symbolPageSize, symbolPage + (e.Delta < 0 ? 1 : -1)));
                }
                else
                    page = Math.Max(0, page + (e.Delta < 0 ? 1 : -1));
                Invalidate();
            };
        }

        string Base
        {
            get
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        Alliance Current
        {
            get
            {
                return selected >= 0 && selected < project.Alliances.Count ? project.Alliances[selected] : null;
            }
        }

        string T(string key)
        {
            string[] a;
            return words.TryGetValue(key, out a) ? a[language] : key;
        }

        string U(string es, string en)
        {
            string[] v;
            return words.TryGetValue("ui15:" + en, out v) ? v[language] : language == 0 ? es : en;
        }

        void Guard(Action a)
        {
            try
            {
                a();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), T("error") + "\n" + ex.Message, "Imperial Galactic Map", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Invalidate();
        }

        GraphicsPath Rounded(Rectangle r, int radius = 10)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Box(Graphics g, Rectangle r, Color fill, Color stroke, int radius = 10)
        {
            if (r.Width < 2 || r.Height < 2)
                return;
            using (var p = Rounded(r, radius))
            {
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, p);
                using (var pen = new Pen(stroke))
                    g.DrawPath(pen, p);
            }
        }

        void TextAt(Graphics g, string value, float x, float y, float width, float height = 25, float size = 10, Color? color = null, bool bold = false)
        {
            using (var f = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular))
            using (var b = new SolidBrush(color ?? text))
            using (var sf = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            }

            )
                g.DrawString(value, f, b, new RectangleF(x, y, width, height), sf);
        }

        void ActionButton(Graphics g, string id, string label, Rectangle r, Action action, bool primary = false, string icon = "")
        {
            bool over = hover == id;
            Color fill = primary ? (over ? Color.FromArgb(255, 184, 94) : accent) : (over ? Color.FromArgb(36, 54, 78) : raised);
            Box(g, r, fill, over ? accent : line, 8);
            if (over && !primary)
            {
                using (var p = new Pen(Color.FromArgb(70, 255, 170, 70), 3))
                    g.DrawLine(p, r.Left + 8, r.Bottom - 2, r.Right - 8, r.Bottom - 2);
            }

            Color ink = primary ? bg : text;
            float x = r.X + 12;
            if (icon.Length > 0)
            {
                DrawIcon(g, icon, new Rectangle(r.X + 12, r.Y + 12, 17, 17), primary ? bg : cyan);
                x += 26;
            }

            TextAt(g, label, x, r.Y + 10, r.Right - x - 8, r.Height - 10, 9.5f, ink, true);
            hits.Add(new Hit { ID = id, R = r, Run = action });
        }

        void DrawIcon(Graphics g, string kind, Rectangle r, Color c)
        {
            using (var p = new Pen(c, 1.8f))
            {
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                if (kind == "load")
                {
                    g.DrawLines(p, new[] { new PointF(x, y + h), new PointF(x, y + 3), new PointF(x + 6, y + 3), new PointF(x + 9, y + 6), new PointF(x + w, y + 6), new PointF(x + w, y + h), new PointF(x, y + h) });
                }
                else if (kind == "export")
                {
                    g.DrawLine(p, x + w / 2, y + 1, x + w / 2, y + h - 3);
                    g.DrawLine(p, x + w / 2, y + 1, x + 3, y + 6);
                    g.DrawLine(p, x + w / 2, y + 1, x + w - 3, y + 6);
                    g.DrawLine(p, x, y + h, x + w, y + h);
                }
                else
                {
                    g.DrawPolygon(p, new[] { new PointF(x + w / 2, y), new PointF(x + w, y + h / 2), new PointF(x + w / 2, y + h), new PointF(x, y + h / 2) });
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            hits.Clear();
            int side = 260, top = 78, bottom = Height - 58;
            using (var b = new SolidBrush(Color.FromArgb(10, 17, 29)))
                g.FillRectangle(b, 0, 0, side, Height);
            using (var p = new Pen(line))
            {
                g.DrawLine(p, side, 0, side, Height);
                g.DrawLine(p, 0, top, Width, top);
                g.DrawLine(p, side, bottom, Width, bottom);
            }

            g.DrawImage(logo, 20, 21, 37, 37);
            TextAt(g, "IMPERIAL", 69, 22, 175, 22, 12, text, true);
            TextAt(g, "GALACTIC MAP", 69, 43, 175, 20, 8, muted, true);
            TextAt(g, "COMPANION  /  0.28", side + 30, 27, 250, 25, 11, muted, true);
            ActionButton(g, "load", T("load_save"), new Rectangle(Width - 430, 18, 178, 42), LoadSave, false, "load");
            ActionButton(g, "export", T("export"), new Rectangle(Width - 238, 18, 216, 42), Export, true, "export");
            TextAt(g, U("ESPACIO DE TRABAJO", "WORKSPACE"), 22, 108, 220, 20, 8, muted, true);
            Nav(g, 0, T("catalogue"), 145);
            Nav(g, 1, T("icon_designer"), 200);
            TextAt(g, U("ALIANZAS DETECTADAS", "OBSERVED ALLIANCES"), 22, 283, 220, 20, 8, muted, true);
            int available = Math.Max(1, (Height - 420) / 54);
            for (int i = alliancePage * available; i < project.Alliances.Count && i < (alliancePage + 1) * available; i++)
            {
                int k = i;
                Alliance a = project.Alliances[i];
                Rectangle r = new Rectangle(14, 315 + (i - alliancePage * available) * 54, 232, 46);
                Box(g, r, selected == i ? raised : Color.FromArgb(10, 17, 29), selected == i ? line : Color.FromArgb(10, 17, 29), 8);
                DrawBadge(g, a, new Rectangle(25, r.Y + 8, 30, 30));
                TextAt(g, Title(a, i), 69, r.Y + 6, 166, 22, 9.5f, selected == i ? text : muted, true);
                TextAt(g, project.Systems.Count(x => x.Alliance == a.ID) + " " + U("sistemas", "systems"), 69, r.Y + 27, 150, 17, 8, muted);
                hits.Add(new Hit { ID = "ally" + i, R = r, Run = () => Select(k) });
            }

            if (project.Alliances.Count > available)
            {
                ActionButton(g, "aprev", "‹", new Rectangle(20, Height - 115, 60, 34), () => alliancePage = Math.Max(0, alliancePage - 1));
                ActionButton(g, "anext", "›", new Rectangle(185, Height - 115, 60, 34), () =>
                {
                    if ((alliancePage + 1) * available < project.Alliances.Count)
                        alliancePage++;
                });
            }

            if (project.Systems.Count > 0)
            {
                Rectangle ur = new Rectangle(14, 315 + Math.Min(project.Alliances.Count, available) * 54, 232, 44);
                Box(g, ur, selected < 0 ? raised : Color.FromArgb(10, 17, 29), line, 8);
                TextAt(g, T("unconfirmed"), ur.X + 12, ur.Y + 13, 208, 23, 9, muted);
                hits.Add(new Hit { ID = "unconfirmed", R = ur, Run = () =>
                {
                    selected = -1;
                    tab = 0;
                    page = 0;
                    LayoutInputs();
                } });
            }

            ActionButton(g, "language", new[] { "Español", "English", "Français", "Deutsch", "Italiano", "Português" }[language], new Rectangle(20, Height - 66, 220, 40), Languages);
            if (project.Systems.Count == 0)
            {
                Empty(g, side, top, bottom);
            }
            else if (tab == 0)
            {
                Catalogue(g, side + 30, top + 25, Width - side - 60, bottom - top - 45);
            }
            else
                Editor(g, side + 30, top + 25, Width - side - 60, bottom - top - 45);
            TextAt(g, notice.Length > 0 ? notice : U("Solo lectura del save · Exportación por fusión · Archivos originales intactos", "Read-only save import · Merge export · Original files untouched"), side + 26, bottom + 19, Width - side - 45, 22, 9, muted);
        }

        string Title(Alliance a, int i)
        {
            return a.Name.Length > 0 ? a.Display : T("alliance_pending") + " " + (i + 1);
        }

        void Nav(Graphics g, int target, string title, int y)
        {
            Rectangle r = new Rectangle(14, y, 232, 44);
            bool active = tab == target;
            Box(g, r, active ? raised : hover == "nav" + target ? surface : Color.FromArgb(10, 17, 29), active ? line : Color.FromArgb(10, 17, 29), 8);
            DrawIcon(g, "diamond", new Rectangle(27, y + 13, 17, 17), active ? accent : muted);
            TextAt(g, title, 58, y + 13, 180, 23, 9.5f, active ? text : muted, true);
            hits.Add(new Hit { ID = "nav" + target, R = r, Run = () =>
            {
                tab = target;
                BindFields();
            } });
        }

        void Empty(Graphics g, int side, int top, int bottom)
        {
            int x = side + 50, y = top + 85;
            TextAt(g, U("Tu galaxia, organizada.", "Your galaxy, organised."), x, y, Width - x - 40, 65, 27, text, true);
            TextAt(g, U("Carga tu partida para explorar alianzas y diseñar sus emblemas.", "Load your save to explore alliances and design their emblems."), x, y + 82, Width - x - 50, 40, 12, muted);
            ActionButton(g, "start", T("load_save"), new Rectangle(x, y + 151, 240, 46), LoadSave, true, "load");
            Box(g, new Rectangle(x, y + 240, Width - x - 50, 125), surface, line);
            TextAt(g, U("Un catálogo local. Tus propias insignias.", "A local catalogue. Your own insignias."), x + 24, y + 264, Width - x - 100, 30, 15, text, true);
            TextAt(g, U("Símbolos seleccionables · Colores por alianza · Glifos de acceso", "Selectable symbols · Alliance colours · Portal glyphs"), x + 24, y + 309, Width - x - 100, 25, 10, muted);
        }

        void Catalogue(Graphics g, int x, int y, int w, int h)
        {
            TextAt(g, U("Catálogo de alianzas", "Alliance catalogue"), x, y, w, 40, 23, text, true);
            TextAt(g, source, x, y + 45, w, 23, 10, muted);
            int statY = y + 86, statW = (w - 32) / 3;
            string[] vals =
            {
                project.Systems.Count.ToString(),
                project.Alliances.Count.ToString(),
                project.Systems.Count(z => z.Owned).ToString()
            };
            string[] labels =
            {
                U("SISTEMAS CONOCIDOS", "KNOWN SYSTEMS"),
                U("ALIANZAS", "ALLIANCES"),
                U("ESTACIONES PROPIAS", "OWNED STATIONS")
            };
            for (int i = 0; i < 3; i++)
            {
                int sx = x + i * (statW + 16);
                Box(g, new Rectangle(sx, statY, statW, 78), surface, line);
                TextAt(g, labels[i], sx + 18, statY + 12, statW - 30, 18, 8, muted, true);
                TextAt(g, vals[i], sx + 18, statY + 32, statW - 30, 35, 23, i == 1 ? accent : text, true);
            }

            Alliance a = Current;
            int body = statY + 100;
            TextAt(g, a == null ? T("unconfirmed") : Title(a, selected), x, body, w - 200, 30, 14, text, true);
            if (a != null && !ImperialCore.IsAlliance(a.ID))
                ActionButton(g, "edit", U("Editar emblema", "Edit emblem"), new Rectangle(x + w - 180, body - 5, 180, 40), () =>
                {
                    tab = 1;
                    BindFields();
                }, false, "diamond");
            if (a != null && ImperialCore.IsAlliance(a.ID))
                TextAt(g, U("OFICIAL · PROTEGIDO", "OFFICIAL · PROTECTED"), x + w - 200, body + 5, 200, 26, 9, accent, true);
            var records = project.Systems.Where(z => a == null ? z.Alliance.Length == 0 : z.Alliance == a.ID).OrderBy(z => z.UA == "00004400a37323b6" ? 0 : 1).ToArray();
            int count = Math.Max(1, (h - 325) / 80);
            if (page * count >= records.Length)
                page = 0;
            for (int i = 0; i < count && i + page * count < records.Length; i++)
            {
                SystemRecord r = records[i + page * count];
                int ry = body + 52 + i * 80;
                Rectangle row = new Rectangle(x, ry, w, 70);
                Box(g, row, hover == "system" + r.UA ? raised : surface, activeSystem == r ? accent : line);
                Rectangle check = new Rectangle(x + 16, ry + 24, 19, 19);
                Box(g, check, r.Selected ? accent : raised, r.Selected ? accent : line, 4);
                if (r.Selected)
                {
                    using (var pen = new Pen(bg, 2))
                        g.DrawLines(pen, new[] { new Point(x + 20, ry + 33), new Point(x + 24, ry + 37), new Point(x + 31, ry + 28) });
                }

                hits.Add(new Hit { ID = "check" + r.UA, R = new Rectangle(x, ry, 45, 70), Run = () =>
                {
                    if (r.Alliance.Length > 0 && !r.Conflict)
                        r.Selected = !r.Selected;
                } });
                TextAt(g, r.Name.Length > 0 ? r.Name : T("name_pending") + (r.Station.Length > 0 ? " · " + T("station") + ": " + r.Station : ""), x + 53, ry + 12, w - 220, 25, 11, text, true);
                Glyphs(g, r.Portal, x + 53, ry + 43, 14);
                TextAt(g, Galaxy(r.Galaxy), x + w - 145, ry + 14, 130, 22, 9, muted);
                hits.Add(new Hit { ID = "system" + r.UA, R = new Rectangle(x + 46, ry, w - 46, 70), Run = () =>
                {
                    activeSystem = r;
                } });
            }

            int by = y + h - 43;
            ActionButton(g, "prev", U("Anterior", "Previous"), new Rectangle(x, by, 115, 38), () => page = Math.Max(0, page - 1));
            TextAt(g, (page + 1) + " / " + Math.Max(1, (records.Length + count - 1) / count), x + 134, by + 11, 100, 25, 9, muted);
            ActionButton(g, "next", U("Siguiente", "Next"), new Rectangle(x + w - 120, by, 120, 38), () =>
            {
                if ((page + 1) * count < records.Length)
                    page++;
            });
        }

        string Galaxy(int n)
        {
            return n == 0 ? "Euclides" : n == 9 ? "Eissentam" : n == 20 ? "Doctinawyra" : "#" + (n + 1);
        }

        void Glyphs(Graphics g, string portal, int x, int y, int size)
        {
            for (int i = 0; i < 12; i++)
            {
                int n = Convert.ToInt32(portal.Substring(i, 1), 16);
                g.DrawImage(atlas, new Rectangle(x + i * (size + 3), y, size, size), new Rectangle(n * 64, 0, 64, 64), GraphicsUnit.Pixel);
            }
        }

        void Editor(Graphics g, int x, int y, int w, int h)
        {
            Alliance a = Current;
            if (a == null)
                return;
            if (ImperialCore.IsAlliance(a.ID))
            {
                TextAt(g, "Imperio Español (IESP)", x, y, w, 42, 23, accent, true);
                TextAt(g, U("Catálogo oficial protegido · Emblema reservado", "Protected official catalogue · Reserved emblem"), x, y + 55, w, 35, 12, muted);
                DrawBadge(g, a, new Rectangle(x + 30, y + 115, 250, 250));
                TextAt(g, U("Su emblema oficial está protegido. El catálogo se obtiene del save y de IGM.", "Its official emblem is protected. The catalogue comes from the save and IGM."), x, y + 390, w, 60, 11, text);
                return;
            }

            TextAt(g, U("Estudio de emblemas", "Emblem studio"), x, y, w, 40, 23, text, true);
            TextAt(g, U("Diseña en alta resolución. El formato del juego se crea al exportar.", "Design in high resolution. The game format is created on export."), x, y + 45, w, 24, 10, muted);
            int rightW = 285, leftW = w - rightW - 20, body = y + 85;
            Box(g, new Rectangle(x, body, leftW, h - 85), surface, line);
            Box(g, new Rectangle(x + leftW + 20, body, rightW, h - 85), surface, line);
            int rx = x + leftW + 20;
            ActionButton(g, "myalliance", myAlliance == a.ID ? U("Mi alianza ✓", "My alliance ✓") : U("Esta es mi alianza", "This is my alliance"), new Rectangle(x + w - 235, y - 2, 235, 39), () =>
            {
                myAlliance = a.ID;
                Data.AssignOwned(project, myAlliance);
                ProfileSave();
                notice = U("Alianza recordada para este perfil.", "Alliance remembered for this profile.");
            });
            TextAt(g, U("VISTA PREVIA", "LIVE PREVIEW"), rx + 20, body + 19, rightW - 40, 20, 8, muted, true);
            int previewSize = Math.Min(rightW - 44, Math.Max(76, h - 85 - 290)), previewBottom = body + 45 + previewSize;
            DrawBadge(g, a, new Rectangle(rx + (rightW - previewSize) / 2, body + 45, previewSize, previewSize));
            TextAt(g, U("576 × 576 · Original de edición", "576 × 576 · Editing original"), rx + 20, previewBottom + 12, rightW - 40, 25, 9, muted);
            TextAt(g, U("Exportación: 180 × 180", "Export: 180 × 180"), rx + 20, previewBottom + 39, rightW - 40, 25, 9, cyan);
            TextAt(g, T("alliance_name"), x + 20, body + 18, leftW - 40, 21, 9, muted);
            TextAt(g, T("initials"), x + leftW - 98, body + 18, 85, 21, 8, muted);
            TextAt(g, U("COLOR DE ALIANZA", "ALLIANCE COLOUR"), x + 20, body + 98, leftW - 40, 20, 8, muted, true);
            int[] palette =
            {
                Color.RoyalBlue.ToArgb(),
                Color.Crimson.ToArgb(),
                Color.SeaGreen.ToArgb(),
                Color.Gold.ToArgb(),
                Color.MediumPurple.ToArgb(),
                Color.DarkOrange.ToArgb(),
                Color.Turquoise.ToArgb(),
                Color.HotPink.ToArgb(),
                Color.White.ToArgb(),
                Color.DimGray.ToArgb()
            };
            for (int i = 0; i < palette.Length; i++)
            {
                int c = palette[i];
                Rectangle r = new Rectangle(x + 20 + i * 30, body + 130, 23, 23);
                using (var b = new SolidBrush(Color.FromArgb(c)))
                    g.FillEllipse(b, r);
                if (a.ColorRGB == c)
                    using (var p = new Pen(text, 2))
                        g.DrawEllipse(p, r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6);
                hits.Add(new Hit { ID = "color" + i, R = new Rectangle(r.X - 3, r.Y - 3, 29, 29), Run = () =>
                {
                    a.ColorRGB = c;
                    UpdateArt(a);
                } });
            }

            ActionButton(g, "customcolor", U("Otro", "More"), new Rectangle(x + 20 + palette.Length * 30, body + 121, 80, 37), () =>
            {
                using (var d = new ColorDialog
                {
                    Color = Color.FromArgb(a.ColorRGB),
                    FullOpen = true
                }

                )
                    if (d.ShowDialog() == DialogResult.OK)
                    {
                        a.ColorRGB = d.Color.ToArgb();
                        UpdateArt(a);
                    }
            });
            TextAt(g, U("BIBLIOTECA DE SÍMBOLOS", "SYMBOL LIBRARY") + " · " + symbols.Length, x + 20, body + 180, leftW - 40, 20, 8, muted, true);
            int cols = 6, gridY = body + 216, space = Math.Max(42, y + h - gridY - 135), cell = Math.Min(64, Math.Min((leftW - 40) / cols, space)), rows = Math.Max(1, Math.Min(4, space / cell));
            int oldSize = symbolPageSize;
            symbolPageSize = cols * rows;
            if (oldSize != symbolPageSize)
                symbolPage = 0;
            symbolPage = Math.Min(symbolPage, Math.Max(0, (Math.Max(0, symbols.Length - 1)) / symbolPageSize));
            for (int i = 0; i < symbolPageSize && symbolPage * symbolPageSize + i < symbols.Length; i++)
            {
                string file = symbols[symbolPage * symbolPageSize + i];
                Rectangle r = new Rectangle(x + 20 + (i % cols) * cell, gridY + (i / cols) * cell, cell - 8, cell - 8);
                bool chosen = a.SymbolPath == Path.GetFileName(file) && a.ImagePng.Length == 0;
                Box(g, r, hover == "sym" + i ? Color.FromArgb(39, 56, 79) : raised, chosen ? accent : line, 7);
                g.DrawImage(GetImage(file), r.X + 10, r.Y + 10, r.Width - 20, r.Height - 20);
                hits.Add(new Hit { ID = "sym" + i, R = r, Run = () =>
                {
                    a.SymbolPath = Path.GetFileName(file);
                    a.ImagePng = "";
                    UpdateArt(a);
                } });
            }

            int gy = gridY + cell * rows + 8;
            ActionButton(g, "sprev", "‹", new Rectangle(x + 20, gy, 40, 36), () => symbolPage = Math.Max(0, symbolPage - 1));
            TextAt(g, (symbolPage + 1) + " / " + Math.Max(1, (symbols.Length + symbolPageSize - 1) / symbolPageSize), x + 70, gy + 10, 92, 24, 9, muted);
            ActionButton(g, "snext", "›", new Rectangle(x + 168, gy, 40, 36), () =>
            {
                if ((symbolPage + 1) * symbolPageSize < symbols.Length)
                    symbolPage++;
            });
            int actions = y + h - 87;
            ActionButton(g, "image", T("load_image"), new Rectangle(x + 20, actions, (leftW - 50) / 2, 39), ImportImage, false, "load");
            ActionButton(g, "clear", T("clear_image"), new Rectangle(x + 30 + (leftW - 50) / 2, actions, (leftW - 50) / 2, 39), () =>
            {
                a.ImagePng = "";
                UpdateArt(a);
            });
            ActionButton(g, "diamond", U("Rombo: ", "Diamond: ") + (a.UseDiamond ? "ON" : "OFF"), new Rectangle(rx + 20, previewBottom + 76, rightW - 40, 39), () =>
            {
                a.UseDiamond = !a.UseDiamond;
                UpdateArt(a);
            });
            ActionButton(g, "ink", U("Contraste automático", "Automatic contrast"), new Rectangle(rx + 20, previewBottom + 123, rightW - 40, 39), () =>
            {
                UpdateArt(a);
            });
            TextAt(g, U("Opacidad", "Opacity") + "  " + a.Opacity + "%", rx + 20, previewBottom + 171, rightW - 40, 24, 9, muted);
            ActionButton(g, "minus", "−", new Rectangle(rx + 20, previewBottom + 198, 65, 36), () =>
            {
                a.Opacity = Math.Max(0, a.Opacity - 10);
                UpdateArt(a);
            });
            ActionButton(g, "plus", "+", new Rectangle(rx + rightW - 85, previewBottom + 198, 65, 36), () =>
            {
                a.Opacity = Math.Min(100, a.Opacity + 10);
                UpdateArt(a);
            });
            TextAt(g, U("Los cambios se conservan localmente.", "Changes are retained locally."), x + 20, y + h - 47, leftW - 40, 22, 9, cyan);
        }

        Image GetImage(string file)
        {
            Image im;
            if (!images.TryGetValue(file, out im))
            {
                im = Assets.HasSymbol(file) ? Assets.Image("Symbols." + Path.GetFileName(file)) : Image.FromFile(file);
                images.Add(file, im);
            }

            return im;
        }

        void DrawBadge(Graphics g, Alliance a, Rectangle r)
        {
            if (ImperialCore.IsAlliance(a.ID))
            {
                ImperialCore.DrawBadge(g, r);
                return;
            }

            if (a.PreviewPng.Length > 0)
            {
                string key = "preview:" + a.ID;
                Image im;
                if (!images.TryGetValue(key, out im))
                {
                    using (var s = new MemoryStream(Convert.FromBase64String(a.PreviewPng)))
                        im = new Bitmap(s);
                    images[key] = im;
                }

                g.DrawImage(im, r);
            }
            else
                using (var b = new SolidBrush(Color.FromArgb(a.ColorRGB)))
                    g.FillPolygon(b, new[] { new Point(r.X + r.Width / 2, r.Top), new Point(r.Right, r.Y + r.Height / 2), new Point(r.X + r.Width / 2, r.Bottom), new Point(r.Left, r.Y + r.Height / 2) });
        }

        void Select(int i)
        {
            selected = i;
            page = 0;
            activeSystem = null;
            BindFields();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (nameField != null)
                LayoutInputs();
        }

        void LayoutInputs()
        {
            bool show = tab == 1 && Current != null && !ImperialCore.IsAlliance(Current.ID) && project.Systems.Count > 0;
            nameField.Visible = initialField.Visible = show;
            if (show)
            {
                int leftW = Width - 625;
                nameField.SetBounds(310, 234, leftW - 130, 29);
                initialField.SetBounds(290 + leftW - 98, 234, 78, 29);
                nameField.BringToFront();
                initialField.BringToFront();
            }
        }

        void BindFields()
        {
            editing = false;
            var a = Current;
            nameField.Text = a == null ? "" : a.Name;
            initialField.Text = a == null ? "" : a.Initials;
            editing = true;
            LayoutInputs();
        }

        void UpdateArt(Alliance a)
        {
            if (ImperialCore.IsAlliance(a.ID))
                return;
            a.InkRGB = Data.Contrast58(a.ColorRGB);
            using (var b = new Bitmap(576, 576, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    using (var p = new GraphicsPath())
                    {
                        if (a.UseDiamond)
                            p.AddPolygon(new[] { new Point(288, 9), new Point(567, 288), new Point(288, 567), new Point(9, 288) });
                        else
                            p.AddRectangle(new Rectangle(12, 12, 552, 552));
                        g.SetClip(p);
                        using (var brush = new SolidBrush(Color.FromArgb(a.ColorRGB)))
                            g.FillPath(brush, p);
                        if (a.ImagePng.Length > 0)
                        {
                            using (var s = new MemoryStream(Convert.FromBase64String(a.ImagePng)))
                            using (var im = Image.FromStream(s))
                            {
                                float k = Math.Min(450f / im.Width, 450f / im.Height);
                                g.DrawImage(im, (576 - im.Width * k) / 2, (576 - im.Height * k) / 2, im.Width * k, im.Height * k);
                            }
                        }
                        else if (a.SymbolPath.Length > 0)
                        {
                            string file = Path.Combine(Base, "Symbols", Path.GetFileName(a.SymbolPath));
                            if (Assets.HasSymbol(file))
                            {
                                Image im = GetImage(file);
                                Color c = Color.FromArgb(a.InkRGB);
                                using (var attr = new ImageAttributes())
                                {
                                    var m = new ColorMatrix(new float[][] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 1, 0 }, new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 } });
                                    attr.SetColorMatrix(m);
                                    g.DrawImage(im, new Rectangle(135, 128, 306, 306), 0, 0, im.Width, im.Height, GraphicsUnit.Pixel, attr);
                                }
                            }
                        }

                        if (a.Initials.Length > 0)
                            using (var f = new Font("Segoe UI", a.Initials.Length > 4 ? 29 : 36, FontStyle.Bold))
                            using (var brush = new SolidBrush(Color.FromArgb(a.InkRGB)))
                            using (var sf = new StringFormat
                            {
                                Alignment = StringAlignment.Center,
                                LineAlignment = StringAlignment.Center
                            }

                            )
                                g.DrawString(a.Initials, f, brush, new RectangleF(160, 375, 256, 65), sf);
                        g.ResetClip();
                        using (var pen = new Pen(Color.FromArgb(91, 112, 136), 8))
                            g.DrawPath(pen, p);
                    }
                }

                if (a.Opacity < 100)
                {
                    for (int y = 0; y < b.Height; y++)
                        for (int x = 0; x < b.Width; x++)
                        {
                            Color c = b.GetPixel(x, y);
                            b.SetPixel(x, y, Color.FromArgb(c.A * a.Opacity / 100, c.R, c.G, c.B));
                        }
                }

                using (var s = new MemoryStream())
                {
                    b.Save(s, ImageFormat.Png);
                    a.PreviewPng = Convert.ToBase64String(s.ToArray());
                }
            }

            string cache = "preview:" + a.ID;
            Image old;
            if (images.TryGetValue(cache, out old))
            {
                old.Dispose();
                images.Remove(cache);
            }

            SaveStyles();
            Invalidate();
        }

        void SaveStyles()
        {
            try
            {
                Directory.CreateDirectory(dataDir);
                foreach (var a in project.Alliances.Where(z => !ImperialCore.IsAlliance(z.ID)))
                {
                    int i = styles.FindIndex(z => z.ID == a.ID);
                    if (i < 0)
                        styles.Add(a);
                    else
                        styles[i] = a;
                }

                string p = Path.Combine(dataDir, "styles_v015.json");
                Data.SaveLocal(p, Data.Json().Serialize(styles));
            }
            catch
            {
                notice = U("No se pudo guardar la personalización local.", "Could not save local customisation.");
            }
        }

        void ImportImage()
        {
            var a = Current;
            if (a == null || ImperialCore.IsAlliance(a.ID))
                return;
            using (var d = new OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
            }

            )
                if (d.ShowDialog() == DialogResult.OK)
                {
                    if (new FileInfo(d.FileName).Length > 16 * 1024 * 1024)
                        throw new Exception("Image >16 MiB");
                    using (var im = Image.FromFile(d.FileName))
                    {
                        if (im.Width > 4096 || im.Height > 4096)
                            throw new Exception("Image >4096px");
                        using (var s = new MemoryStream())
                        {
                            im.Save(s, ImageFormat.Png);
                            a.ImagePng = Convert.ToBase64String(s.ToArray());
                        }
                    }

                    UpdateArt(a);
                }
        }

        string ProfileFile()
        {
            if (project.Owner.Length == 0)
                return "";
            return Path.Combine(dataDir, "profile_" + new string (project.Owner.Where(char.IsLetterOrDigit).ToArray()) + ".json");
        }

        string ResolveFolder()
        {
            if (folder.Length > 0)
                return folder;
            try
            {
                return Data.MigrateBundle(Base);
            }
            catch (InvalidOperationException)
            {
                using (var d = new FolderBrowserDialog
                {
                    Description = U("Selecciona IGM, IGM_datos o Binaries de NMS", "Select IGM, IGM_datos or NMS Binaries"),
                    ShowNewFolderButton = false
                }

                )
                {
                    if (d.ShowDialog() != DialogResult.OK)
                        throw new OperationCanceledException("Folder selection cancelled");
                    return Data.MigrateBundle(d.SelectedPath);
                }
            }
        }

        void ProfileLoad()
        {
            myAlliance = "";
            folder = ResolveFolder();
            string f = ProfileFile();
            if (f.Length > 0 && File.Exists(f))
            {
                object j = Data.Json().DeserializeObject(File.ReadAllText(f));
                myAlliance = Data.Str(Data.Get(j, "alliance"));
            }

            if (Directory.Exists(folder))
            {
                try
                {
                    AutoNames(folder);
                }
                catch (Exception e)
                {
                    throw new InvalidDataException(U("No se pudo combinar el catálogo local de IGM: ", "Could not merge the local IGM catalogue: ") + e.Message, e);
                }
            }
        }

        void ProfileSave()
        {
            string f = ProfileFile();
            if (f.Length == 0)
                return;
            Directory.CreateDirectory(dataDir);
            Data.SaveLocal(f, Data.Json().Serialize(new Dictionary<string, object> { { "alliance", myAlliance }, { "folder", folder } }));
        }

        void AutoNames(string path)
        {
            ImperialCore.Apply(project);
            var local = Data.LoadMod(path);
            int added = project.MergeLocal(local);
            notice = U("Alianzas añadidas desde IGM: ", "Alliances added from IGM: ") + added;
        }

        void LoadSave()
        {
            using (var d = new OpenFileDialog
            {
                Filter = "NMS save|*.hg;*.json|All files|*.*"
            }

            )
                if (d.ShowDialog() == DialogResult.OK)
                {
                    Cursor = Cursors.WaitCursor;
                    try
                    {
                        Project p = Data.Import(d.FileName, "");
                        foreach (var key in images.Keys.Where(k => k.StartsWith("preview:")).ToArray())
                        {
                            images[key].Dispose();
                            images.Remove(key);
                        }

                        project = p;
                        ProfileLoad();
                        if (myAlliance.Length > 0)
                            Data.AssignOwned(project, myAlliance);
                        source = Path.GetFileName(d.FileName) + "  ·  " + p.Systems.Count + " " + U("sistemas", "systems");
                        var used = new HashSet<int>();
                        int[] basic =
                        {
                            Color.FromArgb(12, 15, 21).ToArgb(),
                            Color.FromArgb(210, 32, 45).ToArgb(),
                            Color.FromArgb(35, 95, 225).ToArgb(),
                            Color.FromArgb(250, 210, 40).ToArgb(),
                            Color.FromArgb(24, 145, 70).ToArgb()
                        };
                        int newStyle = 0;
                        for (int i = 0; i < p.Alliances.Count; i++)
                        {
                            var a = p.Alliances[i];
                            if (ImperialCore.IsAlliance(a.ID))
                                continue;
                            var cached = styles.Find(z => z.ID == a.ID);
                            if (cached != null)
                            {
                                a.ColorRGB = cached.ColorRGB;
                                a.PreviewPng = cached.PreviewPng;
                                a.Icon = cached.Icon;
                                a.SymbolPath = cached.SymbolPath;
                                a.ImagePng = cached.ImagePng;
                                a.InkRGB = cached.InkRGB;
                                a.Opacity = cached.Opacity;
                                a.UseDiamond = cached.UseDiamond;
                                if (cached.NameEdited && cached.Name.Length > 0)
                                {
                                    a.Name = cached.Name;
                                    a.Initials = cached.Initials;
                                }

                                a.NameEdited = cached.NameEdited;
                            }

                            uint seed = 2166136261;
                            foreach (char c in a.ID)
                                seed = (seed ^ c) * 16777619;
                            var random = new Random((int)(seed & 0x7fffffff));
                            if (a.ColorRGB == 0)
                            {
                                a.ColorRGB = newStyle < basic.Length ? basic[newStyle++] : 0;
                                while (a.ColorRGB == 0 || used.Contains(a.ColorRGB))
                                    a.ColorRGB = Color.FromArgb(random.Next(40, 235), random.Next(40, 235), random.Next(40, 235)).ToArgb();
                            }

                            used.Add(a.ColorRGB);
                            if (a.SymbolPath.Length == 0 && symbols.Length > 0)
                                a.SymbolPath = Path.GetFileName(symbols[seed % (uint)symbols.Length]);
                            if (a.PreviewPng.Length == 0)
                                UpdateArt(a);
                        }

                        selected = project.Alliances.Count > 0 ? 0 : -1;
                        page = symbolPage = alliancePage = 0;
                        BindFields();
                        notice = T("imported") + ": " + p.Systems.Count + " / " + p.Alliances.Count + (p.Skipped > 0 ? " · " + p.Skipped + " " + U("registros omitidos o ambiguos", "skipped or ambiguous records") : "") + (p.Conflicts > 0 ? " · " + p.Conflicts + " " + U("conflictos sin exportar", "conflicts excluded from export") : "");
                        SaveStyles();
                    }
                    finally
                    {
                        Cursor = Cursors.Default;
                    }
                }
        }

        void Export()
        {
            if (project.Systems.Count == 0)
                return;
            folder = ResolveFolder();
            Directory.CreateDirectory(folder);
            AutoNames(folder);
            ProfileSave();
            BindFields();
            string result = Data.Export(project, folder, true);
            notice = T("exported") + ": " + result;
            SaveStyles();
        }

        void Languages()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = surface,
                ForeColor = text,
                ShowImageMargin = false
            };
            string[] names =
            {
                "Español",
                "English",
                "Français",
                "Deutsch",
                "Italiano",
                "Português"
            };
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                var item = menu.Items.Add(names[i]);
                item.Click += (s, e) =>
                {
                    language = k;
                    Directory.CreateDirectory(dataDir);
                    Data.SaveLocal(Path.Combine(dataDir, "companion.json"), "{\"language\":" + k + "}");
                    Invalidate();
                };
            }

            menu.Show(this, new Point(20, Height - 235));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var im in images.Values)
                    im.Dispose();
                logo.Dispose();
                atlas.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    public class MainWindow : Form
    {
        public MainWindow()
        {
            Text = "Imperial Galactic Map · Companion 0.28";
            ClientSize = new Size(1380, 930);
            MinimumSize = new Size(1100, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.None;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Controls.Add(new ModernCanvas { Dock = DockStyle.Fill });
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainWindow());
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString(), "IGM Companion");
            }
        }
    }
}
