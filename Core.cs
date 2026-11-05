using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Drawing;

namespace IGM
{
    public class SystemRecord
    {
        public string UA, Name = "", Station = "", Alliance = "", Owner = "", Evidence = "";
        public int Planet;
        public bool Owned, Conflict, Selected = true;
        public string Key
        {
            get
            {
                return UA + ":" + Planet;
            }
        }

        public string Portal
        {
            get
            {
                ulong u = Convert.ToUInt64(UA, 16);
                return (((ulong)(Planet > 0 ? Planet : 1) << 44) | (((u >> 40) & 4095) << 32) | (u & 0xffffffffUL)).ToString("X12");
            }
        }

        public int Galaxy
        {
            get
            {
                return (int)((Convert.ToUInt64(UA, 16) >> 32) & 255);
            }
        }
    }

    public class Alliance
    {
        public string ID, Name = "", Initials = "", PreviewPng = "", SymbolPath = "", ImagePng = "", Icon = "";
        public int ColorRGB, InkRGB = -1, Opacity = 100;
        public bool UseDiamond = true, NameEdited;
        public string Display
        {
            get
            {
                return (Name.Length > 0 ? Name : ID) + (Initials.Length > 0 ? " (" + Initials + ")" : "");
            }
        }
    }

    public class Project
    {
        public List<SystemRecord> Systems = new List<SystemRecord>();
        public List<Alliance> Alliances = new List<Alliance>();
        public string SourceHash = "", SourceFile = "", Owner = "";
        public int Skipped;
        public int Conflicts
        {
            get
            {
                return Systems.Count(x => x.Conflict);
            }
        }

        // Union by stable IDs, never by display names. The installed catalogue is read-only.
        public int MergeLocal(Project local)
        {
            var records = Systems.ToDictionary(x => x.Key);
            var alliances = Alliances.ToDictionary(x => x.ID);
            int added = 0;
            foreach (var source in local.Alliances)
            {
                Alliance target;
                if (!alliances.TryGetValue(source.ID, out target))
                {
                    target = source;
                    Alliances.Add(target);
                    alliances.Add(target.ID, target);
                    added++;
                }
                else
                {
                    if (!target.NameEdited && source.Name.Length > 0)
                    {
                        target.Name = source.Name;
                        target.Initials = source.Initials;
                    }

                    if (target.ColorRGB == 0)
                        target.ColorRGB = source.ColorRGB;
                    if (target.PreviewPng.Length == 0 && target.Icon.Length == 0)
                    {
                        target.PreviewPng = source.PreviewPng;
                        target.Icon = source.Icon;
                    }
                }
            }

            foreach (var source in local.Systems)
            {
                SystemRecord target;
                if (!records.TryGetValue(source.Key, out target))
                {
                    source.Selected = false;
                    Systems.Add(source);
                    records.Add(source.Key, source);
                    continue;
                }

                // Local custom names outrank automatic names read from discovery caches.
                if (source.Name.Length > 0)
                    target.Name = source.Name;
                if (target.Alliance.Length == 0 && target.Evidence != "conflicting_cache")
                    target.Alliance = source.Alliance;
                else if (target.Alliance.Length > 0 && source.Alliance.Length > 0 && target.Alliance != source.Alliance)
                {
                    target.Conflict = true;
                    target.Selected = false;
                }
            }

            ImperialCore.Apply(this);
            return added;
        }

        public Alliance GetAlliance(string id)
        {
            Alliance a = Alliances.Find(x => x.ID == id);
            if (a == null)
            {
                a = new Alliance
                {
                    ID = id
                };
                Alliances.Add(a);
            }

            return a;
        }
    }

