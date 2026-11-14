using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// The Tushonka Mapping SDK's side of things, found by shape (its types are read by reflection, so the kit builds in any
    /// project and a new SDK version never stops it compiling): the Map Definition (map id, SPT folder, build folder,
    /// scenes), bot zones and player spawn points, the AI doors holder, and the map's server settings (base.json).
    /// </summary>
    [InitializeOnLoad]
    internal static class KitSdk
    {
        static KitSdk()
        {
            EditorApplication.projectChanged += () => _defs = null;
        }

        // ---------- Map Definition ----------

        private static Type _defType;
        private static bool _defLooked;

        /// <summary>The SDK's Map Definition asset type (a ScriptableObject with MapId and SptRoot), or null without the SDK.</summary>
        public static Type MapDefinitionType
        {
            get
            {
                if (!_defLooked)
                {
                    _defLooked = true;
                    _defType = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                        .FirstOrDefault(t => t.Name.EndsWith("MapDefinition") && t.GetField("MapId") != null && t.GetField("SptRoot") != null);
                }
                return _defType;
            }
        }

        public static bool Present => MapDefinitionType != null;

        internal class MapDef
        {
            public ScriptableObject Asset;
            public string MapId = "", SptRoot = "", OutputFolder = "Build";
            public List<string> ScenePaths = new List<string>();
        }

        private static List<MapDef> _defs;

        public static List<MapDef> MapDefinitions()
        {
            if (_defs != null)
                return _defs;
            _defs = new List<MapDef>();
            Type t = MapDefinitionType;
            if (t == null)
                return _defs;
            foreach (string guid in AssetDatabase.FindAssets("t:" + t.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), t) as ScriptableObject;
                if (asset == null)
                    continue;
                var so = new SerializedObject(asset);
                var d = new MapDef
                {
                    Asset = asset,
                    MapId = so.FindProperty("MapId")?.stringValue ?? "",
                    SptRoot = so.FindProperty("SptRoot")?.stringValue ?? "",
                    OutputFolder = so.FindProperty("OutputFolder")?.stringValue ?? "Build",
                };
                SerializedProperty scenes = so.FindProperty("Scenes");
                if (scenes != null && scenes.isArray)
                    for (int i = 0; i < scenes.arraySize; i++)
                    {
                        Object scene = scenes.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (scene != null)
                            d.ScenePaths.Add(AssetDatabase.GetAssetPath(scene));
                    }
                _defs.Add(d);
            }
            return _defs;
        }

        /// <summary>The Map Definition of this map id (or the only one in the project), or null.</summary>
        public static MapDef For(string mapId)
        {
            List<MapDef> defs = MapDefinitions();
            return defs.FirstOrDefault(d => !string.IsNullOrEmpty(mapId) && d.MapId == mapId) ?? (defs.Count == 1 ? defs[0] : null);
        }

        public static string MapDefinitionSptRoot()
        {
            ZombiesMapSettings s = KitExport.Settings();
            return For(s != null ? s.MapId : null)?.SptRoot ?? "";
        }

        public static string OutputDirectory(MapDef def)
        {
            string folder = string.IsNullOrEmpty(def.OutputFolder) ? "Build" : def.OutputFolder;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.IsPathRooted(folder) ? folder : Path.Combine(projectRoot, folder));
        }

        /// <summary>The SDK's package folder for the map (made by Build Map with Create Package on), or null.</summary>
        public static string PackageFolder(MapDef def)
        {
            if (def == null)
                return null;
            string p = Path.Combine(OutputDirectory(def), "package", def.MapId);
            return Directory.Exists(p) ? p : null;
        }

        // ---------- Types ----------

        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();

        /// <summary>A game or SDK type by its class name (from the project's own code), or null.</summary>
        public static Type FindType(string name)
        {
            if (Types.TryGetValue(name, out Type had))
                return had;
            Type found = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = asm.GetName().Name;
                if (asm.IsDynamic || n.StartsWith("SPZombies.") || n.StartsWith("Unity") || n.StartsWith("System") || n == "mscorlib" || n == "netstandard")
                    continue;
                foreach (Type t in SafeTypes(asm))
                    if (t.Name == name)
                    {
                        found = t;
                        break;
                    }
                if (found != null)
                    break;
            }
            Types[name] = found;
            return found;
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null);
            }
            catch
            {
                return Enumerable.Empty<Type>();
            }
        }

        private static MethodInfo _createBotZone, _createSpawnPoint;
        private static bool _toolsLooked;

        // The SDK's scene tools: CreateBotZone(Transform, string, Vector3) and CreateSpawnPoint(Transform, string, Vector3, bool)
        private static bool FindSceneTools()
        {
            if (_toolsLooked)
                return _createBotZone != null && _createSpawnPoint != null;
            _toolsLooked = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.GetName().Name.StartsWith("Assembly-CSharp")))
                foreach (Type t in SafeTypes(asm))
                {
                    MethodInfo spawn = t.GetMethod("CreateSpawnPoint", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Transform), typeof(string), typeof(Vector3), typeof(bool) }, null);
                    MethodInfo zone = t.GetMethod("CreateBotZone", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Transform), typeof(string), typeof(Vector3) }, null);
                    if (spawn != null && zone != null)
                    {
                        _createSpawnPoint = spawn;
                        _createBotZone = zone;
                        return true;
                    }
                }
            return false;
        }

        private static Component[] All(Type t) => t == null ? new Component[0] : Object.FindObjectsOfType(t, true).OfType<Component>().Where(c => c != null && c.gameObject.scene.IsValid()).ToArray();

        public static bool HasBotZone => All(FindType("BotZone")).Length > 0;

        public static bool HasAIDoorsHolder => All(FindType("AIDoorsHolder")).Length > 0;

        /// <summary>The map's player spawn points named Spawn_Player_1..4 (SPZombies puts player N at Spawn_Player_N).</summary>
        public static List<Component> PlayerSpawns() =>
            All(FindType("SpawnPointMarker")).Where(c => c.name.StartsWith("Spawn_Player_")).OrderBy(c => c.name, StringComparer.Ordinal).ToList();

        /// <summary>Every spawn point marker of the open scenes.</summary>
        public static int SpawnPointCount => All(FindType("SpawnPointMarker")).Length;

        // ---------- Player spawns ----------

        /// <summary>
        /// Makes the four player spawns Spawn_Player_1..4 the SDK's way (player spawn points in a bot zone, the bot zone made
        /// too if the map has none: the game needs one to start a raid), in a small square at the spot, facing the way given.
        /// Only missing ones are made. Returns what it did.
        /// </summary>
        public static string AddPlayerSpawns(Scene scene, Vector3 at, Quaternion facing)
        {
            if (!FindSceneTools())
                return "The SDK's scene tools weren't found (is the Tushonka Mapping SDK in this project?).";
            Type botZoneType = FindType("BotZone");
            Type markerType = FindType("SpawnPointMarker");
            if (botZoneType == null || markerType == null)
                return "The SDK's BotZone / SpawnPointMarker types weren't found.";
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add player spawns");

            Component zone = All(botZoneType).FirstOrDefault(c => c.gameObject.scene == scene) ?? All(botZoneType).FirstOrDefault();
            if (zone == null)
            {
                GameObject holder = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BotZones");
                if (holder == null)
                {
                    holder = new GameObject("BotZones");
                    SceneManager.MoveGameObjectToScene(holder, scene);
                    Undo.RegisterCreatedObjectUndo(holder, "Add player spawns");
                }
                zone = (Component)_createBotZone.Invoke(null, new object[] { holder.transform, "ZoneMain", Vector3.zero });
                zone.transform.position = at;
                Undo.RegisterCreatedObjectUndo(zone.gameObject, "Add player spawns");
            }

            Vector3[] square = { new Vector3(-0.8f, 0f, 0.8f), new Vector3(0.8f, 0f, 0.8f), new Vector3(-0.8f, 0f, -0.8f), new Vector3(0.8f, 0f, -0.8f) };
            List<Component> existing = All(markerType).ToList();
            var made = new List<GameObject>();
            for (int i = 1; i <= 4; i++)
            {
                string name = "Spawn_Player_" + i;
                if (existing.Any(m => m.name == name))
                    continue;
                var marker = (Component)_createSpawnPoint.Invoke(null, new object[] { zone.transform, name, Vector3.zero, true });
                marker.transform.SetPositionAndRotation(at + facing * square[i - 1] + Vector3.up * 0.05f, facing);
                object sp = markerType.GetField("_spawnPoint")?.GetValue(marker);
                if (sp != null)
                {
                    Type spType = sp.GetType();
                    // PMC or scav: whoever comes into the zombies map starts here
                    SetEnum(sp, spType.GetField("Sides"), "All");
                    SetEnum(sp, spType.GetField("Categories"), "Player");
                    spType.GetField("BotZone")?.SetValue(sp, zone);
                    spType.GetField("Position")?.SetValue(sp, marker.transform.position);
                    spType.GetField("Rotation")?.SetValue(sp, Quaternion.Euler(0f, marker.transform.eulerAngles.y, 0f));
                }
                Undo.RegisterCreatedObjectUndo(marker.gameObject, "Add player spawns");
                EditorUtility.SetDirty(marker);
                existing.Add(marker);
                made.Add(marker.gameObject);
            }
            // The bot zone lists its spawn points
            FieldInfo listField = botZoneType.GetField("SpawnPointMarkers");
            if (listField != null && typeof(IList).IsAssignableFrom(listField.FieldType))
            {
                Undo.RecordObject(zone, "Add player spawns");
                var list = (IList)Activator.CreateInstance(listField.FieldType);
                foreach (Component m in existing)
                {
                    object sp = markerType.GetField("_spawnPoint")?.GetValue(m);
                    object owner = sp?.GetType().GetField("BotZone")?.GetValue(sp);
                    if (owner as Object == zone)
                        list.Add(m);
                }
                listField.SetValue(zone, list);
                EditorUtility.SetDirty(zone);
            }
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            if (made.Count > 0)
                Selection.objects = made.Cast<Object>().ToArray();
            return made.Count > 0
                ? $"{made.Count} player spawns made under {zone.name} (selected): move each to its spot. Solo starts at Spawn_Player_1, a team's players 1-4 at Spawn_Player_1..4. Save, then build the map."
                : "All four player spawns are already there.";
        }

        private static void SetEnum(object target, FieldInfo field, string value)
        {
            if (field == null || !field.FieldType.IsEnum || !Enum.IsDefined(field.FieldType, value))
                return;
            field.SetValue(target, Enum.Parse(field.FieldType, value));
        }

        /// <summary>
        /// Adds an AIDoorsHolder (an empty list of AI door links): without one, the popular Waypoints mod stops a raid on
        /// a custom map from starting.
        /// </summary>
        public static string AddAIDoorsHolder(Scene scene)
        {
            Type t = FindType("AIDoorsHolder");
            if (t == null)
                return "The AIDoorsHolder type wasn't found (is the Tushonka Mapping SDK in this project?).";
            if (HasAIDoorsHolder)
                return "The map already has an AIDoorsHolder.";
            var go = new GameObject("AIDoorsHolder");
            SceneManager.MoveGameObjectToScene(go, scene);
            Component c = go.AddComponent(t);
            FieldInfo links = t.GetField("DoorsLinks");
            if (links != null)
                links.SetValue(c, Activator.CreateInstance(links.FieldType));
            Undo.RegisterCreatedObjectUndo(go, "Add AI doors holder");
            EditorSceneManager.MarkSceneDirty(scene);
            return "AIDoorsHolder added. Save, then build the map.";
        }

        // ---------- Server settings (base.json) ----------

        /// <summary>The map's server settings file (where the SDK keeps it, in the SPT folder), or null.</summary>
        public static string ServerSettingsFile(string mapId)
        {
            string root = KitInstall.SptRoot;
            if (!KitInstall.IsSpt(root) || string.IsNullOrEmpty(mapId))
                return null;
            string p = Path.Combine(root, "SPT_Runtime", "user", "mods", "CustomMapLoader", "maps", mapId, "base.json");
            return File.Exists(p) ? p : null;
        }

        // What a zombies map needs: no bots or waves of its own, a long raid timer, the old spawn system
        private static readonly (string key, object value)[] ZombiesServer =
        {
            ("NewSpawn", false), ("OfflineNewSpawn", false), ("BotMax", 0),
            ("EscapeTimeLimit", 1440), ("EscapeTimeLimitCoop", 1440), ("EscapeTimeLimitPVE", 1440),
            ("waves", new List<object>()), ("BossLocationSpawn", new List<object>()),
        };

        private static string _problemsFile;
        private static DateTime _problemsTime;
        private static List<string> _problems;

        /// <summary>What isn't zombies-ready in the map's server settings (empty: all good). Read again only when the file changes.</summary>
        public static List<string> ServerProblems(string file)
        {
            DateTime time = File.GetLastWriteTimeUtc(file);
            if (_problems != null && file == _problemsFile && time == _problemsTime)
                return _problems;
            _problemsFile = file;
            _problemsTime = time;
            _problems = ReadProblems(file);
            return _problems;
        }

        private static List<string> ReadProblems(string file)
        {
            var list = new List<string>();
            KitJson.JsonObject o;
            try
            {
                o = KitJson.Parse(File.ReadAllText(file)) as KitJson.JsonObject;
            }
            catch (Exception e)
            {
                list.Add("can't be read: " + e.Message);
                return list;
            }
            if (o == null)
            {
                list.Add("isn't a JSON object");
                return list;
            }
            foreach (var (key, want) in ZombiesServer)
            {
                object have = o[key];
                bool ok;
                if (want is bool b)
                    ok = have is bool hb && hb == b;
                else if (want is int n)
                    ok = have is KitJson.JsonNumber hn && Math.Abs(hn.Value - n) < 0.001;
                else
                    ok = have is List<object> hl && hl.Count == 0;
                if (!ok)
                    list.Add(want is List<object> ? $"{key} isn't empty" : $"{key} should be {(want is bool wb ? (wb ? "true" : "false") : want.ToString())}");
            }
            return list;
        }

        /// <summary>Makes the map's server settings zombies-ready (a backup is kept once, beside it). Returns what it did.</summary>
        public static string FixServer(string file)
        {
            var o = KitJson.Parse(File.ReadAllText(file)) as KitJson.JsonObject;
            if (o == null)
                return "The server settings file isn't a JSON object.";
            string backup = file + ".spzombies-backup";
            if (!File.Exists(backup))
                File.Copy(file, backup);
            foreach (var (key, want) in ZombiesServer)
                o[key] = want is int n ? KitJson.Num(n) : want is List<object> ? new List<object>() : want;
            File.WriteAllText(file, KitJson.Write(o));
            return $"Server settings made zombies-ready ({Path.GetFileName(file)}; the old one kept as {Path.GetFileName(backup)}). Restart the SPT server.";
        }
    }
}
