using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Shows every marker as it will look in a match: the real model from the SPZombies install (the box, perk machines,
    /// Pack-a-Punch, window boards, doors, debris, power switch, 115 rock), the wall buy's chalk on its board, wall decals,
    /// and see-through boxes for the things players never see (spawns, zones, blockers, exfil areas). Sized and set out
    /// with the same sums SPZombies uses, so what you see is where it will be.
    /// The previews are hidden, never saved and never built into the map: they're remade from the markers whenever a scene
    /// opens or the scripts reload. Clicking one selects its marker.
    /// </summary>
    [InitializeOnLoad]
    internal static class KitPreviews
    {
        public const string RootName = "SPZombies Kit Preview";
        private const string PrefShow = "SPZombiesMapKit.ShowPreviews";
        // SPZombies' default barricade depth: at it the boards are exactly as thick as made
        private const float WindowDepth = 0.3f;

        private class Preview
        {
            public GameObject Root;
            public Transform Model;
            public string Key;
            public Vector3 Natural = Vector3.one, Centre;
            public float BaseScale = 1f;
            public bool Primitive;  // a stand-in box or capsule (no model measured)
            public Vector3 LastPos, LastScale;
            public Quaternion LastRot;
        }

        private static readonly Dictionary<ZombiesMarker, Preview> Live = new Dictionary<ZombiesMarker, Preview>();
        private static readonly Dictionary<GameObject, ZombiesMarker> Owners = new Dictionary<GameObject, ZombiesMarker>();
        private static readonly HashSet<ZombiesMarker> Dirty = new HashSet<ZombiesMarker>();
        private static readonly Dictionary<Color, Material> BoxMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<string, Texture2D> Pictures = new Dictionary<string, Texture2D>();
        private static Material _board;
        private static double _next;

        /// <summary>Every marker in the open scenes (kept up to date for the Scene view drawing).</summary>
        public static readonly List<ZombiesMarker> Markers = new List<ZombiesMarker>();

        /// <summary>Goes up whenever a marker is added, removed, changed or moved (the paths view works its routes out again).</summary>
        public static int Revision { get; private set; }

        public static bool Show
        {
            get => EditorPrefs.GetBool(PrefShow, true);
            set
            {
                EditorPrefs.SetBool(PrefShow, value);
                _next = 0;
            }
        }

        static KitPreviews()
        {
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += ClearAll;
            ZombiesMarker.Edited += m =>
            {
                if (m != null)
                    Dirty.Add(m);
            };
            Selection.selectionChanged += Redirect;
            EditorApplication.delayCall += DestroyStrays;
            EditorApplication.hierarchyChanged += () => _hierarchyChanged = true;
        }

        /// <summary>Remakes every preview (after the models were reloaded or a setting changed).</summary>
        public static void RebuildAll()
        {
            ClearAll();
            _next = 0;
        }

        public static void ClearAll()
        {
            foreach (Preview p in Live.Values)
                if (p.Root != null)
                    Object.DestroyImmediate(p.Root);
            Live.Clear();
            Owners.Clear();
            foreach (Texture2D t in Pictures.Values)
                if (t != null)
                    Object.DestroyImmediate(t);
            Pictures.Clear();
            DestroyStrays();
        }

        // Previews left from before a script reload (the kit's lists are new; the hidden objects aren't)
        private static void DestroyStrays()
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go != null && go.name == RootName && go.transform.parent == null && !EditorUtility.IsPersistent(go)
                    && (go.hideFlags & HideFlags.DontSaveInEditor) != 0 && !Owners.ContainsKey(go))
                    Object.DestroyImmediate(go);
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            double now = EditorApplication.timeSinceStartup;
            if (now < _next && Dirty.Count == 0)
                return;
            _next = now + 0.2;
            try
            {
                Refresh();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SPZombies Kit] Previews: {e}");
            }
        }

        /// <summary>Brings every preview up to date now (instead of on the next editor tick).</summary>
        public static void RefreshNow() => Refresh();

        private static void Refresh()
        {
            Markers.Clear();
            foreach (ZombiesMarker m in Object.FindObjectsOfType<ZombiesMarker>(true))
                if (m != null && m.gameObject.scene.IsValid())
                    Markers.Add(m);
            bool show = Show;
            var seen = new HashSet<ZombiesMarker>();
            foreach (ZombiesMarker m in Markers)
            {
                seen.Add(m);
                if (!show || !m.gameObject.activeInHierarchy || !m.enabled)
                {
                    Remove(m);
                    continue;
                }
                string key = Key(m);
                Live.TryGetValue(m, out Preview p);
                if (p == null || p.Root == null || p.Key != key || Dirty.Contains(m))
                {
                    Remove(m);
                    Live[m] = Build(m, key);
                    Revision++;
                }
                else if (Moved(m, p))
                {
                    Apply(m, p);
                    Revision++;
                }
            }
            Dirty.Clear();
            foreach (ZombiesMarker gone in Live.Keys.Where(k => k == null || !seen.Contains(k)).ToList())
                Remove(gone);
        }

        private static string Key(ZombiesMarker m)
        {
            GameObject named = NamedLook(m);
            return $"{m.Kind}|{m.What}|{(m.CustomLook != null ? m.CustomLook.GetInstanceID() : 0)}|{(named != null ? named.GetInstanceID() : 0)}|{m.NoBoard}|{KitModels.Generation}";
        }

        // ---------- Map objects by name ----------

        private static Dictionary<string, GameObject> _sceneLooks;
        private static double _sceneLooksAt;
        private static bool _hierarchyChanged = true;
        private static readonly System.Text.RegularExpressions.Regex CopySuffix = new System.Text.RegularExpressions.Regex(@"\s*\(\d+\)$");

        /// <summary>
        /// The map object a layout entry names instead of using one of SPZombies' looks (a door, debris or light fixture of
        /// the map, a copied prop), found as SPZombies finds it in a match: an object with a level-of-detail group of that
        /// name. Null when there's none (in a match there wouldn't be one either).
        /// </summary>
        private static GameObject NamedLook(ZombiesMarker m)
        {
            string name = null;
            switch (m.Kind)
            {
                case ZombiesKind.Door:
                case ZombiesKind.Debris:
                    if (m.CustomLook == null && !string.IsNullOrEmpty(m.What)
                        && KitModels.Prefab(KitModels.ZombiesBundle, (m.Kind == ZombiesKind.Door ? "zm_door_" : "zm_debris_") + m.What) == null)
                        name = m.What;
                    break;
                case ZombiesKind.PowerLight:
                case ZombiesKind.Lamp:
                    if (m.CustomLook == null && !string.IsNullOrEmpty(m.What))
                        name = m.What;
                    break;
                case ZombiesKind.Prop:
                    if (m.What != null && m.What.StartsWith("scene:"))
                        name = m.What.Substring("scene:".Length);
                    break;
            }
            if (name == null)
                return null;
            double now = EditorApplication.timeSinceStartup;
            if (_sceneLooks == null || (_hierarchyChanged && now - _sceneLooksAt > 2.0))
            {
                _sceneLooks = new Dictionary<string, GameObject>();
                foreach (LODGroup g in Object.FindObjectsOfType<LODGroup>())
                {
                    GameObject go = g.gameObject;
                    if (go.hideFlags != HideFlags.None || go.GetComponentInParent<ZombiesMarker>() != null)
                        continue;
                    string n = CopySuffix.Replace(go.name, "").Trim();
                    if (n.Length > 0 && !_sceneLooks.ContainsKey(n))
                        _sceneLooks[n] = go;
                }
                _sceneLooksAt = now;
                _hierarchyChanged = false;
            }
            return _sceneLooks.TryGetValue(name, out GameObject found) && found != null ? found : null;
        }

        private static bool Moved(ZombiesMarker m, Preview p)
        {
            Transform t = m.transform;
            return t.position != p.LastPos || t.rotation != p.LastRot || t.lossyScale != p.LastScale;
        }

        private static void Remove(ZombiesMarker m)
        {
            if (!Live.TryGetValue(m, out Preview p))
                return;
            Revision++;
            if (p.Root != null)
            {
                Owners.Remove(p.Root);
                Object.DestroyImmediate(p.Root);
            }
            Live.Remove(m);
            if (m != null)
                m.HasPreviewBox = false;
        }

        // Clicking a preview picks its marker
        private static void Redirect()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
                return;
            Transform t = go.transform;
            while (t.parent != null)
                t = t.parent;
            if (t.name == RootName && Owners.TryGetValue(t.gameObject, out ZombiesMarker m) && m != null)
                Selection.activeGameObject = m.gameObject;
        }

        // ---------- Building ----------

        private static Preview Build(ZombiesMarker m, string key)
        {
            var p = new Preview { Key = key, Root = new GameObject(RootName) };
            p.Root.hideFlags = HideFlags.HideAndDontSave;
            Owners[p.Root] = m;
            try
            {
                MakeModel(m, p);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SPZombies Kit] Preview of {m.name}: {e.Message}");
            }
            if (p.Model == null)
                MakePrimitive(m, p);
            foreach (Transform t in p.Root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            Apply(m, p);
            return p;
        }

        private static void MakeModel(ZombiesMarker m, Preview p)
        {
            string what = m.What ?? "";
            switch (m.Kind)
            {
                case ZombiesKind.BoxSpot:
                    // Its hinge away from the side players buy from
                    SetModel(p, KitModels.Prefab(KitModels.ZombiesBundle, "zm_box"), 0.35f, Quaternion.Euler(0f, 180f, 0f));
                    break;
                case ZombiesKind.PerkMachine:
                    SetModel(p, KitModels.Prefab(KitModels.PerksBundle, "perk_" + what), 0.35f, Quaternion.identity);
                    break;
                case ZombiesKind.PackAPunch:
                    SetModel(p, KitModels.Prefab(KitModels.PapBundle, "pap_machine"), 0.35f, Quaternion.identity);
                    break;
                case ZombiesKind.Door:
                    if (m.CustomLook != null)
                        Custom(p, m.CustomLook);
                    else
                    {
                        GameObject door = KitModels.Prefab(KitModels.ZombiesBundle, "zm_door_" + what);
                        if (door != null)
                            SetModel(p, door, 1f, FaceForward(door));
                        else if (NamedLook(m) != null)
                            Custom(p, NamedLook(m));
                    }
                    break;
                case ZombiesKind.Debris:
                    if (m.CustomLook != null)
                        Custom(p, m.CustomLook);
                    else
                    {
                        GameObject debris = KitModels.Prefab(KitModels.ZombiesBundle, "zm_debris_" + what);
                        if (debris != null)
                            SetModel(p, debris, 1f, Quaternion.identity);
                        else if (NamedLook(m) != null)
                            Custom(p, NamedLook(m));
                    }
                    break;
                case ZombiesKind.Prop:
                    if (NamedLook(m) != null)
                        Custom(p, NamedLook(m));
                    break;
                case ZombiesKind.PowerSwitch:
                case ZombiesKind.ExfilActivator:
                    SetModel(p, KitModels.Prefab(KitModels.ZombiesBundle, "zm_power_switch"), 0.17f, Quaternion.identity);
                    break;
                case ZombiesKind.Window:
                    SetModel(p, KitModels.Prefab(KitModels.ZombiesBundle, "zm_window"), 1f, Quaternion.identity);
                    break;
                case ZombiesKind.Rock:
                    SetModel(p, KitModels.Prefab(KitModels.ZombiesBundle, "zm_rock_115"), 1f, Quaternion.identity);
                    break;
                case ZombiesKind.WallBuy:
                    WallBuy(m, p);
                    break;
                case ZombiesKind.Decal:
                    Decal(m, p);
                    break;
                case ZombiesKind.PowerLight:
                case ZombiesKind.Lamp:
                    if (m.CustomLook != null)
                        Custom(p, m.CustomLook);
                    else if (NamedLook(m) != null)
                        Custom(p, NamedLook(m));
                    AddLight(m, p);
                    break;
            }
        }

        // Some of the original doors were modelled facing sideways (thin along X): turned so the face looks down +Z
        private static Quaternion FaceForward(GameObject prefab)
        {
            MeshFilter mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            Vector3 size = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.one;
            return size.x < size.z ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        }

        private static void SetModel(Preview p, GameObject prefab, float baseScale, Quaternion turn)
        {
            if (prefab == null)
                return;
            var go = (GameObject)Object.Instantiate(prefab);
            Strip(go);
            Adopt(p, go.transform, baseScale, turn);
        }

        // A copy of an object of the map (a custom door, debris or light fixture), as SPZombies copies its template
        private static void Custom(Preview p, GameObject look)
        {
            var go = (GameObject)Object.Instantiate(look);
            go.SetActive(true);
            Strip(go);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.forceRenderingOff = false;
                r.enabled = true;
            }
            Adopt(p, go.transform, Mathf.Abs(look.transform.lossyScale.x), Quaternion.identity);
        }

        /// <summary>Only the look: no scripts, colliders, bodies or sounds, nothing static (never in a bake).</summary>
        internal static void Strip(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null)
                    Object.DestroyImmediate(mb);
            foreach (Joint j in go.GetComponentsInChildren<Joint>(true))
                Object.DestroyImmediate(j);
            foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
                Object.DestroyImmediate(rb);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true))
                a.enabled = false;
        }

        // Hangs the model under the preview and measures it there (the preview unturned at the origin)
        private static void Adopt(Preview p, Transform model, float baseScale, Quaternion turn)
        {
            Transform root = p.Root.transform;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;
            model.SetParent(root, false);
            model.localPosition = Vector3.zero;
            model.localRotation = turn;
            model.localScale = Vector3.one * baseScale;
            Bounds b = VisibleBounds(model.gameObject);
            p.Model = model;
            p.BaseScale = baseScale;
            p.Natural = Vector3.Max(b.size, Vector3.one * 0.01f);
            p.Centre = b.center;
            p.Primitive = false;
        }

        private static void MakePrimitive(ZombiesMarker m, Preview p)
        {
            bool person = m.Kind == ZombiesKind.PlayerStart || m.Kind == ZombiesKind.ZombieSpawn;
            GameObject go = GameObject.CreatePrimitive(person ? PrimitiveType.Capsule : PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(p.Root.transform, false);
            Color c = m.Kind == ZombiesKind.Zone ? WithAlpha(KitInfo.ZoneColour(Mathf.Max(1, m.Zone)), 0.14f) : KitInfo.Colour(m.Kind);
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = BoxMaterial(c);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            p.Model = go.transform;
            p.Primitive = true;
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>Puts the preview where the marker is, at its size (SPZombies' own sums).</summary>
        private static void Apply(ZombiesMarker m, Preview p)
        {
            Transform t = m.transform;
            Transform root = p.Root.transform;
            root.SetPositionAndRotation(t.position, t.rotation);
            root.localScale = Vector3.one;
            p.LastPos = t.position;
            p.LastRot = t.rotation;
            p.LastScale = t.lossyScale;
            if (p.Model == null)
                return;

            bool sized = ZombiesMarker.IsSized(m.Kind);
            Vector3 lossy = new Vector3(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y), Mathf.Abs(t.lossyScale.z));
            float scale = lossy.x > 0.01f ? lossy.x : 1f;
            // A 115 rock may be stretched unevenly
            Vector3 stretch = m.Kind == ZombiesKind.Rock ? new Vector3(1f, lossy.y / scale, lossy.z / scale) : Vector3.one;
            Vector3 size;

            if (p.Primitive)
            {
                size = sized ? lossy : KitInfo.MarkerSize(m.Kind) * scale;
                bool capsule = m.Kind == ZombiesKind.PlayerStart || m.Kind == ZombiesKind.ZombieSpawn;
                p.Model.localScale = capsule ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
                p.Model.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
                p.Model.localRotation = Quaternion.identity;
            }
            else if (m.Kind == ZombiesKind.Decal)
            {
                // A quad on the wall: its bottom middle on the spot, facing out of the wall
                size = new Vector3(lossy.x, lossy.y, 0.02f);
                p.Model.localScale = new Vector3(size.x, size.y, 1f);
                p.Model.localPosition = new Vector3(0f, size.y * 0.5f, 0.01f);
                p.Model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            else
            {
                Vector3 k = sized
                    ? new Vector3(lossy.x / p.Natural.x, lossy.y / p.Natural.y, lossy.z / p.Natural.z)
                    : Vector3.Scale(Vector3.one * scale, stretch);
                size = sized ? lossy : Vector3.Scale(p.Natural * scale, stretch);
                // The barricade's boards keep their shape: depth follows width and height, the Depth only thickens it
                if (m.Kind == ZombiesKind.Window)
                    k.z = (k.x + k.y) * 0.5f * (size.z / WindowDepth);
                p.Model.localScale = k * p.BaseScale;
                p.Model.localPosition = new Vector3(0f, size.y * 0.5f, 0f) - Vector3.Scale(p.Centre, k);
            }
            m.PreviewBox = new Bounds(new Vector3(0f, size.y * 0.5f, 0f), size);
            m.HasPreviewBox = true;
        }

        // ---------- Wall buys, decals, lights ----------

        // How wide a gun is drawn on the wall (its chalk), by its class
        private static float GunWidth(string id)
        {
            if (id == KitInfo.GrenadesWallId)
                return 0.25f;
            string c = KitInstall.FindGun(id)?.Class ?? "";
            switch (c)
            {
                case "pistol": return 0.24f;
                case "smg": return 0.6f;
                case "shotgun": return 1.0f;
                case "sniper": return 1.15f;
                case "lmg": return 1.1f;
                case "launcher": return 0.8f;
                case "wonder": return 0.6f;
                default: return 0.95f;
            }
        }

        private static Texture2D Picture(string path)
        {
            if (path == null)
                return null;
            if (Pictures.TryGetValue(path, out Texture2D t) && t != null)
                return t;
            t = KitInstall.LoadPicture(path);
            Pictures[path] = t;
            return t;
        }

        // A gun's chalk outline, trimmed to the drawing (kept per gun)
        private static Texture2D Chalk(string gunId)
        {
            string key = "chalk:" + gunId;
            if (Pictures.TryGetValue(key, out Texture2D t) && t != null)
                return t;
            t = KitInstall.LoadChalk(gunId);
            Pictures[key] = t;
            return t;
        }

        // The chalk outline (SPZombies' own picture of the gun) on a wooden board on the wall's face
        private static void WallBuy(ZombiesMarker m, Preview p)
        {
            var holder = new GameObject("Wall buy");
            Texture2D chalk = Chalk(m.What);
            float w = GunWidth(m.What);
            float h = chalk != null && chalk.width > 0 ? w * chalk.height / chalk.width : w * 0.35f;
            if (!m.NoBoard || chalk == null)
            {
                GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(board.GetComponent<Collider>());
                board.name = "Board";
                board.transform.SetParent(holder.transform, false);
                board.transform.localScale = new Vector3(w * 1.15f, h * 1.15f, 0.02f);
                board.transform.localPosition = new Vector3(0f, h * 0.575f, 0.01f);
                board.GetComponent<Renderer>().sharedMaterial = BoardMaterial();
            }
            if (chalk != null)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "Chalk";
                q.transform.SetParent(holder.transform, false);
                q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                q.transform.localScale = new Vector3(w, h, 1f);
                q.transform.localPosition = new Vector3(0f, h * 0.575f, m.NoBoard ? 0.004f : 0.024f);
                Renderer r = q.GetComponent<Renderer>();
                r.sharedMaterial = PictureMaterial(chalk);
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            Adopt(p, holder.transform, 1f, Quaternion.identity);
        }

        private static void Decal(ZombiesMarker m, Preview p)
        {
            Texture2D tex = Picture(KitInstall.DecalPicture(string.IsNullOrEmpty(m.What) ? "blood_hand_01" : m.What));
            if (tex == null)
                return;
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Decal";
            q.transform.SetParent(p.Root.transform, false);
            Renderer r = q.GetComponent<Renderer>();
            r.sharedMaterial = PictureMaterial(tex);
            r.shadowCastingMode = ShadowCastingMode.Off;
            p.Model = q.transform;
            p.Primitive = false;
        }

        // A power light's light (lit in the editor, to see what it lights), as SPZombies makes it
        private static void AddLight(ZombiesMarker m, Preview p)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(p.Root.transform, false);
            go.transform.localPosition = Vector3.down * 0.2f;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.range = 9f;
            light.intensity = 1.5f;
            light.shadows = LightShadows.None;
            string what = m.What ?? "";
            bool street = what.StartsWith("Streetlight") || what.StartsWith("Searchlight");
            if (m.Kind == ZombiesKind.Lamp || street)
            {
                light.color = street ? new Color(1f, 0.82f, 0.55f) : new Color(1f, 0.9f, 0.7f);
                light.range = street ? 16f : 11f;
                light.intensity = street ? 1.7f : 1.3f;
                go.transform.localPosition = street ? Vector3.up * 0.2f : Vector3.down * 0.2f;
            }
        }

        private static Material BoxMaterial(Color c)
        {
            if (BoxMaterials.TryGetValue(c, out Material mat) && mat != null)
                return mat;
            mat = new Material(Shader.Find("Sprites/Default")) { color = c, hideFlags = HideFlags.HideAndDontSave, name = "SPZombies Kit Box" };
            BoxMaterials[c] = mat;
            return mat;
        }

        private static Material BoardMaterial()
        {
            if (_board != null)
                return _board;
            _board = new Material(Shader.Find("Standard")) { color = new Color(0.36f, 0.25f, 0.16f), hideFlags = HideFlags.HideAndDontSave, name = "SPZombies Kit Board" };
            return _board;
        }

        private static readonly Dictionary<Texture2D, Material> PictureMaterials = new Dictionary<Texture2D, Material>();

        private static Material PictureMaterial(Texture2D tex)
        {
            if (PictureMaterials.TryGetValue(tex, out Material mat) && mat != null)
                return mat;
            mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = Color.white, hideFlags = HideFlags.HideAndDontSave, name = "SPZombies Kit Picture" };
            PictureMaterials[tex] = mat;
            return mat;
        }

        /// <summary>
        /// The visible model only, measured from its meshes (as SPZombies measures it): active, enabled meshes, the nearest
        /// detail level only, no shadow-only stand-ins.
        /// </summary>
        internal static Bounds VisibleBounds(GameObject go)
        {
            var skip = new HashSet<Renderer>();
            foreach (LODGroup group in go.GetComponentsInChildren<LODGroup>(false))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null)
                            skip.Add(r);
            }
            bool any = false;
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            Renderer[] all = go.GetComponentsInChildren<Renderer>(false);
            bool lodNamed = all.Any(r => r.name.IndexOf("LOD0", StringComparison.OrdinalIgnoreCase) >= 0);
            foreach (Renderer r in all)
            {
                if (!r.enabled || r.forceRenderingOff || skip.Contains(r) || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    continue;
                if (lodNamed && r.name.IndexOf("LOD0", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // (In the editor a missing component is a "fake null": compared with ==, never with ?.)
                MeshFilter filter = r is MeshRenderer ? r.GetComponent<MeshFilter>() : null;
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                Bounds local = mesh.bounds;
                Matrix4x4 mx = r.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 w = mx.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        bounds = new Bounds(w, Vector3.zero);
                        any = true;
                    }
                    else
                        bounds.Encapsulate(w);
                }
            }
            return any ? bounds : new Bounds(go.transform.position, Vector3.one * 0.3f);
        }
    }
}