    // Read-only save decoder. No external process, game memory access or save writes.
    public static partial class Data
    {
        public const int FileLimit = 128 * 1024 * 1024;
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = FileLimit,
                RecursionLimit = 256
            };
        }

        public static Dictionary<string, object> Obj(object o)
        {
            return o as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        public static object Get(object o, string a, string b = "")
        {
            object v;
            var d = Obj(o);
            if (d.TryGetValue(a, out v))
                return v;
            return b.Length > 0 && d.TryGetValue(b, out v) ? v : null;
        }

        public static object[] Arr(object o)
        {
            if (o is object[])
                return (object[])o;
            var a = o as ArrayList;
            return a == null ? new object[0] : a.ToArray();
        }

        public static string Str(object o)
        {
            return o == null ? "" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static int Num(object o)
        {
            if (o == null)
                throw new FormatException("Missing integer");
            return Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static ulong ULong(object o)
        {
            string s = Str(o);
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt64(s.Substring(2), 16) : Convert.ToUInt64(s);
        }

        public static string Repair(string s)
        {
            if (s.Contains("Ã") || s.Contains("Â"))
                try
                {
                    var b = Encoding.GetEncoding(1252, new EncoderExceptionFallback(), new DecoderExceptionFallback()).GetBytes(s);
                    string t = Utf8.GetString(b);
                    if (!t.Contains("Ã") && !t.Contains("Â"))
                        return t;
                }
                catch (ArgumentException)
                {
                }

            return s;
        }

        public static uint[] Words(string id)
        {
            var a = id.Split('_');
            if (a.Length != 4)
                throw new FormatException("Alliance ID");
            var w = a.Select(x => uint.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (w.All(x => x == 0))
                throw new FormatException("Empty alliance");
            return w;
        }

        public static string ID(object o)
        {
            var a = Arr(Get(o, "Data", "8?J") ?? o);
            if (a.Length != 4)
                return "";
            var w = a.Select(x => Convert.ToUInt32(x)).ToArray();
            return w.All(x => x == 0) ? "" : string.Join("_", w.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }

        public static string UA(object o)
        {
            ulong n = ULong(o);
            if (n == 0 || n >= 1UL << 56)
                throw new FormatException("UA");
            n &= 0x000fffffffffffffUL;
            if (n == 0)
                throw new FormatException("Empty system address");
            return n.ToString("x16");
        }

        public static string Pack(object address)
        {
            if (address is string || address is ValueType)
                return UA(address);
            object gal = Get(address, "RealityIndex", "Iis"), g = Get(address, "GalacticAddress", "oZw");
            int galaxy = Num(gal), x = Num(Get(g, "VoxelX", "dZj")), y = Num(Get(g, "VoxelY", "IyE")), z = Num(Get(g, "VoxelZ", "uXE")), s = Num(Get(g, "SolarSystemIndex", "vby"));
            if (galaxy < 0 || galaxy > 255 || x < -2048 || x > 2047 || y < -128 || y > 127 || z < -2048 || z > 2047 || s < 0 || s > 4095)
                throw new FormatException("UniverseAddress bounds");
            return UA(((ulong)s << 40) | ((ulong)galaxy << 32) | ((ulong)(y & 255) << 24) | ((ulong)(z & 4095) << 12) | (uint)(x & 4095));
        }

        static int Length(byte[] b, ref int i, int n)
        {
            if (n == 15)
            {
                int v;
                do
                {
                    if (i >= b.Length)
                        throw new InvalidDataException("LZ4 length");
                    v = b[i++];
                    n = checked(n + v);
                }
                while (v == 255);
            }

            return n;
        }

        public static byte[] LZ4(byte[] b, int expected)
        {
            if (expected <= 0 || expected > 524288)
                throw new InvalidDataException("Chunk size");
            byte[] d = new byte[expected];
            int i = 0, j = 0;
            while (i < b.Length)
            {
                int token = b[i++], n = Length(b, ref i, token >> 4);
                if (n > b.Length - i || n > d.Length - j)
                    throw new InvalidDataException("LZ4 literals");
                Buffer.BlockCopy(b, i, d, j, n);
                i += n;
                j += n;
                if (i == b.Length)
                    break;
                if (i + 2 > b.Length)
                    throw new InvalidDataException("LZ4 offset");
                int off = b[i] | b[i + 1] << 8;
                i += 2;
                if (off == 0 || off > j)
                    throw new InvalidDataException("LZ4 backref");
                n = checked(Length(b, ref i, token & 15) + 4);
                if (n > d.Length - j)
                    throw new InvalidDataException("LZ4 expansion");
                for (int k = 0; k < n; k++)
                {
                    d[j] = d[j - off];
                    j++;
                }
            }

            if (j != expected)
                throw new InvalidDataException("Chunk length");
            return d;
        }

        public static byte[] ReadSave(string file)
        {
            if (new FileInfo(file).Length > FileLimit)
                throw new InvalidDataException("Save >128 MiB");
            byte[] b = File.ReadAllBytes(file);
            int start = b.Length >= 3 && b[0] == 239 && b[1] == 187 && b[2] == 191 ? 3 : 0;
            while (start < b.Length && (b[start] == 32 || b[start] == 9 || b[start] == 10 || b[start] == 13))
                start++;
            if (start < b.Length && b[start] == '{')
                return b.Skip(start).ToArray();
            using (var output = new MemoryStream())
            {
                int pos = 0;
                while (pos < b.Length)
                {
                    if (pos + 16 > b.Length || BitConverter.ToUInt32(b, pos) != 0xfeeda1e5 || BitConverter.ToUInt32(b, pos + 12) != 0)
                        throw new InvalidDataException("Unsupported save format; use the actual save data or decoded JSON, not its metadata/index.");
                    int c = checked((int)BitConverter.ToUInt32(b, pos + 4)), n = checked((int)BitConverter.ToUInt32(b, pos + 8));
                    pos += 16;
                    if (c <= 0 || c > b.Length - pos || n <= 0 || output.Length + n > FileLimit)
                        throw new InvalidDataException("Save bounds");
                    byte[] block = new byte[c];
                    Buffer.BlockCopy(b, pos, block, 0, c);
                    pos += c;
                    byte[] decoded = LZ4(block, n);
                    output.Write(decoded, 0, decoded.Length);
                }

                return output.ToArray();
            }
        }

        public static string Hash(string path)
        {
            using (var s = File.OpenRead(path))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        static bool IsStation(object b)
        {
            object t = Get(b, "BaseType", "peI");
            return Str(Get(t, "PersistentBaseTypes", "DPp")).Equals("PlayerSpaceStationBase", StringComparison.OrdinalIgnoreCase);
        }

        static bool RecordError(Exception e)
        {
            return e is FormatException || e is OverflowException || e is InvalidDataException;
        }

        public static Project Import(string file, string owner)
        {
            byte[] bytes = ReadSave(file);
            object doc = Json().DeserializeObject(Utf8.GetString(bytes).TrimEnd('\0'));
            // Prefer the active context, including expedition saves; also accept decoded player-only JSON.
            string contextName = Str(Get(doc, "ActiveContext", "XTp"));
            bool expedition = contextName.Equals("Expedition", StringComparison.OrdinalIgnoreCase) || contextName.Equals("Season", StringComparison.OrdinalIgnoreCase);
            object ctx = Get(doc, expedition ? "ExpeditionContext" : "BaseContext", expedition ? "2YS" : "vLc"), player = Get(ctx, "PlayerStateData", "6f=");
            if (player == null)
                player = Get(doc, "PlayerStateData", "6f=");
            if (player == null)
                throw new InvalidDataException("PlayerStateData missing. Select a save or decoded save JSON.");
            var p = new Project
            {
                SourceFile = Path.GetFileName(file),
                SourceHash = Hash(file)
            };
            object[] stations = Arr(Get(player, "PersistentPlayerBases", "F?0")).Where(IsStation).ToArray();
            string[] owners = stations.Select(x => Str(Get(Get(x, "Owner", "3?K"), "UID", "K7E"))).Where(x => x.Length > 0).Distinct().ToArray();
            if (string.IsNullOrEmpty(owner) && owners.Length == 1)
                owner = owners[0];
            // Multiple owners are not guessed: cache entries remain usable, ownership stays unconfirmed.
            p.Owner = owner ?? "";
            var systems = new Dictionary<string, SystemRecord>();
            var ambiguous = new HashSet<string>();
            foreach (object r in stations)
                try
                {
                    object own = Get(r, "Owner", "3?K");
                    if (p.Owner.Length == 0 || Str(Get(own, "UID", "K7E")) != p.Owner)
                        continue;
                    string u = Pack(Get(r, "GalacticAddress", "oZw"));
                    SystemRecord s;
                    if (!systems.TryGetValue(u, out s))
                    {
                        s = new SystemRecord
                        {
                            UA = u,
                            Owned = true,
                            Owner = p.Owner,
                            Evidence = "owned_station",
                            Selected = false
                        };
                        systems.Add(u, s);
                    }

                    s.Station = Repair(Str(Get(r, "Name", "NKm")));
                }
                catch (Exception e)
                {
                    if (!RecordError(e))
                        throw;
                    p.Skipped++;
                }

            foreach (object r in Arr(Get(player, "TeleportEndpoints", "nlG")))
                try
                {
                    if (!Str(Get(r, "TeleporterType", "iAF")).Equals("Spacestation", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string id = ID(Get(r, "AllianceId", "6H2"));
                    if (id.Length == 0)
                        continue;
                    string u = Pack(Get(r, "UniverseAddress", "yhJ"));
                    SystemRecord s;
                    if (!systems.TryGetValue(u, out s))
                    {
                        s = new SystemRecord
                        {
                            UA = u
                        };
                        systems.Add(u, s);
                    }

                    if (ambiguous.Contains(u))
                        continue;
                    if (s.Alliance.Length > 0 && s.Alliance != id)
                    {
                        s.Alliance = "";
                        s.Selected = false;
                        s.Evidence = "conflicting_cache";
                        s.Conflict = true;
                        ambiguous.Add(u);
                        p.Skipped++;
                        continue;
                    }

                    s.Alliance = id;
                    s.Selected = true;
                    s.Evidence = "save_cached_allianceid";
                    if (s.Station.Length == 0)
                        s.Station = Repair(Str(Get(r, "Name", "NKm")));
                    p.GetAlliance(id);
                }
                catch (Exception e)
                {
                    if (!RecordError(e))
                        throw;
                    p.Skipped++;
                }

            object dm = Get(doc, "DiscoveryManagerData", "fDu"), store = Get(Get(dm, "DiscoveryData-v1", "ETO"), "Store", "OsQ");
            foreach (object r in Arr(Get(store, "Record", "?fB")))
                try
                {
                    object dd = Get(r, "DD", "8P3");
                    if (Str(Get(dd, "DT", "<Dn")) != "SolarSystem")
                        continue;
                    SystemRecord s;
                    if (systems.TryGetValue(UA(Get(dd, "UA", "5L6")), out s))
                    {
                        string name = Repair(Str(Get(Get(r, "DM", "q9a"), "CN", "q5u")));
                        if (name.Length > 0)
                            s.Name = name;
                    }
                }
                catch (Exception e)
                {
                    if (!RecordError(e))
                        throw;
                    p.Skipped++;
                }

            p.Systems.AddRange(systems.Values);
            ImperialCore.Apply(p);
            return p;
        }

        public static int AssignOwned(Project p, string alliance)
        {
            Words(alliance);
            if (p.Owner.Length == 0)
                return 0;
            int n = 0;
            foreach (var r in p.Systems.Where(x => x.Owned && x.Owner == p.Owner && x.Alliance.Length == 0 && !x.Conflict && x.Evidence != "conflicting_cache"))
            {
                r.Alliance = alliance;
                r.Selected = true;
                r.Evidence = "user_profile_alliance";
                n++;
            }

            if (n > 0)
                p.GetAlliance(alliance);
            return n;
        }

        public static string Hex(string text)
        {
            byte[] b = Utf8.GetBytes(text);
            if (b.Length > 127)
            {
                int n = 127;
                while (n > 0 && (b[n] & 0xc0) == 0x80)
                    n--;
                Array.Resize(ref b, n);
            }

            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        public static string Unhex(string h)
        {
            if (h.Length % 2 != 0)
                throw new FormatException("UTF8 hex");
            byte[] b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
            return Utf8.GetString(b);
        }

        public static string Initials(string n)
        {
            if (n == "Imperio Español")
                return "IESP";
            return new string (n.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(x => char.ToUpperInvariant(x[0])).Where(char.IsLetter).Take(6).ToArray());
        }

        public static int Contrast58(int argb)
        {
            Color c = Color.FromArgb(argb);
            Func<byte, double> channel = v =>
            {
                double t = v / 255.0;
                return t <= .04045 ? t / 12.92 : Math.Pow((t + .055) / 1.055, 2.4);
            };
            double l = .2126 * channel(c.R) + .7152 * channel(c.G) + .0722 * channel(c.B);
            return (1.05 / (l + .05) >= (l + .05) / .05) ? Color.White.ToArgb() : Color.Black.ToArgb();
        }
    }
}
