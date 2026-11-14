using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Writes the zombies layout SPZombies reads (BepInEx\plugins\SPZombies_Maps\ZM_&lt;Name&gt;\&lt;mapId&gt;zombies.json, plus the
    /// map's sounds, pictures, tips, nav links and game over shots beside it), installs it into the SPT folder to test, makes
    /// the release zip, and reads a layout file back in as markers.
    /// </summary>
    internal static class KitExport
    {
        public const string ExportFolder = "SPZombiesExport";
        public const string KitRootName = "SPZombies";
        public const string LooksRootName = "SPZombies Custom Looks";
        public const string DataFolder = "Assets/SPZombiesMapData";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------- Finding things ----------

        public static ZombiesMapSettings Settings() =>
            Object.FindObjectsOfType<ZombiesMapSettings>(true).FirstOrDefault(s => s != null && s.gameObject.scene.IsValid());

        public static List<ZombiesMapSettings> AllSettings() =>
            Object.FindObjectsOfType<ZombiesMapSettings>(true).Where(s => s != null && s.gameObject.scene.IsValid()).ToList();

        /// <summary>Every component of a type in the open scenes, in hierarchy order (scene by scene, top to bottom).</summary>
        public static List<T> InOrder<T>(bool activeOnly = true) where T : Behaviour
        {
            var list = new List<T>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (T c in root.GetComponentsInChildren<T>(true))
                        if (!activeOnly || (c.enabled && c.gameObject.activeInHierarchy))
                            list.Add(c);
            }
            return list;
        }

        /// <summary>The kit's own root object (editor only) in the map's scene: settings on it, markers under it.</summary>
        public static Transform KitRoot(Scene scene, bool make = true)
        {
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == KitRootName);
            if (root == null && make)
            {
                root = new GameObject(KitRootName) { tag = "EditorOnly" };
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "SPZombies root");
            }
            return root != null ? root.transform : null;
        }

        /// <summary>The scene the zombies layout lives in: the map settings' scene, else the active scene.</summary>
        public static Scene MapScene()
        {
            ZombiesMapSettings s = Settings();
            return s != null ? s.gameObject.scene : SceneManager.GetActiveScene();
        }

        public static ZombiesMapSettings MakeSettings(Scene scene)
        {
            Transform root = KitRoot(scene);
            // (In the editor a missing component is a "fake null": compared with ==, never with ??)
            ZombiesMapSettings s = root.GetComponent<ZombiesMapSettings>();
            if (s == null)
                s = Undo.AddComponent<ZombiesMapSettings>(root.gameObject);
            KitSdk.MapDef def = KitSdk.For(null);
            if (string.IsNullOrEmpty(s.MapId) && def != null)
                s.MapId = def.MapId;
            if (string.IsNullOrEmpty(s.MapName))
                s.MapName = Clean(Pretty(s.MapId));
            EditorSceneManager.MarkSceneDirty(scene);
            return s;
        }

        private static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "MyMap";
            string s = id.StartsWith("zm_") ? id.Substring(3) : id;
            return string.Concat(s.Split('_').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        }

        /// <summary>The folder a kind's markers are kept in under the kit's root.</summary>
        public static string Category(ZombiesKind k)
        {
            switch (k)
            {
                case ZombiesKind.ZombieSpawn:
                case ZombiesKind.PlayerStart: return "Spawns";
                case ZombiesKind.Window: return "Barricades";
                case ZombiesKind.PerkMachine:
                case ZombiesKind.PackAPunch:
                case ZombiesKind.BoxSpot: return "Machines and box";
                case ZombiesKind.WallBuy: return "Wall buys";
                case ZombiesKind.Door:
                case ZombiesKind.Debris: return "Doors and debris";
                case ZombiesKind.Zone:
                case ZombiesKind.Blocker:
                case ZombiesKind.PlayAreaWall: return "Zones and walls";
                case ZombiesKind.PowerSwitch:
                case ZombiesKind.PowerLight:
                case ZombiesKind.Lamp: return "Power and lights";
                case ZombiesKind.ExfilActivator:
                case ZombiesKind.ExfilHold:
                case ZombiesKind.ExfilZone: return "Exfil";
                default: return "Extras";
            }
        }

        public static Transform Folder(Scene scene, string name)
        {
            Transform root = KitRoot(scene);
            Transform f = root.Find(name);
            if (f == null)
            {
                f = new GameObject(name) { tag = "EditorOnly" }.transform;
                f.SetParent(root, false);
                Undo.RegisterCreatedObjectUndo(f.gameObject, "SPZombies folder");
            }
            return f;
        }

        public static string Clean(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>The folder in BepInEx\plugins every zombies map goes in (SPZombies 0.1.2+ ships it and reads it).</summary>
        public const string MapsFolder = "SPZombies_Maps";

        /// <summary>Each map's own folder in it starts with this: ZM_&lt;MapName&gt;.</summary>
        public const string MapFolderPrefix = "ZM_";

        // Folders an imported layout can come from: a map's ZM_<Name> folder, or SPZombies' own older Kennerz_ZM_<Name>\maps
        private static readonly string[] KnownFolderPrefixes = { MapFolderPrefix, "Kennerz_ZM_" };

        /// <summary>The map's own folder: ZM_ and its name (a name already starting with zm_ isn't doubled).</summary>
        public static string FolderName(ZombiesMapSettings s)
        {
            string name = Clean(s.MapName).Length > 0 ? Clean(s.MapName) : Clean(Pretty(s.MapId));
            if (name.StartsWith(MapFolderPrefix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(MapFolderPrefix.Length);
            return MapFolderPrefix + (name.Length > 0 ? name : "MyMap");
        }

        /// <summary>Where the map's zombies files go in an SPT folder, for showing: BepInEx\plugins\SPZombies_Maps\ZM_&lt;Name&gt;.</summary>
        public static string InstallPath(ZombiesMapSettings s) => $"BepInEx\\plugins\\{MapsFolder}\\{FolderName(s)}";

        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static string ExportRoot(ZombiesMapSettings s) => Path.Combine(ProjectRoot, ExportFolder, Clean(s.MapId));

        /// <summary>The map's own folder in the export (its layout, sounds, pictures and tips sit straight in it).</summary>
        public static string PluginDir(ZombiesMapSettings s) => Path.Combine(ExportRoot(s), "BepInEx", "plugins", MapsFolder, FolderName(s));

        public static string MapsDir(ZombiesMapSettings s) => PluginDir(s);

        // ---------- Custom looks ----------

        /// <summary>The name of a custom look's copy in the map (SPZombies finds the look by this name in a match).</summary>
        public static string LookName(GameObject look)
        {
            string id = GlobalObjectId.GetGlobalObjectIdSlow(look).ToString();
            uint h = 2166136261;
            foreach (char c in id)
                h = (h ^ c) * 16777619;
            string n = Clean(look.name).ToLowerInvariant();
            if (n.Length > 32)
                n = n.Substring(0, 32);
            return $"spz_{n}_{h & 0xffffff:x6}";
        }

        /// <summary>
        /// Every custom look in use gets a copy in the map, far under it (SPZombies copies the look from it in a match): the
        /// look's scripts off, nothing static, one level of detail group on its root (how SPZombies recognises a prop).
        /// Copies no marker uses any more are removed. True when the scene changed (save it and build the map again).
        /// </summary>
        public static bool EnsureLooks(List<ZombiesMarker> markers, Scene scene, List<string> log)
        {
            var looks = markers.Where(m => m.CustomLook != null && KitInfo.CanHaveCustomLook(m.Kind)).Select(m => m.CustomLook).Distinct().ToList();
            GameObject rootGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == LooksRootName);
            if (looks.Count == 0 && rootGo == null)
                return false;
            bool changed = false;
            if (rootGo == null)
            {
                rootGo = new GameObject(LooksRootName);
                SceneManager.MoveGameObjectToScene(rootGo, scene);
                rootGo.transform.position = new Vector3(0f, -500f, 0f);
                Undo.RegisterCreatedObjectUndo(rootGo, "SPZombies custom looks");
                changed = true;
            }
            Transform root = rootGo.transform;
            var wanted = new HashSet<string>();
            foreach (GameObject look in looks)
            {
                string name = LookName(look);
                wanted.Add(name);
                if (root.Find(name) != null)
                    continue;
                var copy = (GameObject)Object.Instantiate(look);
                copy.name = name;
                copy.transform.SetParent(root, false);
                copy.transform.localPosition = new Vector3(root.childCount * 12f, 0f, 0f);
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = look.transform.lossyScale;
                copy.SetActive(true);
                MakeTemplate(copy);
                Undo.RegisterCreatedObjectUndo(copy, "SPZombies custom look");
                log.Add($"Custom look '{look.name}' copied into the map as {name}.");
                changed = true;
            }
            foreach (Transform old in root.Cast<Transform>().ToList())
                if (!wanted.Contains(old.name))
                {
                    Undo.DestroyObjectImmediate(old.gameObject);
                    changed = true;
                }
            if (root.childCount == 0)
            {
                Undo.DestroyObjectImmediate(rootGo);
                changed = true;
            }
            if (changed)
                EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        private static void MakeTemplate(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                if (t.gameObject.CompareTag("EditorOnly"))
                    t.gameObject.tag = "Untagged";
            }
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null)
                    Object.DestroyImmediate(mb);
            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(rb);
            // One detail group on the root, holding the closest detail of every mesh
            LODGroup[] inner = go.GetComponentsInChildren<LODGroup>(true);
            var close = new List<Renderer>();
            var far = new HashSet<Renderer>();
            foreach (LODGroup g in inner)
            {
                LOD[] lods = g.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null)
                        {
                            if (i == 0)
                                close.Add(r);
                            else
                                far.Add(r);
                        }
            }
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                if (!close.Contains(r) && !far.Contains(r))
                    close.Add(r);
            foreach (Renderer r in far)
                if (!close.Contains(r))
                    r.enabled = false;
            foreach (LODGroup g in inner)
                Object.DestroyImmediate(g);
            LODGroup group = go.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.005f, close.ToArray()) });
            group.RecalculateBounds();
        }

        // ---------- The layout ----------

        private static float Norm(float degrees)
        {
            degrees = Mathf.Repeat(degrees, 360f);
            return degrees > 180f ? degrees - 360f : degrees;
        }

        /// <summary>A marker's "What" as SPZombies reads it.</summary>
        public static string WhatOf(ZombiesMarker m)
        {
            if (m.Kind == ZombiesKind.Zone)
                return Mathf.Max(1, m.Zone).ToString(Inv);
            if (m.CustomLook != null && KitInfo.CanHaveCustomLook(m.Kind))
                return LookName(m.CustomLook);
            return m.What ?? "";
        }

        public static KitJson.JsonObject Entry(ZombiesMarker m)
        {
            Transform t = m.transform;
            bool sized = ZombiesMarker.IsSized(m.Kind);
            Vector3 lossy = m.Size;
            float scale = lossy.x > 0.01f ? lossy.x : 1f;
            Vector3 p = t.position, eu = t.eulerAngles;
            var e = new KitJson.JsonObject();
            e["Type"] = m.Kind.ToString();
            e["What"] = WhatOf(m);
            e["Price"] = KitJson.Num(KitInfo.HasPrice(m.Kind) ? m.Price : 0);
            e["Position"] = KitJson.Nums(p.x, p.y, p.z);
            e["Turn"] = KitJson.Num(Mathf.Repeat(eu.y, 360f));
            e["Tilt"] = KitJson.Num(Norm(eu.x));
            e["Roll"] = KitJson.Num(Norm(eu.z));
            e["Scale"] = KitJson.Num(sized ? 1f : scale);
            e["Size"] = sized ? KitJson.Nums(lossy.x, lossy.y, m.Kind == ZombiesKind.Decal ? 0.02f : lossy.z) : null;
            bool uneven = m.Kind == ZombiesKind.Rock && (Mathf.Abs(lossy.y - lossy.x) > 0.01f || Mathf.Abs(lossy.z - lossy.x) > 0.01f);
            e["Stretch"] = uneven ? KitJson.Nums(1f, lossy.y / scale, lossy.z / scale) : null;
            e["Locked"] = true;
            e["OpensWithPower"] = (m.Kind == ZombiesKind.Door || m.Kind == ZombiesKind.Debris) && m.OpensWithPower;
            e["NoBoard"] = m.Kind == ZombiesKind.WallBuy && m.NoBoard;
            e["Zone"] = KitJson.Num(m.Kind == ZombiesKind.ZombieSpawn || m.Kind == ZombiesKind.Window ? Mathf.Max(0, m.Zone) : 0);
            return e;
        }

        public static KitJson.JsonObject Layout(ZombiesMapSettings s, List<ZombiesMarker> markers, List<ZombiesZoneOutline> outlines)
        {
            var o = new KitJson.JsonObject();
            o["Map"] = s.MapId;
            o["Hour"] = s.HoldTime ? KitJson.Num(Mathf.Clamp(s.Hour, 0f, 24f)) : null;
            if (s.HoldWeather)
            {
                var w = new KitJson.JsonObject();
                w["Clouds"] = KitJson.Num(s.Clouds);
                w["Rain"] = KitJson.Num(s.Rain);
                w["Fog"] = KitJson.Num(s.Fog);
                w["Wind"] = KitJson.Num(s.Wind);
                o["Weather"] = w;
            }
            else
                o["Weather"] = null;
            o["ZombiesMap"] = true;
            o["MapSelectSpot"] = string.IsNullOrEmpty(s.MapSelectSpot) ? null : s.MapSelectSpot;
            o["Moonlight"] = s.Moonlight;
            o["MaxPerks"] = KitJson.Num(Mathf.Max(0, s.MaxPerks));
            int boxStart = markers.FindIndex(m => m.Kind == ZombiesKind.BoxSpot && m.BoxStartsHere);
            o["BoxStart"] = KitJson.Num(boxStart);
            o["AlwaysPowered"] = s.AlwaysPowered;
            // Left out when normal, so older SPZombies versions read the same file
            o["StartGun"] = string.IsNullOrEmpty(s.StartGun) || s.StartGun == KitInfo.DefaultStartGun ? null : s.StartGun;
            KitJson.JsonObject prices = null;
            if (s.CustomPerkPrices)
                foreach (string id in KitInfo.PerkIds.Concat(new[] { "revive" }).Distinct())
                {
                    int p = s.PriceOf(id);
                    if (p <= 0 || p == KitInfo.PerkDefaultPrice(id))
                        continue;
                    prices = prices ?? new KitJson.JsonObject();
                    prices[id] = KitJson.Num(p);
                }
            o["PerkPrices"] = prices;
            o["Entries"] =markers.Select(m => (object)Entry(m)).ToList();
            var zones = new List<object>();
            foreach (ZombiesZoneOutline z in outlines)
            {
                List<Vector3> pts = z.WorldCorners();
                if (pts.Count < 3)
                    continue;
                var zo = new KitJson.JsonObject();
                zo["Id"] = KitJson.Num(Mathf.Max(1, z.Id));
                zo["Points"] = pts.Select(q => (object)KitJson.Nums(q.x, q.y, q.z)).ToList();
                zo["Height"] = KitJson.Num(z.Height);
                zo["Below"] = KitJson.Num(z.Below);
                zones.Add(zo);
            }
            o["Zones"] = zones;
            return o;
        }

        /// <summary>Writes the layout and the map's files into the project's SPZombiesExport folder. Null on success, else why not.</summary>
        public static string Export(out string summary)
        {
            summary = "";
            ZombiesMapSettings s = Settings();
            if (s == null)
                return "There are no map settings yet: open the kit's Map tab and press Add map settings.";
            if (string.IsNullOrEmpty(Clean(s.MapId)))
                return "The map id is empty (Map tab): use the SDK Map Definition's Map Id.";
            if (AllSettings().Count > 1)
                return "There's more than one Zombies Map Settings in the open scenes: keep one.";
            List<ZombiesMarker> markers = InOrder<ZombiesMarker>();
            var log = new List<string>();
            bool looksChanged = EnsureLooks(markers, s.gameObject.scene, log);

            string dir = MapsDir(s);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string id = s.MapId;
            File.WriteAllText(Path.Combine(dir, id + "zombies.json"), KitJson.Write(Layout(s, markers, InOrder<ZombiesZoneOutline>())));

            CopyWav(s.LoadingMusic, Path.Combine(dir, id + "_loading.wav"), log);
            CopyWav(s.Ambience, Path.Combine(dir, id + "_ambience.wav"), log);
            CopyWav(s.EasterEggSong, Path.Combine(dir, id + "_eesong.wav"), log);
            CopyPicture(s.Banner, Path.Combine(dir, id + "_banner"), false, log);
            CopyPicture(s.LoadingPicture, Path.Combine(dir, id + "_lore"), true, log);
            if (!string.IsNullOrWhiteSpace(s.Tips))
                File.WriteAllText(Path.Combine(dir, id + "_tips.txt"),
                    $"# Loading screen tips for {id}, shuffled in with the mode's own.\n# One tip a line. Lines starting with # are skipped.\n{s.Tips.Trim()}\n");

            List<ZombiesNavLink> links = InOrder<ZombiesNavLink>();
            if (links.Count > 0)
            {
                var sb = new StringBuilder("# Walk-across links on the navmesh: x1 y1 z1 x2 y2 z2 width [oneway]\n");
                foreach (ZombiesNavLink l in links)
                {
                    Vector3 a = l.transform.position, b = l.WorldEnd;
                    sb.AppendLine(string.Format(Inv, "{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} {6:0.##}{7}",
                        a.x, a.y, a.z, b.x, b.y, b.z, l.Width, l.OneWay ? " oneway" : ""));
                }
                File.WriteAllText(Path.Combine(dir, id + "_navlinks.txt"), sb.ToString());
            }
            List<ZombiesGameOverShot> shots = InOrder<ZombiesGameOverShot>();
            if (shots.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (ZombiesGameOverShot shot in shots)
                {
                    Vector3 p = shot.transform.position, e = shot.transform.eulerAngles;
                    sb.Append(string.Format(Inv, "{0:0.00} {1:0.00} {2:0.00} {3:0.0} {4:0.0}\n", p.x, p.y, p.z, e.y, e.x));
                }
                File.WriteAllText(Path.Combine(dir, id + "_gameover_shots.txt"), sb.ToString());
            }

            var counts = markers.GroupBy(m => m.Kind).OrderBy(g => g.Key).Select(g => $"{g.Count()} {KitInfo.KindName(g.Key).ToLowerInvariant()}");
            summary = $"Exported {markers.Count} things ({string.Join(", ", counts)}) to {dir}.";
            if (log.Count > 0)
                summary += "\n" + string.Join("\n", log);
            if (looksChanged)
                summary += "\nThe map scene changed (custom looks): save it and build the map again.";
            return null;
        }

        private static void CopyWav(AudioClip clip, string to, List<string> log)
        {
            if (clip == null)
                return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                log.Add($"{clip.name}: not a .wav file, left out (SPZombies reads 16-bit PCM .wav).");
                return;
            }
            string problem = WavProblem(path);
            if (problem != null)
                log.Add($"{clip.name}: {problem}");
            File.Copy(path, to, true);
        }

        private static void CopyPicture(Texture2D tex, string toWithoutExt, bool jpgOk, List<string> log)
        {
            if (tex == null)
                return;
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png" || (jpgOk && (ext == ".jpg" || ext == ".jpeg")))
            {
                File.Copy(path, toWithoutExt + (ext == ".png" ? ".png" : ".jpg"), true);
                return;
            }
            // Other picture files: made into a png
            var t = new Texture2D(2, 2);
            if (t.LoadImage(File.ReadAllBytes(path)))
                File.WriteAllBytes(toWithoutExt + ".png", t.EncodeToPNG());
            else
                log.Add($"{tex.name}: couldn't be read as a picture (use a .png{(jpgOk ? " or .jpg" : "")}).");
            Object.DestroyImmediate(t);
        }

        /// <summary>Why a .wav file won't play in SPZombies (it reads 16-bit PCM), or null when it's fine.</summary>
        public static string WavProblem(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 44 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
                    return "isn't a WAV file.";
                // SPZombies reads the format straight after the header: a file with other data first plays wrong
                if (Encoding.ASCII.GetString(b, 12, 4) != "fmt ")
                    return "has extra data before its format, so SPZombies can't read it.";
                int i = 12;
                while (i + 8 <= b.Length)
                {
                    string chunk = Encoding.ASCII.GetString(b, i, 4);
                    int size = BitConverter.ToInt32(b, i + 4);
                    if (chunk == "fmt " && i + 24 <= b.Length)
                    {
                        int format = BitConverter.ToUInt16(b, i + 8);
                        int bits = BitConverter.ToUInt16(b, i + 22);
                        if (format != 1 && format != 0xFFFE)
                            return "isn't PCM (SPZombies plays 16-bit PCM .wav).";
                        return bits == 16 ? null : $"is {bits}-bit: SPZombies plays 16-bit PCM .wav.";
                    }
                    i += 8 + size + (size & 1);
                }
                return "has no format chunk.";
            }
            catch (Exception e)
            {
                return "can't be read: " + e.Message;
            }
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from))
                CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // ---------- Install and release ----------

        /// <summary>Exports, then copies the map's zombies folder into the SPT folder. Null on success, else why not.</summary>
        public static string Install(out string summary)
        {
            string error = Export(out summary);
            if (error != null)
                return error;
            if (!KitInstall.IsSpt(KitInstall.SptRoot))
                return "Set the SPT folder first (Setup tab).";
            ZombiesMapSettings s = Settings();
            string dest = Path.Combine(KitInstall.Plugins, MapsFolder, FolderName(s));
            // Never over another map's folder without asking
            string destMaps = dest;
            if (Directory.Exists(destMaps))
            {
                foreach (string f in Directory.GetFiles(destMaps, "*zombies.json"))
                {
                    try
                    {
                        if (KitJson.Parse(File.ReadAllText(f)) is KitJson.JsonObject o && o.Str("Map") != s.MapId
                            && !EditorUtility.DisplayDialog("SPZombies Map Kit", $"{dest} holds the layout of another map ({o.Str("Map")}). Replace it?", "Replace", "Cancel"))
                            return "Not installed.";
                    }
                    catch
                    {
                    }
                }
                Directory.Delete(destMaps, true);
            }
            CopyDir(PluginDir(s), dest);
            summary += $"\nInstalled to {dest}. Restart the SPT server and the game to play it (zombies mode, map screen).";
            return null;
        }

        /// <summary>
        /// The release zip: the SDK's map package (bundles + server files, made by Build Map with Create Package on) and the
        /// zombies folder together, with a read-me, ready to extract into an SPT folder. Null on success, else why not.
        /// </summary>
        public static string ReleaseZip(out string summary)
        {
            string error = Export(out summary);
            if (error != null)
                return error;
            ZombiesMapSettings s = Settings();
            KitSdk.MapDef def = KitSdk.For(s.MapId);
            string package = KitSdk.PackageFolder(def);
            string outDir = def != null ? KitSdk.OutputDirectory(def) : Path.Combine(ProjectRoot, ExportFolder);
            string staging = Path.Combine(ProjectRoot, ExportFolder, "release_staging", Clean(s.MapId));
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            if (package != null)
                CopyDir(package, staging);
            else
                summary += "\nNo SDK map package found: build the map with Create Package ticked on its Map Definition first, or the zip has only the zombies layout.";
            CopyDir(Path.Combine(ExportRoot(s), "BepInEx"), Path.Combine(staging, "BepInEx"));
            string name = Clean(s.MapName).Length > 0 ? Clean(s.MapName) : Clean(Pretty(s.MapId));
            File.WriteAllText(Path.Combine(staging, name + "_SPZombies_README.txt"), ReadMe(s, name));
            Directory.CreateDirectory(outDir);
            string zip = Path.Combine(outDir, $"{name}_for_SPZombies.zip");
            if (File.Exists(zip))
                File.Delete(zip);
            ZipFile.CreateFromDirectory(staging, zip);
            summary += $"\nRelease zip: {zip}";
            EditorUtility.RevealInFinder(zip);
            return null;
        }

        private static string ReadMe(ZombiesMapSettings s, string name) =>
            $"{name}: a zombies map for Dr K's SPZombies (map id {s.MapId}).\r\n\r\n" +
            "Needs:\r\n" +
            "- SPT 4.1.x\r\n" +
            "- Dr K's SPZombies (and what it needs: Pack-a-Punch, Perk Beverages, Ray Gun, Thundergun)\r\n" +
            "- The Tushonka Mapping SDK's CustomMapLoader (client and server)\r\n\r\n" +
            "Installing: close the game and the SPT server, extract this zip into your SPT folder (the one with the SPT_Runtime\r\n" +
            "folder), then start the server and the game. The map shows on the map screen in zombies mode.\r\n\r\n" +
            $"Fika headless: copy the BepInEx\\plugins\\CustomMapLoader map files and {InstallPath(s)} onto the headless too.\r\n\r\n" +
            $"Made with the SPZombies Map Kit {KitInfo.KitVersion}.\r\n";

        // ---------- Reading a layout back in ----------

        /// <summary>
        /// Reads a layout file (from SPZombies' F8 editor or a kit export) into markers, zone outlines, nav links and game
        /// over shots in the map scene, with the map's settings, sounds and pictures. Returns what it did.
        /// </summary>
        public static string Import(string file, bool replace)
        {
            if (!(KitJson.Parse(File.ReadAllText(file)) is KitJson.JsonObject o))
                return "That file isn't a layout.";
            Scene scene = MapScene();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Import zombies layout");
            if (replace)
            {
                foreach (ZombiesMarker m in InOrder<ZombiesMarker>(false))
                    Undo.DestroyObjectImmediate(m.gameObject);
                foreach (ZombiesZoneOutline z in InOrder<ZombiesZoneOutline>(false))
                    Undo.DestroyObjectImmediate(z.gameObject);
                foreach (ZombiesNavLink l in InOrder<ZombiesNavLink>(false))
                    Undo.DestroyObjectImmediate(l.gameObject);
                foreach (ZombiesGameOverShot g in InOrder<ZombiesGameOverShot>(false))
                    Undo.DestroyObjectImmediate(g.gameObject);
            }
            ZombiesMapSettings s = Settings() ?? MakeSettings(scene);
            Undo.RecordObject(s, "Import zombies layout");
            string id = o.Str("Map") ?? s.MapId;
            s.MapId = id;
            // The map's folder name (SPZombies_Maps\ZM_<Name>\..., or SPZombies' own older Kennerz_ZM_<Name>\maps\...) gives its name
            string dirOfFile = Path.GetDirectoryName(file) ?? "";
            string folder = Path.GetFileName(dirOfFile);
            if (string.Equals(folder, "maps", StringComparison.OrdinalIgnoreCase))
                folder = Path.GetFileName(Path.GetDirectoryName(dirOfFile) ?? "");
            string prefix = KnownFolderPrefixes.FirstOrDefault(p => folder.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            if (prefix != null && folder.Length > prefix.Length)
                s.MapName = folder.Substring(prefix.Length);
            else if (string.IsNullOrEmpty(s.MapName))
                s.MapName = Clean(Pretty(id));
            s.HoldTime = o["Hour"] is KitJson.JsonNumber;
            if (s.HoldTime)
                s.Hour = o.Num("Hour");
            KitJson.JsonObject w = o.Obj("Weather");
            s.HoldWeather = w != null;
            if (w != null)
            {
                s.Clouds = w.Num("Clouds", s.Clouds);
                s.Rain = w.Num("Rain", s.Rain);
                s.Fog = w.Num("Fog", s.Fog);
                s.Wind = w.Num("Wind", s.Wind);
            }
            s.Moonlight = o.Bool("Moonlight", true);
            s.MaxPerks = (int)o.Num("MaxPerks");
            s.AlwaysPowered = o.Bool("AlwaysPowered");
            s.MapSelectSpot = o.Str("MapSelectSpot") ?? "";
            s.StartGun = string.IsNullOrEmpty(o.Str("StartGun")) ? KitInfo.DefaultStartGun : o.Str("StartGun");
            KitJson.JsonObject prices = o.Obj("PerkPrices");
            s.CustomPerkPrices = prices != null && prices.Keys.Count > 0;
            s.PerkPrices = KitInfo.PerkIds.Concat(new[] { "revive" }).Distinct()
                .Select(p => new ZombiesMapSettings.PerkPrice { Id = p, Price = prices != null && prices.Has(p) ? (int)prices.Num(p) : KitInfo.PerkDefaultPrice(p) })
                .ToList();
            int boxStart = (int)o.Num("BoxStart", -1);

            int made = 0, skipped = 0;
            List<object> entries = o.Arr("Entries") ?? new List<object>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (!(entries[i] is KitJson.JsonObject e) || !Enum.TryParse(e.Str("Type") ?? "", out ZombiesKind kind))
                {
                    skipped++;
                    continue;
                }
                float[] pos = e.Floats("Position");
                if (pos == null || pos.Length < 3)
                {
                    skipped++;
                    continue;
                }
                var go = new GameObject(KitInfo.KindName(kind)) { tag = "EditorOnly" };
                go.transform.SetParent(Folder(scene, Category(kind)), false);
                go.transform.SetPositionAndRotation(new Vector3(pos[0], pos[1], pos[2]), Quaternion.Euler(e.Num("Tilt"), e.Num("Turn"), e.Num("Roll")));
                float[] size = e.Floats("Size");
                float scale = e.Num("Scale", 1f);
                if (scale <= 0f)
                    scale = 1f;
                if (ZombiesMarker.IsSized(kind))
                    go.transform.localScale = size != null && size.Length >= 3 ? new Vector3(size[0], size[1], kind == ZombiesKind.Decal ? 1f : size[2])
                        : size != null && size.Length >= 2 ? new Vector3(size[0], size[1], 1f) : Vector3.Scale(KitInfo.DefaultSize(kind), Vector3.one * scale);
                else
                {
                    float[] st = e.Floats("Stretch");
                    go.transform.localScale = st != null && st.Length == 3 ? new Vector3(scale * st[0], scale * st[1], scale * st[2]) : Vector3.one * scale;
                }
                ZombiesMarker m = go.AddComponent<ZombiesMarker>();
                m.Kind = kind;
                m.What = e.Str("What") ?? "";
                m.Price = (int)e.Num("Price");
                m.Zone = (int)e.Num("Zone");
                if (kind == ZombiesKind.Zone)
                {
                    m.Zone = int.TryParse(m.What, NumberStyles.Integer, Inv, out int z) ? Mathf.Max(1, z) : 1;
                    m.What = "";
                }
                m.OpensWithPower = e.Bool("OpensWithPower");
                m.NoBoard = e.Bool("NoBoard");
                m.BoxStartsHere = kind == ZombiesKind.BoxSpot && i == boxStart;
                go.name = KitInfo.Label(m);
                Undo.RegisterCreatedObjectUndo(go, "Import zombies layout");
                made++;
            }

            int zones = 0;
            foreach (object zo in o.Arr("Zones") ?? new List<object>())
            {
                if (!(zo is KitJson.JsonObject z) || !(z.Arr("Points") is List<object> pts) || pts.Count < 3)
                    continue;
                var world = pts.OfType<List<object>>().Select(p => p.OfType<KitJson.JsonNumber>().Select(n => (float)n.Value).ToArray())
                    .Where(p => p.Length >= 3).Select(p => new Vector3(p[0], p[1], p[2])).ToList();
                if (world.Count < 3)
                    continue;
                var origin = new Vector3(world.Average(p => p.x), world.Min(p => p.y), world.Average(p => p.z));
                var go = new GameObject($"Zone {(int)z.Num("Id", 1)} outline") { tag = "EditorOnly" };
                go.transform.SetParent(Folder(scene, "Zones and walls"), false);
                go.transform.position = origin;
                ZombiesZoneOutline outline = go.AddComponent<ZombiesZoneOutline>();
                outline.Id = Mathf.Max(1, (int)z.Num("Id", 1));
                outline.Height = z.Num("Height", 4f);
                outline.Below = z.Num("Below", 1f);
                outline.Corners = world.Select(p => p - origin).ToList();
                Undo.RegisterCreatedObjectUndo(go, "Import zombies layout");
                zones++;
            }

            string dir = Path.GetDirectoryName(file) ?? "";
            int extras = ImportExtras(dir, id, s, scene);
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            KitPreviews.RebuildAll();
            return $"Imported {made} things{(skipped > 0 ? $" ({skipped} unreadable left out)" : "")}, {zones} zone outlines and {extras} extra files from {Path.GetFileName(file)} into {scene.name}.";
        }

        private static int ImportExtras(string dir, string id, ZombiesMapSettings s, Scene scene)
        {
            int n = 0;
            string tips = Path.Combine(dir, id + "_tips.txt");
            if (File.Exists(tips))
            {
                s.Tips = string.Join("\n", File.ReadAllLines(tips).Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#")));
                n++;
            }
            string links = Path.Combine(dir, id + "_navlinks.txt");
            if (File.Exists(links))
            {
                foreach (string raw in File.ReadAllLines(links))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    float[] v = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => float.TryParse(t, NumberStyles.Float, Inv, out float f) ? f : float.NaN).ToArray();
                    if (v.Length < 6 || v.Take(6).Any(float.IsNaN))
                        continue;
                    var go = new GameObject("Nav link") { tag = "EditorOnly" };
                    go.transform.SetParent(Folder(scene, "Zones and walls"), false);
                    go.transform.position = new Vector3(v[0], v[1], v[2]);
                    ZombiesNavLink l = go.AddComponent<ZombiesNavLink>();
                    l.End = new Vector3(v[3], v[4], v[5]) - go.transform.position;
                    l.Width = v.Length > 6 && !float.IsNaN(v[6]) ? v[6] : 1f;
                    l.OneWay = line.IndexOf("oneway", StringComparison.OrdinalIgnoreCase) >= 0;
                    Undo.RegisterCreatedObjectUndo(go, "Import zombies layout");
                }
                n++;
            }
            string shots = Path.Combine(dir, id + "_gameover_shots.txt");
            if (File.Exists(shots))
            {
                foreach (string raw in File.ReadAllLines(shots))
                {
                    float[] v = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => float.TryParse(t, NumberStyles.Float, Inv, out float f) ? f : float.NaN).ToArray();
                    if (v.Length < 5 || v.Take(5).Any(float.IsNaN))
                        continue;
                    var go = new GameObject("Game over shot") { tag = "EditorOnly" };
                    go.transform.SetParent(Folder(scene, "Extras"), false);
                    go.transform.SetPositionAndRotation(new Vector3(v[0], v[1], v[2]), Quaternion.Euler(v[4], v[3], 0f));
                    go.AddComponent<ZombiesGameOverShot>();
                    Undo.RegisterCreatedObjectUndo(go, "Import zombies layout");
                }
                n++;
            }
            // Sounds and pictures: copied into the project and set on the map settings
            string data = $"{DataFolder}/{Clean(id)}";
            s.LoadingMusic = ImportAsset<AudioClip>(Path.Combine(dir, id + "_loading.wav"), data, ref n) ?? s.LoadingMusic;
            s.Ambience = ImportAsset<AudioClip>(Path.Combine(dir, id + "_ambience.wav"), data, ref n) ?? s.Ambience;
            s.EasterEggSong = ImportAsset<AudioClip>(Path.Combine(dir, id + "_eesong.wav"), data, ref n) ?? s.EasterEggSong;
            s.Banner = ImportAsset<Texture2D>(Path.Combine(dir, id + "_banner.png"), data, ref n) ?? s.Banner;
            s.LoadingPicture = ImportAsset<Texture2D>(Path.Combine(dir, id + "_lore.jpg"), data, ref n)
                               ?? ImportAsset<Texture2D>(Path.Combine(dir, id + "_lore.png"), data, ref n) ?? s.LoadingPicture;
            return n;
        }

        private static T ImportAsset<T>(string file, string folder, ref int n) where T : Object
        {
            if (!File.Exists(file))
                return null;
            Directory.CreateDirectory(Path.Combine(ProjectRoot, folder));
            string asset = $"{folder}/{Path.GetFileName(file)}";
            File.Copy(file, Path.Combine(ProjectRoot, asset), true);
            AssetDatabase.ImportAsset(asset);
            n++;
            return AssetDatabase.LoadAssetAtPath<T>(asset);
        }
    }
}
