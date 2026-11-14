using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// SPZombies' models, loaded straight from the installed mods' bundles (SPZombies, Perk Beverages, Pack-a-Punch): the box,
    /// the perk machines, Pack-a-Punch, the window boards, doors, debris, the power switch and the 115 rock. Only for
    /// showing them in the Scene view: nothing of them is saved into your project or your map.
    /// </summary>
    internal static class KitModels
    {
        public const string ZombiesBundle = "zombies.bundle";
        public const string PerksBundle = "perk_beverages.bundle";
        public const string PapBundle = "pap_hideout.bundle";

        private static readonly Dictionary<string, AssetBundle> Bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Material> Fixed = new HashSet<Material>();

        /// <summary>Goes up each time the models are reloaded (previews made from old ones rebuild).</summary>
        public static int Generation { get; private set; }

        private static bool SameBundle(string loadedName, string file)
        {
            if (string.IsNullOrEmpty(loadedName))
                return false;
            string bare = Path.GetFileNameWithoutExtension(file);
            string n = loadedName.ToLowerInvariant();
            return n == file.ToLowerInvariant() || n == bare || n.EndsWith("/" + file.ToLowerInvariant()) || n.EndsWith("/" + bare);
        }

        /// <summary>One of the bundles, loaded once, or null when the mod isn't installed in the SPT folder.</summary>
        public static AssetBundle Bundle(string file)
        {
            if (Bundles.TryGetValue(file, out AssetBundle have) && have != null)
                return have;
            // Still loaded from before a script reload: Unity keeps bundles loaded, the kit's list is new
            AssetBundle loaded = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(b => b != null && SameBundle(b.name, file));
            if (loaded == null && !Failed.Contains(file))
            {
                string path = KitInstall.Find(file);
                if (path != null)
                {
                    try
                    {
                        loaded = AssetBundle.LoadFromFile(path);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SPZombies Kit] Couldn't load {path}: {e.Message}");
                    }
                    if (loaded == null)
                    {
                        Debug.LogWarning($"[SPZombies Kit] Couldn't load {path} (is another copy of it loaded? Press Reload models in the kit's Setup tab).");
                        Failed.Add(file);
                    }
                }
            }
            if (loaded != null)
                Bundles[file] = loaded;
            return loaded;
        }

        /// <summary>A model from one of the bundles by its name, or null.</summary>
        public static GameObject Prefab(string file, string asset)
        {
            if (string.IsNullOrEmpty(asset))
                return null;
            string key = file + "|" + asset;
            if (Prefabs.TryGetValue(key, out GameObject had))
                return had;
            AssetBundle bundle = Bundle(file);
            GameObject prefab = null;
            if (bundle != null)
            {
                try
                {
                    prefab = bundle.LoadAsset<GameObject>(asset);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SPZombies Kit] {file}: couldn't load {asset}: {e.Message}");
                }
            }
            if (prefab != null)
                FixShaders(prefab);
            Prefabs[key] = prefab;
            return prefab;
        }

        /// <summary>The names of the models in a bundle (lower case, no folders), or none.</summary>
        public static IEnumerable<string> Names(string file)
        {
            AssetBundle bundle = Bundle(file);
            if (bundle == null)
                return Enumerable.Empty<string>();
            return bundle.GetAllAssetNames().Where(a => a.EndsWith(".prefab")).Select(a => Path.GetFileNameWithoutExtension(a).ToLowerInvariant());
        }

        /// <summary>Every look of a kind in SPZombies' bundle (names after the prefix), else the known list.</summary>
        public static List<string> Looks(string prefix, string[] fallback)
        {
            var found = Names(ZombiesBundle).Where(n => n.StartsWith(prefix)).Select(n => n.Substring(prefix.Length)).ToList();
            if (found.Count == 0)
                return fallback.ToList();
            // The known order first (the original's), then any new ones
            return fallback.Where(found.Contains).Concat(found.Where(f => !fallback.Contains(f)).OrderBy(f => f)).ToList();
        }

        /// <summary>The looks the kit offers for a kind, those of them in the installed bundle (all of them without one).</summary>
        public static List<string> Offered(string prefix, string[] offered)
        {
            var found = Names(ZombiesBundle).Where(n => n.StartsWith(prefix)).Select(n => n.Substring(prefix.Length)).ToList();
            return found.Count == 0 ? offered.ToList() : offered.Where(found.Contains).ToList();
        }

        /// <summary>Whether SPZombies has this look (offered by the kit or not).</summary>
        public static bool IsLook(string prefix, string look, string[] all) =>
            !string.IsNullOrEmpty(look) && (Bundle(ZombiesBundle) != null ? Prefab(ZombiesBundle, prefix + look) != null : all.Contains(look));

        public static bool Any => Bundle(ZombiesBundle) != null || Bundle(PerksBundle) != null || Bundle(PapBundle) != null;

        // A bundle's shaders were built for the game: swapped for the editor's own of the same name, so they draw right here
        private static void FixShaders(GameObject prefab)
        {
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null || !Fixed.Add(mat))
                        continue;
                    Shader own = Shader.Find(mat.shader.name);
                    if (own != null && own != mat.shader)
                    {
                        int queue = mat.renderQueue;
                        mat.shader = own;
                        mat.renderQueue = queue;
                    }
                }
        }

        /// <summary>Unloads the models and reads them again (after SPZombies was updated or the SPT folder changed).</summary>
        public static void Reload()
        {
            KitPreviews.ClearAll();
            foreach (AssetBundle b in Bundles.Values)
                if (b != null)
                    b.Unload(true);
            // Ones loaded before a script reload (not in the list)
            foreach (AssetBundle b in AssetBundle.GetAllLoadedAssetBundles().ToList())
                if (b != null && (SameBundle(b.name, ZombiesBundle) || SameBundle(b.name, PerksBundle) || SameBundle(b.name, PapBundle)))
                    b.Unload(true);
            Bundles.Clear();
            Failed.Clear();
            Prefabs.Clear();
            Fixed.Clear();
            KitInstall.Forget();
            Generation++;
        }
    }
}
