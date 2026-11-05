using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace IGM
{
    // IGM file contract v1. No dependency on a particular DLL version number.
    public static partial class Data
    {
        public static string MigrateBundle(string baseDir)
        {
            string root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetFileName(root).Equals("IGM_datos", StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
                return root;
            if (Directory.Exists(Path.Combine(root, "IGM_datos")) || Path.GetFileName(root).Equals("IGM", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Path.GetDirectoryName(root), "NMS.exe")))
                return Path.Combine(root, "IGM_datos");
            if (File.Exists(Path.Combine(root, "NMS.exe")))
                return Path.Combine(root, "IGM", "IGM_datos");
            throw new InvalidOperationException("Select the installed IGM, IGM_datos or NMS Binaries folder.");
        }

        static string ReadText(string path)
        {
            if (new FileInfo(path).Length > FileLimit)
                throw new InvalidDataException("File >128 MiB: " + Path.GetFileName(path));
            return Utf8.GetString(File.ReadAllBytes(path)).TrimStart('\ufeff');
        }

        static IEnumerable<string> Rows(string path)
        {
            return File.Exists(path) ? ReadText(path).Split('\n') : new string[0];
        }

        static string[] Exploration(string line)
        {
            var x = line.TrimEnd('\r').Split('\t');
            int planet, pirate, priority;
            if (x.Length != 5 || !int.TryParse(x[1], out planet) || planet < 0 || planet > 15 || !int.TryParse(x[2], out pirate) || pirate < -1 || pirate > 1 || !int.TryParse(x[3], out priority) || priority < 0 || priority > 3 || x[4].Length > 254)
                throw new InvalidDataException("Unsupported exploration row. Existing data was not changed.");
            UA("0x" + x[0]);
            string name = Unhex(x[4]);
            if (name.Contains("\0"))
                throw new InvalidDataException("Name contains NUL");
            return x;
        }

        public static Project LoadMod(string dir)
        {
            var p = new Project();
            var known = new Dictionary<string, SystemRecord>();
            foreach (string raw in Rows(Path.Combine(dir, "catalogo_observado.jsonl")))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                try
                {
                    object r = Json().DeserializeObject(line);
                    string u = UA("0x" + Str(Get(r, "system_ua"))), id = ID(Get(r, "alliance_id_words"));
                    if (id.Length == 0)
                        continue;
                    SystemRecord s;
                    if (!known.TryGetValue(u, out s))
                    {
                        s = new SystemRecord
                        {
                            UA = u,
                            Evidence = "existing_mod"
                        };
                        known.Add(u, s);
                        p.Systems.Add(s);
                    }

                    s.Alliance = id;
                    var a = p.GetAlliance(id);
                    string n = Repair(Str(Get(r, "cached_alliance_name")));
                    if (n.Length > 0)
                        a.Name = n;
                }
                catch (Exception e)
                {
                    if (!(RecordError(e) || e is ArgumentException))
                        throw;
                    p.Skipped++;
                }
            }

            var priority = new Dictionary<string, int>();
            foreach (string raw in Rows(Path.Combine(dir, "exploracion.tsv")))
            {
                if (raw.Trim().Length == 0 || raw.StartsWith("#"))
                    continue;
                try
                {
                    var x = Exploration(raw);
                    string ua = UA("0x" + x[0]);
                    SystemRecord s;
                    int old = -1;
                    priority.TryGetValue(ua, out old);
                    int rank = int.Parse(x[3]);
                    if (x[1] == "0" && known.TryGetValue(ua, out s) && Unhex(x[4]).Length > 0 && rank >= old)
                    {
                        s.Name = Unhex(x[4]);
                        priority[ua] = rank;
                    }
                }
                catch (Exception e)
                {
                    if (!(RecordError(e) || e is ArgumentException))
                        throw;
                    p.Skipped++;
                }
            }

            var colours = new Dictionary<string, int>();
            foreach (string raw in Rows(Path.Combine(dir, "colores_alianzas.tsv")))
            {
                var x = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                uint r, g, b;
                if (x.Length == 8 && uint.TryParse(x[5], out r) && r <= 255 && uint.TryParse(x[6], out g) && g <= 255 && uint.TryParse(x[7], out b) && b <= 255)
                    colours[string.Join("_", x.Take(4).ToArray())] = unchecked((int)0xff000000) | (int)(r << 16 | g << 8 | b);
            }

            foreach (var a in p.Alliances)
            {
                a.Initials = Initials(a.Name);
                int c;
                if (colours.TryGetValue(a.ID, out c))
                    a.ColorRGB = c;
                if (ImperialCore.IsAlliance(a.ID))
                    continue;
                string file = Path.Combine(dir, "iconos_alianzas", a.ID + ".bmp");
                if (File.Exists(file))
                {
                    try
                    {
                        using (var b = new System.Drawing.Bitmap(file))
                        {
                            if (b.Width != 180 || b.Height != 180)
                                throw new InvalidDataException("Icon dimensions");
                            using (var s = new MemoryStream())
                            {
                                b.Save(s, System.Drawing.Imaging.ImageFormat.Png);
                                a.PreviewPng = Convert.ToBase64String(s.ToArray());
                            }
                        }
                    }
                    catch (ArgumentException)
                    {
                        p.Skipped++;
                    }
                }
                else
                {
                    file = Path.ChangeExtension(file, ".igmicon");
                    if (File.Exists(file))
                    {
                        a.Icon = ReadText(file);
                        IconCodec.Validate(a.Icon);
                    }
                }
            }

            ImperialCore.Apply(p);
            return p;
        }

        static byte[] Append(string folder, string file, IEnumerable<string> lines)
        {
            string path = Path.Combine(folder, file);
            byte[] old = File.Exists(path) ? File.ReadAllBytes(path) : new byte[0];
            if (old.Length > FileLimit)
                throw new InvalidDataException("Export file too large");
            // Validate only known fields. Preserve every original byte and unknown JSON field.
            foreach (string row in Rows(path))
            {
                if (row.Trim().Length == 0)
                    continue;
                if (file.EndsWith(".tsv"))
                {
                    if (!row.StartsWith("#"))
                        Exploration(row);
                }
                else
                {
                    string r = row.TrimEnd('\r');
                    if (!r.StartsWith("{\"system_ua\":\"") || r.IndexOf("\",\"alliance_id_words\":[", StringComparison.Ordinal) < 0)
                        throw new InvalidDataException("Catalogue layout is not compatible with this adapter. No files changed.");
                    object j = Json().DeserializeObject(r);
                    UA("0x" + Str(Get(j, "system_ua")));
                    if (ID(Get(j, "alliance_id_words")).Length == 0)
                        throw new InvalidDataException("Unsupported catalogue record");
                }
            }

            if (file.EndsWith(".tsv"))
            {
                var keys = new HashSet<string>();
                foreach (string row in Rows(path).Concat(lines))
                {
                    if (row.Trim().Length == 0 || row.StartsWith("#"))
                        continue;
                    var parts = Exploration(row);
                    keys.Add(UA("0x" + parts[0]) + ":" + parts[1]);
                }

                if (keys.Count > 8192)
                    throw new InvalidDataException("Exploration capacity 8192");
            }

            string suffix = (old.Length > 0 && old[old.Length - 1] != 10 ? "\r\n" : "") + string.Join("\r\n", lines.ToArray()) + "\r\n";
            return old.Concat(Utf8.GetBytes(suffix)).ToArray();
        }

        public static string Export(Project project, string folder, bool checkGame)
        {
            ImperialCore.Apply(project);
            if (checkGame)
            {
                var processes = Process.GetProcessesByName("NMS");
                try
                {
                    if (processes.Length > 0)
                        throw new InvalidOperationException("NMS is running. Close it before exporting.");
                }
                finally
                {
                    foreach (var p in processes)
                        p.Dispose();
                }
            }

            folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Path.GetFileName(folder).Equals("IGM_datos", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select IGM_datos");
            Directory.CreateDirectory(folder);
            using (var exportLock = new FileStream(Path.Combine(folder, ".companion-export.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                var expected = new Dictionary<string, byte[]>();
                foreach (string rel in new[]
                {
                    "catalogo_observado.jsonl",
                    "exploracion.tsv"
                }

                )
                {
                    string path = Path.Combine(folder, rel);
                    expected[rel] = File.Exists(path) ? File.ReadAllBytes(path) : null;
                }

                string iconRoot = Path.Combine(folder, "iconos_alianzas");
                if (Directory.Exists(iconRoot))
                    foreach (string path in Directory.GetFiles(iconRoot, "*.bmp"))
                        expected[Path.Combine("iconos_alianzas", Path.GetFileName(path))] = File.ReadAllBytes(path);
                Project existing = LoadMod(folder);
                var known = existing.Systems.ToDictionary(x => x.UA);
                var additions = new List<string>();
                var names = new List<string>();
                int conflicts = project.Conflicts;
                var nameKeys = new HashSet<string>(Rows(Path.Combine(folder, "exploracion.tsv")).Where(x => x.Length > 0 && !x.StartsWith("#")).Select(x => x.Split('\t')[0].ToLowerInvariant()));
                foreach (var s in project.Systems.Where(x => x.Selected && !x.Conflict && x.Alliance.Length > 0))
                {
                    string ua = UA("0x" + s.UA);
                    uint[] words = Words(s.Alliance);
                    SystemRecord old;
                    bool has = known.TryGetValue(ua, out old);
                    if (has && old.Alliance != s.Alliance)
                    {
                        conflicts++;
                        continue;
                    }

                    Alliance a = project.GetAlliance(s.Alliance);
                    if (!has || (a.NameEdited && existing.Alliances.Any(z => z.ID == a.ID && z.Name != a.Name)))
                    {
                        // These first two keys/order/spacing are required by current IGM's C reader.
                        string row = "{\"system_ua\":\"" + ua + "\",\"alliance_id_words\":[" + string.Join(",", words.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray()) + "],\"cached_alliance_name\":" + Json().Serialize(a.Name) + ",\"source\":\"save_cached_allianceid\",\"freshness\":\"cached_not_server_verified\",\"source_sha256\":" + Json().Serialize(project.SourceHash) + "}";
                        if (Utf8.GetByteCount(row) > 1800)
                            throw new InvalidDataException("Alliance name too long");
                        additions.Add(row);
                        if (!has)
                            known.Add(ua, s);
                    }

                    if (s.Name.Length > 0 && (!has || old.Name.Length == 0) && !nameKeys.Contains(ua))
                    {
                        names.Add(ua + "\t0\t-1\t1\t" + Hex(s.Name));
                        nameKeys.Add(ua);
                    }
                }

                if (known.Count > 4096)
                    throw new InvalidDataException("Catalogue capacity 4096");
                var writes = new Dictionary<string, byte[]>();
                if (additions.Count > 0)
                    writes["catalogo_observado.jsonl"] = Append(folder, "catalogo_observado.jsonl", additions);
                if (names.Count > 0)
                    writes["exploracion.tsv"] = Append(folder, "exploracion.tsv", names);
                var iconIds = new HashSet<string>();
                string icons = Path.Combine(folder, "iconos_alianzas");
                if (Directory.Exists(icons))
                    foreach (string file in Directory.GetFiles(icons))
                    {
                        string ext = Path.GetExtension(file);
                        if (ext == ".bmp" || ext == ".igmicon")
                            iconIds.Add(Path.GetFileNameWithoutExtension(file));
                    }

                foreach (var a in project.Alliances.Where(x => !ImperialCore.IsAlliance(x.ID) && (x.Icon.Length > 0 || x.PreviewPng.Length > 0)))
                {
                    Words(a.ID);
                    iconIds.Add(a.ID);
                    string rel = Path.Combine("iconos_alianzas", a.ID + ".bmp"), path = Path.Combine(folder, rel);
                    byte[] image = IconCodec.ExportTexture(a);
                    if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(image))
                        writes[rel] = image;
                }

                if (iconIds.Count > 128)
                    throw new InvalidDataException("Icon capacity 128");
                if (writes.Count > 0)
                {
                    foreach (string key in writes.Keys)
                        if (!expected.ContainsKey(key))
                            expected[key] = null;
                    Commit(folder, writes, expected);
                }

                return additions.Count + " / names: " + names.Count + " / icons: " + writes.Keys.Count(x => x.EndsWith(".bmp")) + " / conflicts: " + conflicts;
            }
        }

        // Stage -> backup -> compare -> atomic replacement. Never rewrites unrelated data.
        public static void Commit(string folder, Dictionary<string, byte[]> writes, Dictionary<string, byte[]> expected = null)
        {
            string backup = Path.Combine(folder, "copias_companion", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var originals = new Dictionary<string, byte[]>();
            var pending = new Dictionary<string, string>();
            var done = new List<string>();
            try
            {
                foreach (var w in writes)
                {
                    string dst = Path.GetFullPath(Path.Combine(folder, w.Key));
                    if (Path.IsPathRooted(w.Key) || !dst.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Export path");
                    byte[] old = File.Exists(dst) ? File.ReadAllBytes(dst) : null;
                    if (expected != null)
                    {
                        byte[] want = expected[w.Key];
                        if (want == null ? old != null : old == null || !want.SequenceEqual(old))
                            throw new IOException("Data changed while preparing export. No overwrite performed.");
                    }

                    originals[w.Key] = old;
                    if (old != null)
                    {
                        string bak = Path.Combine(backup, w.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(bak));
                        File.WriteAllBytes(bak, old);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    string tmp = dst + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    pending[w.Key] = tmp;
                    using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        f.Write(w.Value, 0, w.Value.Length);
                        f.Flush(true);
                    }
                }

                foreach (var w in writes)
                {
                    string dst = Path.Combine(folder, w.Key);
                    byte[] old = originals[w.Key];
                    if (old == null ? File.Exists(dst) : !File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(old))
                        throw new IOException("Data changed during export; close NMS and retry. Backups retained.");
                    if (old == null)
                        File.Move(pending[w.Key], dst);
                    else
                        File.Replace(pending[w.Key], dst, null);
                    done.Add(w.Key);
                }
            }
            catch
            {
                foreach (string key in done.AsEnumerable().Reverse())
                {
                    string dst = Path.Combine(folder, key);
                    if (File.Exists(dst) && File.ReadAllBytes(dst).SequenceEqual(writes[key]))
                    {
                        if (originals[key] == null)
                            File.Delete(dst);
                        else
                            File.WriteAllBytes(dst, originals[key]);
                    }
                }

                throw;
            }
            finally
            {
                foreach (string tmp in pending.Values)
                    if (File.Exists(tmp))
                        File.Delete(tmp);
            }
        }

        public static void SaveLocal(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, text, Utf8);
                if (File.Exists(path))
                    File.Replace(tmp, path, path + ".bak");
                else
                    File.Move(tmp, path);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }
    }
}
