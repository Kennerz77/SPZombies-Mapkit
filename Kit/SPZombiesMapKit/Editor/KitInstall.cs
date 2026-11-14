using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// The SPT install SPZombies is in: the kit reads SPZombies' models, gun list, chalk outlines and wall decals from it (so
    /// nothing of SPZombies is copied into your project), and installs your map's layout there to test it.
    /// </summary>
    internal static class KitInstall
    {
        private const string PrefRoot = "SPZombiesMapKit.SptRoot";

        public const string CoreFolder = "Kennerz_ZM_Core";
        public const string CoreDll = "Kennerz_ZM_Core.dll";

        /// <summary>The SPT folder (the one with BepInEx and SPT_Runtime), or "" when not set.</summary>
        public static string SptRoot
        {
            get
            {
                string set = EditorPrefs.GetString(PrefRoot, "");
                if (IsSpt(set))
                    return set;
                // The SDK Map Definition's own SPT folder
                string sdk = KitSdk.MapDefinitionSptRoot();
                return IsSpt(sdk) ? sdk : set;
            }
            set
            {
                EditorPrefs.SetString(PrefRoot, value ?? "");
                Forget();
            }
        }

        public static bool IsSpt(string root) => !string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, "BepInEx", "plugins"));

        public static string Plugins => Path.Combine(SptRoot, "BepInEx", "plugins");

        private static Dictionary<string, string> _found;
        private static List<Gun> _guns;
        private static List<string> _decals;

        /// <summary>Forget what was read (the folder changed, or SPZombies was updated).</summary>
        public static void Forget()
        {
            _found = null;
            _guns = null;
            _decals = null;
        }

        /// <summary>A file of one of the mods, by name, in BepInEx\plugins\&lt;mod&gt;\ (or deeper, once found), or null.</summary>
        public static string Find(string fileName)
        {
            if (_found == null)
                _found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_found.TryGetValue(fileName, out string had))
                return had;
            string path = null;
            if (IsSpt(SptRoot))
            {
                try
                {
                    // One level of mod folders first (fast), then anywhere under plugins
                    foreach (string dir in Directory.GetDirectories(Plugins))
                    {
                        string p = Path.Combine(dir, fileName);
                        if (File.Exists(p))
                        {
                            path = p;
                            break;
                        }
                    }
                    if (path == null)
                        path = Directory.EnumerateFiles(Plugins, fileName, SearchOption.AllDirectories).FirstOrDefault();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SPZombies Kit] Looking for {fileName}: {e.Message}");
                }
            }
            _found[fileName] = path;
            return path;
        }

        /// <summary>SPZombies' own plugin folder, or null when it isn't installed there.</summary>
        public static string CoreDir
        {
            get
            {
                string dll = Find(CoreDll);
                return dll != null ? Path.GetDirectoryName(dll) : null;
            }
        }

        public static bool CoreInstalled => CoreDir != null;

        public const string CoreGuid = "com.kennerz.zmcore";
        private static string _versionOf;
        private static DateTime _versionStamp;
        private static string _version = "";

        /// <summary>
        /// SPZombies' version as its plugin states it, or "". The DLL's file version isn't stamped (it reads 1.0.0.0), so
        /// the version comes from the plugin's own attribute in the DLL: the GUID string, then its name, then its version.
        /// </summary>
        public static string CoreVersion
        {
            get
            {
                try
                {
                    string dll = Find(CoreDll);
                    if (dll == null)
                        return "";
                    DateTime stamp = File.GetLastWriteTimeUtc(dll);
                    if (dll == _versionOf && stamp == _versionStamp)
                        return _version;
                    _versionOf = dll;
                    _versionStamp = stamp;
                    _version = PluginVersion(File.ReadAllBytes(dll), CoreGuid) ?? "";
                    return _version;
                }
                catch
                {
                    return "";
                }
            }
        }

        /// <summary>SPZombies older than the kit needs (or a version that can't be read: treated as fine).</summary>
        public static bool CoreTooOld => Version.TryParse(CoreVersion, out Version v) && v < KitInfo.CoreNeeded;

        // A plugin attribute's arguments are stored one after another as length-prefixed strings: find the GUID, skip
        // the name, read the version
        private static string PluginVersion(byte[] b, string guid)
        {
            byte[] g = System.Text.Encoding.UTF8.GetBytes(guid);
            for (int i = 1; i + g.Length < b.Length; i++)
            {
                if (b[i - 1] != g.Length || !Same(b, i, g))
                    continue;
                int at = i + g.Length;
                string name = Read(b, ref at), version = name != null ? Read(b, ref at) : null;
                if (version != null && Version.TryParse(version, out _))
                    return version;
            }
            return null;
        }

        private static bool Same(byte[] b, int at, byte[] g)
        {
            for (int k = 0; k < g.Length; k++)
                if (b[at + k] != g[k])
                    return false;
            return true;
        }

        private static string Read(byte[] b, ref int at)
        {
            if (at >= b.Length)
                return null;
            int len = b[at];
            if (len == 0 || len > 0x7f || at + 1 + len > b.Length)
                return null;
            string s = System.Text.Encoding.UTF8.GetString(b, at + 1, len);
            at += 1 + len;
            return s;
        }

        // ---------- Guns ----------

        internal class Gun
        {
            public string Id, Name, Role, Class;
        }

        /// <summary>Every gun a wall buy can sell (SPZombies' gun list), the wall's own first, then grenades.</summary>
        public static List<Gun> Guns()
        {
            if (_guns != null)
                return _guns;
            _guns = new List<Gun>();
            string file = CoreDir != null ? Path.Combine(CoreDir, "box_builds.json") : null;
            if (file != null && File.Exists(file))
            {
                try
                {
                    if (KitJson.Parse(File.ReadAllText(file)) is List<object> all)
                        foreach (object o in all)
                            if (o is KitJson.JsonObject g && !string.IsNullOrEmpty(g.Str("id")))
                                _guns.Add(new Gun { Id = g.Str("id"), Name = g.Str("name") ?? g.Str("id"), Role = g.Str("role") ?? "", Class = g.Str("class") ?? "" });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SPZombies Kit] Couldn't read {file}: {e.Message}");
                }
            }
            _guns = _guns.OrderBy(g => g.Role == "wall" ? 0 : 1).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _guns.Insert(0, new Gun { Id = KitInfo.GrenadesWallId, Name = "Grenades (M67)", Role = "wall", Class = "grenade" });
            return _guns;
        }

        public static Gun FindGun(string id) => Guns().FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

        public static string GunName(string id) => FindGun(id)?.Name ?? (string.IsNullOrEmpty(id) ? "(no gun)" : id);

        /// <summary>The wall buy's chalk outline picture (SPZombies draws one per gun), or null.</summary>
        public static string ChalkPicture(string gunId)
        {
            if (CoreDir == null || string.IsNullOrEmpty(gunId))
                return null;
            string p = Path.Combine(CoreDir, "chalk", gunId + ".png");
            return File.Exists(p) ? p : null;
        }

        // ---------- Decals ----------

        public static List<string> DecalNames()
        {
            if (_decals != null)
                return _decals;
            string dir = CoreDir != null ? Path.Combine(CoreDir, "decals") : null;
            _decals = dir != null && Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.png").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList()
                : KitInfo.DecalLooks.ToList();
            return _decals;
        }

        public static string DecalPicture(string name)
        {
            if (CoreDir == null || string.IsNullOrEmpty(name))
                return null;
            string p = Path.Combine(CoreDir, "decals", name + ".png");
            return File.Exists(p) ? p : null;
        }

        /// <summary>
        /// A gun's chalk outline trimmed to the drawing and centred (SPZombies' saved pictures can have wide empty margins on
        /// one side), or null when there's none or it barely drew (too few chalk lines to show). Not an asset: destroy it.
        /// </summary>
        public static Texture2D LoadChalk(string gunId)
        {
            // SPZombies saves no picture of the grenades' wall chalk (it's drawn from an M67 in the match): a drawn one
            if (gunId == KitInfo.GrenadesWallId && ChalkPicture(gunId) == null)
                return GrenadeChalk();
            Texture2D full = LoadPicture(ChalkPicture(gunId));
            if (full == null)
                return null;
            Color32[] px = full.GetPixels32();
            int w = full.width, h = full.height, minX = w, minY = h, maxX = -1, maxY = -1, count = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 40)
                    {
                        count++;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            // Too little drawn to read as a gun (a broken picture)
            if (count < 600 || maxX - minX < 24 || maxY - minY < 12)
            {
                UnityEngine.Object.DestroyImmediate(full);
                return null;
            }
            const int Pad = 6;
            minX = Mathf.Max(0, minX - Pad);
            minY = Mathf.Max(0, minY - Pad);
            maxX = Mathf.Min(w - 1, maxX + Pad);
            maxY = Mathf.Min(h - 1, maxY + Pad);
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            var trimmed = new Texture2D(cw, ch, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            trimmed.SetPixels(full.GetPixels(minX, minY, cw, ch));
            trimmed.Apply();
            UnityEngine.Object.DestroyImmediate(full);
            return trimmed;
        }

        // A chalk outline of an M67: round body, fuse on top, spoon down its side, pull ring
        private static Texture2D GrenadeChalk()
        {
            const int W = 200, H = 280;
            var px = new Color32[W * H];
            var rng = new System.Random(115);
            void Dot(float x, float y)
            {
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (dx * dx + dy * dy > 9 || rng.NextDouble() < 0.18)
                            continue;
                        int ix = Mathf.RoundToInt(x) + dx, iy = Mathf.RoundToInt(y) + dy;
                        if (ix >= 0 && iy >= 0 && ix < W && iy < H)
                            px[iy * W + ix] = new Color32(235, 235, 230, 230);
                    }
            }
            void Line(float x0, float y0, float x1, float y1)
            {
                int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)));
                for (int i = 0; i <= steps; i++)
                    Dot(Mathf.Lerp(x0, x1, i / (float)steps), Mathf.Lerp(y0, y1, i / (float)steps));
            }
            for (int a = 0; a < 360; a++)
                Dot(95f + 72f * Mathf.Cos(a * Mathf.Deg2Rad), 100f + 88f * Mathf.Sin(a * Mathf.Deg2Rad));
            Line(72f, 186f, 72f, 222f);
            Line(118f, 186f, 118f, 222f);
            Line(72f, 222f, 118f, 222f);
            Line(118f, 218f, 160f, 196f);
            Line(160f, 196f, 172f, 120f);
            Line(172f, 120f, 168f, 70f);
            for (int a = 0; a < 360; a += 2)
                Dot(58f + 20f * Mathf.Cos(a * Mathf.Deg2Rad), 240f + 20f * Mathf.Sin(a * Mathf.Deg2Rad));
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Whether SPZombies' chalk picture of this gun is missing or broken (the kit shows a plain board).</summary>
        public static bool ChalkBroken(string gunId)
        {
            if (gunId == KitInfo.GrenadesWallId || ChalkPicture(gunId) == null)
                return false;
            Texture2D t = LoadChalk(gunId);
            if (t == null)
                return true;
            UnityEngine.Object.DestroyImmediate(t);
            return false;
        }

        /// <summary>A picture file as a texture (not an asset; destroy it when done), or null.</summary>
        public static Texture2D LoadPicture(string path)
        {
            if (path == null || !File.Exists(path))
                return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            if (tex.LoadImage(File.ReadAllBytes(path)))
                return tex;
            UnityEngine.Object.DestroyImmediate(tex);
            return null;
        }
    }
}
