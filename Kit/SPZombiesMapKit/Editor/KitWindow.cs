using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// SPZombies > Map Kit: everything for making a zombies map in one window.
    /// Place: pictures of what you can place, one click to put it where the Scene view looks.
    /// Map: the map's own settings. Check: what would stop it working. Export: write, install and zip it. Setup: the SPT
    /// folder, the SDK's player spawns, AI doors holder and server settings.
    /// </summary>
    internal class KitWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Place", "Map", "Paths", "Check", "Export", "Setup" };
        private int _tab, _placeTab;
        private Vector2 _scroll;
        private string _search = "";
        private KitPlace.Item _picked;
        private readonly KitPlace.Options _options = new KitPlace.Options();
        private string _status = "";
        private MessageType _statusType = MessageType.None;
        private UnityEditor.Editor _settingsEditor;

        [MenuItem("SPZombies/Map Kit", false, 0)]
        public static void Open() => GetWindow<KitWindow>("SPZombies Map Kit").Show();

        private const int PlaceTabIndex = 0, MapTabIndex = 1, PathsTabIndex = 2, CheckTabIndex = 3, ExportTabIndex = 4, SetupTabIndex = 5;

        [MenuItem("SPZombies/Zombie Paths", false, 19)]
        public static void OpenPaths()
        {
            KitWindow w = GetWindow<KitWindow>("SPZombies Map Kit");
            w._tab = PathsTabIndex;
            KitPaths.Run();
            KitPathsView.Seen();
            w.Show();
        }

        [MenuItem("SPZombies/Check Map", false, 20)]
        public static void OpenCheck()
        {
            KitWindow w = GetWindow<KitWindow>("SPZombies Map Kit");
            w._tab = CheckTabIndex;
            KitCheck.Run();
            w.Show();
        }

        [MenuItem("SPZombies/Export Layout", false, 21)]
        public static void MenuExport()
        {
            KitWindow w = GetWindow<KitWindow>("SPZombies Map Kit");
            w._tab = ExportTabIndex;
            w.Report(KitExport.Export(out string summary), summary);
        }

        [MenuItem("SPZombies/Install Into SPT (Test It)", false, 22)]
        public static void MenuInstall()
        {
            KitWindow w = GetWindow<KitWindow>("SPZombies Map Kit");
            w._tab = ExportTabIndex;
            w.Report(KitExport.Install(out string summary), summary);
        }

        [MenuItem("SPZombies/Drop Selected To Ground", false, 40)]
        public static void MenuDrop()
        {
            foreach (GameObject go in Selection.gameObjects)
                KitPlace.DropToGround(go.transform);
        }

        [MenuItem("SPZombies/Guide", false, 60)]
        public static void OpenGuide()
        {
            string guide = AssetDatabase.FindAssets("SPZombiesMapKit_Guide").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (guide != null)
                EditorUtility.OpenWithDefaultApp(Path.GetFullPath(guide));
        }

        private void OnEnable()
        {
            minSize = new Vector2(380f, 420f);
        }

        private void OnDisable()
        {
            if (_settingsEditor != null)
                DestroyImmediate(_settingsEditor);
        }

        private void Report(string error, string summary)
        {
            _status = error ?? summary;
            _statusType = error != null ? MessageType.Error : MessageType.Info;
            Debug.Log("[SPZombies Kit] " + _status);
            Repaint();
        }

        private void OnGUI()
        {
            ZombiesMapSettings s = KitExport.Settings();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(s != null && !string.IsNullOrEmpty(s.MapId) ? $"Map: {s.MapId}" : "No map settings yet", EditorStyles.miniBoldLabel);
                if (!KitInstall.CoreInstalled)
                    GUILayout.Label("SPZombies not found (Setup)", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(new GUIContent($"SPZombies Map Kit {KitInfo.KitVersion} by Kenner",
                    KitInstall.CoreInstalled ? $"SPZombies {KitInstall.CoreVersion} found in your SPT folder." : "SPZombies isn't found: set your SPT folder in Setup."),
                    EditorStyles.miniLabel);
            }
            _tab = GUILayout.Toolbar(_tab, TabNames, GUILayout.Height(24));
            EditorGUILayout.Space(2);
            switch (_tab)
            {
                case PlaceTabIndex: PlaceTab(); break;
                case MapTabIndex: MapTab(s); break;
                case PathsTabIndex: PathsTab(); break;
                case CheckTabIndex: CheckTab(); break;
                case ExportTabIndex: ExportTab(s); break;
                case SetupTabIndex: SetupTab(s); break;
            }
            if (_status.Length > 0)
                EditorGUILayout.HelpBox(_status, _statusType);
            Footer();
        }

        private void Footer()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool p = GUILayout.Toggle(KitPreviews.Show, "Models", EditorStyles.toolbarButton);
                if (p != KitPreviews.Show)
                    KitPreviews.Show = p;
                bool l = GUILayout.Toggle(KitScene.ShowLabels, "Labels", EditorStyles.toolbarButton);
                if (l != KitScene.ShowLabels)
                {
                    KitScene.ShowLabels = l;
                    SceneView.RepaintAll();
                }
                bool a = GUILayout.Toggle(KitScene.ShowArrows, "Arrows", EditorStyles.toolbarButton);
                if (a != KitScene.ShowArrows)
                {
                    KitScene.ShowArrows = a;
                    SceneView.RepaintAll();
                }
                GUILayout.Label("Labels within", EditorStyles.miniLabel, GUILayout.Width(70));
                float d = GUILayout.HorizontalSlider(KitScene.Distance, 10f, 300f, GUILayout.Width(90));
                if (Mathf.Abs(d - KitScene.Distance) > 0.5f)
                {
                    KitScene.Distance = d;
                    SceneView.RepaintAll();
                }
                GUILayout.Label($"{KitScene.Distance:0} m", EditorStyles.miniLabel, GUILayout.Width(40));
                GUILayout.FlexibleSpace();
            }
        }

        // ---------- Place ----------

        private void PlaceTab()
        {
            _placeTab = GUILayout.SelectionGrid(_placeTab, KitPlace.Tabs, 5, EditorStyles.miniButton);
            _search = EditorGUILayout.TextField("Search", _search);
            string tab = KitPlace.Tabs[_placeTab];
            string needle = _search.Trim().ToLowerInvariant();
            List<KitPlace.Item> shown = KitPlace.Items().Where(i => needle.Length > 0 ? i.Label.ToLowerInvariant().Contains(needle) : i.Tab == tab).ToList();
            if (!KitInstall.CoreInstalled)
                EditorGUILayout.HelpBox("SPZombies isn't found: set your SPT folder in the Setup tab to see the real models, guns and decals.", MessageType.Warning);
            if (tab == "Spawns" && needle.Length == 0)
                EditorGUILayout.HelpBox("Players start at the SDK's player spawns Spawn_Player_1..4 (player N at Spawn_Player_N; Setup tab adds them). Player start markers are for maps without those.", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 24f) / 112f));
            int col = 0;
            bool rendered = false;
            EditorGUILayout.BeginHorizontal();
            foreach (KitPlace.Item item in shown)
            {
                if (col == columns)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    col = 0;
                }
                // One model picture a repaint, so the window opens at once
                Texture2D thumb = item.Thumb;
                if (thumb == null && !item.ThumbTried && !rendered)
                {
                    rendered = item.Asset != null;
                    thumb = KitPlace.Thumb(item);
                    Repaint();
                }
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(106)))
                {
                    Color old = GUI.backgroundColor;
                    if (_picked == item)
                        GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
                    Rect r = GUILayoutUtility.GetRect(102, 102);
                    if (GUI.Button(r, GUIContent.none))
                    {
                        if (_picked == item && Event.current.clickCount > 1)
                            Place();
                        Pick(item);
                    }
                    GUI.backgroundColor = old;
                    if (thumb != null)
                        GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), thumb, ScaleMode.ScaleToFit);
                    else
                    {
                        Color c = item.KitComponent != null ? new Color(0.4f, 0.7f, 1f) : KitInfo.Colour(item.Kind);
                        EditorGUI.DrawRect(new Rect(r.x + 30, r.y + 30, r.width - 60, r.height - 60), new Color(c.r, c.g, c.b, 0.85f));
                    }
                    GUILayout.Label(item.Label, EditorStyles.wordWrappedMiniLabel, GUILayout.Width(102));
                }
                col++;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            if (_picked == null)
            {
                EditorGUILayout.HelpBox("Pick something, then Place: it lands where the Scene view's centre points (wall buys and decals on the wall there), facing you.", MessageType.None);
                return;
            }
            EditorGUILayout.LabelField(_picked.Label, EditorStyles.boldLabel);
            if (_picked.KitComponent == null)
            {
                EditorGUILayout.LabelField(KitInfo.Help(_picked.Kind), EditorStyles.wordWrappedMiniLabel);
                if (KitInfo.HasPrice(_picked.Kind))
                    _options.Price = EditorGUILayout.IntField("Price (points)", _options.Price < 0 ? KitInfo.DefaultPrice(_picked.Kind) : _options.Price);
                if (_picked.Kind == ZombiesKind.Door || _picked.Kind == ZombiesKind.Debris)
                    _options.Power = EditorGUILayout.Toggle("Opens with the power", _options.Power);
                if (KitInfo.HasZone(_picked.Kind))
                    _options.Zone = EditorGUILayout.IntField(_picked.Kind == ZombiesKind.Zone ? "Zone number" : "Zone (0: where it stands)", _options.Zone);
                if (_picked.Kind == ZombiesKind.WallBuy)
                    _options.NoBoard = EditorGUILayout.Toggle("No board (chalk on the wall)", _options.NoBoard);
            }
            else if (_picked.KitComponent == typeof(ZombiesZoneOutline))
                _options.Zone = EditorGUILayout.IntField("Zone number", Mathf.Max(1, _options.Zone));
            else if (_picked.KitComponent == typeof(ZombiesGameOverShot))
                EditorGUILayout.LabelField("The shot is placed where the Scene view camera is, looking where it looks.", EditorStyles.wordWrappedMiniLabel);
            if (_picked.Custom)
                EditorGUILayout.HelpBox("Select an object of your map (in the Scene view or the Hierarchy), then press the button: it becomes a " +
                                        KitInfo.KindName(_picked.Kind).ToLowerInvariant() + " exactly where it stands, the kit keeps the original out of the map build, " +
                                        "and SPZombies puts its copy there in a match. Also: right-click it in the Hierarchy > SPZombies.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_picked.Custom ? $"Turn the selected object into a {KitInfo.KindName(_picked.Kind).ToLowerInvariant()}" : "Place where the Scene view looks", GUILayout.Height(28)))
                    Place();
                if (GUILayout.Button("Drop selected to ground", GUILayout.Height(28), GUILayout.Width(160)))
                    MenuDrop();
            }
        }

        private void Pick(KitPlace.Item item)
        {
            if (_picked != item)
                _options.Price = -1;
            _picked = item;
        }

        private void Place()
        {
            if (_picked == null)
                return;
            _status = KitPlace.Place(_picked, _options);
            _statusType = MessageType.Info;
            SceneView.lastActiveSceneView?.Focus();
        }

        // ---------- Map ----------

        private void MapTab(ZombiesMapSettings s)
        {
            if (s == null)
            {
                EditorGUILayout.HelpBox("A zombies map needs its settings: its id (the SDK Map Definition's Map Id), name, time, power and so on. " +
                                        "They go on an editor-only object called SPZombies in the map's scene (the active scene).", MessageType.Info);
                if (GUILayout.Button("Add map settings to " + SceneManager.GetActiveScene().name, GUILayout.Height(30)))
                {
                    Selection.activeObject = KitExport.MakeSettings(SceneManager.GetActiveScene());
                    _status = "Map settings added.";
                }
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            UnityEditor.Editor.CreateCachedEditor(s, typeof(ZombiesMapSettingsEditor), ref _settingsEditor);
            _settingsEditor.OnInspectorGUI();
            EditorGUILayout.Space();
            List<ZombiesMarker> markers = KitExport.InOrder<ZombiesMarker>();
            EditorGUILayout.LabelField("Placed on the map so far", EditorStyles.boldLabel);
            foreach (var g in markers.GroupBy(m => m.Kind).OrderBy(g => g.Key))
                EditorGUILayout.LabelField(KitInfo.KindName(g.Key), g.Count().ToString());
            int outlines = KitExport.InOrder<ZombiesZoneOutline>().Count;
            if (outlines > 0)
                EditorGUILayout.LabelField("Zone outlines", outlines.ToString());
            EditorGUILayout.EndScrollView();
        }

        // ---------- Paths ----------

        private Vector2 _pathsScroll;
        private bool _pathsLegend = true;

        private void PathsTab()
        {
            EditorGUILayout.HelpBox("How zombies will move in a match, worked out on the baked navmesh the way SPZombies decides it: which barricade each zombie spawn feeds and its route there, " +
                                    "where zombies stand at each barricade, and whether they can walk in anywhere without one. Doors, debris and barricades count as shut, as at the start of a match. " +
                                    "Bake the navmesh with the SDK first; work the paths out again after each bake.", MessageType.None);
            if (GUILayout.Button(KitPaths.Ran ? "Work out the zombie paths again" : "Work out the zombie paths", GUILayout.Height(30)))
            {
                KitPaths.Run();
                KitPathsView.Seen();
            }
            // Each toggle saves its setting only when it changes
            void Toggle(bool on, string label, GUIStyle style, System.Action<bool> set)
            {
                bool now = GUILayout.Toggle(on, label, style);
                if (now == on)
                    return;
                set(now);
                SceneView.RepaintAll();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Toggle(KitPathsView.Show, "Show in the Scene view", EditorStyles.miniButtonLeft, v => KitPathsView.Show = v);
                Toggle(KitPathsView.ShowAreas, "Walk areas", EditorStyles.miniButtonMid, v => KitPathsView.ShowAreas = v);
                Toggle(KitPathsView.OnlyProblems, "Only problems", EditorStyles.miniButtonRight, v => KitPathsView.OnlyProblems = v);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Toggle(KitPathsView.ShowInside, "Routes in to the players", EditorStyles.miniButtonLeft, v => KitPathsView.ShowInside = v);
                Toggle(KitPathsView.ShowLegend, "Legend", EditorStyles.miniButtonMid, v => KitPathsView.ShowLegend = v);
                Toggle(KitPathsView.Live, "Update as I move things", EditorStyles.miniButtonRight, v => KitPathsView.Live = v);
            }

            _pathsLegend = EditorGUILayout.Foldout(_pathsLegend, "What the Scene view shows", true);
            if (_pathsLegend)
            {
                Swatch(KitPathsView.Walks, "Green line: the spawn's zombies walk to their barricade (arrows show the way).");
                Swatch(KitPathsView.Partial, "Orange line: the route stops short, at the X: a gap in the navmesh.");
                Swatch(KitPathsView.None, "Red: no route at all, or the spawn is off the navmesh.");
                Swatch(KitPathsView.Inside, "Blue line: no barricade faces the spawn within 35 m, so its zombies start inside and go for the players.");
                Swatch(KitPathsView.LeakColour, "Red dashes: a leak, where zombies can walk in to the players without a barricade.");
                Swatch(KitPathsView.Walks, "At each barricade: big dot = where zombies stand to tear the boards, small cyan dot = where they land inside, bar = the sill (green vaults over, orange climbs on, red too high or low).");
                Swatch(new Color(1f, 0.47f, 0.16f), "Tinted floor: orange the zombie side, green the player side, grey shut by a barricade/door/debris, blue reached by nobody.");
                EditorGUILayout.LabelField("Select a spawn or a barricade to bring its own routes forward. Lines behind walls show faintly.", EditorStyles.wordWrappedMiniLabel);
            }

            if (!KitPaths.Ran)
                return;
            int walks = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.Walks);
            int partial = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.Partial);
            int none = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.None || r.Status == KitPaths.Route.OffMesh);
            int inside = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.Inside);
            using (new EditorGUILayout.HorizontalScope())
            {
                Chip(KitPathsView.Walks, walks, "walk there");
                Chip(KitPathsView.Partial, partial, "stop short");
                Chip(KitPathsView.None, none, "no route");
                Chip(KitPathsView.Inside, inside, "inside");
                Chip(KitPathsView.LeakColour, KitPaths.Leaks.Count, "leaks");
            }
            _pathsScroll = EditorGUILayout.BeginScrollView(_pathsScroll);
            foreach (KitPaths.Note n in KitPaths.Notes.ToList())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(Icon(n.Level), GUILayout.Width(20), GUILayout.Height(20));
                    GUILayout.Label(n.Text, EditorStyles.wordWrappedLabel);
                    if (n.Target != null && GUILayout.Button("Select", GUILayout.Width(52)))
                        Selection.activeObject = n.Target;
                    if (n.Where.HasValue && GUILayout.Button("Show", GUILayout.Width(46)) && SceneView.lastActiveSceneView != null)
                    {
                        SceneView.lastActiveSceneView.Frame(new Bounds(n.Where.Value, Vector3.one * 8f), false);
                        SceneView.lastActiveSceneView.Focus();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void Swatch(Color c, string text)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect r = GUILayoutUtility.GetRect(16, 12, GUILayout.Width(16));
                EditorGUI.DrawRect(new Rect(r.x, r.y + 3, 14, 9), c);
                GUILayout.Label(text, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void Chip(Color c, int count, string text)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(60)))
            {
                Rect r = GUILayoutUtility.GetRect(10, 4, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(r, c);
                GUILayout.Label(count.ToString(), EditorStyles.boldLabel);
                GUILayout.Label(text, EditorStyles.miniLabel);
            }
        }

        // ---------- Check ----------

        private void CheckTab()
        {
            if (GUILayout.Button("Check map", GUILayout.Height(30)))
                KitCheck.Run();
            if (KitCheck.Results.Count == 0)
            {
                EditorGUILayout.HelpBox("Checks the map for what would stop it working in SPZombies (spawns, zones, navmesh, power, window sills, server settings...).", MessageType.None);
                return;
            }
            int errors = KitCheck.Results.Count(r => r.Level == KitCheck.Level.Error), warnings = KitCheck.Results.Count(r => r.Level == KitCheck.Level.Warning);
            EditorGUILayout.LabelField($"{errors} errors, {warnings} warnings (checked {KitCheck.RanAt:HH:mm:ss})", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (KitCheck.Item item in KitCheck.Results.ToList())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(Icon(item.Level), GUILayout.Width(20), GUILayout.Height(20));
                    GUILayout.Label(item.Text, EditorStyles.wordWrappedLabel);
                    if (item.Target != null && GUILayout.Button("Select", GUILayout.Width(52)))
                    {
                        Selection.activeObject = item.Target;
                        if (item.Target is Component || item.Target is GameObject)
                            SceneView.lastActiveSceneView?.FrameSelected();
                    }
                    if (item.Fix != null && GUILayout.Button(item.FixLabel ?? "Fix", GUILayout.Width(80)))
                    {
                        item.Fix();
                        KitCheck.Run();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static GUIContent Icon(KitCheck.Level level)
        {
            switch (level)
            {
                case KitCheck.Level.Error: return EditorGUIUtility.IconContent("console.erroricon.sml");
                case KitCheck.Level.Warning: return EditorGUIUtility.IconContent("console.warnicon.sml");
                case KitCheck.Level.Good: return EditorGUIUtility.IconContent("TestPassed");
                default: return EditorGUIUtility.IconContent("console.infoicon.sml");
            }
        }

        // ---------- Export ----------

        private void ExportTab(ZombiesMapSettings s)
        {
            if (s == null)
            {
                EditorGUILayout.HelpBox("Add the map settings first (Map tab).", MessageType.Info);
                ImportButton();
                return;
            }
            EditorGUILayout.HelpBox("SPZombies reads the zombies layout from " + KitExport.InstallPath(s) + "\\" + s.MapId + "zombies.json, " +
                                    "with the map's sounds, pictures and tips beside it. The map itself (its bundles and server files) comes from the SDK's Build Map.", MessageType.None);
            if (KitInstall.CoreInstalled && KitInstall.CoreTooOld)
                EditorGUILayout.HelpBox($"Your SPT folder has SPZombies {KitInstall.CoreVersion}: maps made with this kit need {KitInfo.CoreNeeded} or newer. Update SPZombies to test it.", MessageType.Warning);
            if (GUILayout.Button("Export layout", GUILayout.Height(28)))
                Report(KitExport.Export(out string summary), summary);
            EditorGUILayout.LabelField("Saved inside this Unity project: " + KitExport.MapsDir(s), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            GUI.enabled = KitInstall.IsSpt(KitInstall.SptRoot);
            if (GUILayout.Button("Install into SPT (test it)", GUILayout.Height(28)))
                Report(KitExport.Install(out string summary), summary);
            GUI.enabled = true;
            EditorGUILayout.LabelField("Copies the zombies folder into " + (KitInstall.IsSpt(KitInstall.SptRoot) ? KitInstall.Plugins + " (your SPT folder, set in Setup)" : "your SPT folder (set it in Setup)") +
                                       ". Build the map with the SDK (Deploy To Game) too, then restart the SPT server and the game.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            if (GUILayout.Button("Make release zip", GUILayout.Height(28)))
                Report(KitExport.ReleaseZip(out string summary), summary);
            EditorGUILayout.LabelField("The SDK's map package (Build Map with Create Package ticked) and the zombies folder in one zip, with a read-me: " +
                                       "players extract it into their SPT folder.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open the export folder"))
                {
                    Directory.CreateDirectory(KitExport.ExportRoot(s));
                    EditorUtility.RevealInFinder(KitExport.ExportRoot(s));
                }
                ImportButton();
            }
        }

        private void ImportButton()
        {
            if (!GUILayout.Button("Import a layout file..."))
                return;
            string file = EditorUtility.OpenFilePanel("A zombies layout (<map>zombies.json)", KitInstall.IsSpt(KitInstall.SptRoot) ? KitInstall.Plugins : "", "json");
            if (string.IsNullOrEmpty(file))
                return;
            bool any = KitExport.InOrder<ZombiesMarker>(false).Count > 0;
            int choice = any ? EditorUtility.DisplayDialogComplex("SPZombies Map Kit", "The map already has markers. Replace them with the file's, or add the file's to them?", "Replace", "Cancel", "Add") : 0;
            if (choice == 1)
                return;
            try
            {
                Report(null, KitExport.Import(file, choice == 0));
            }
            catch (System.Exception e)
            {
                Report("Couldn't import it: " + e.Message, null);
            }
            GUIUtility.ExitGUI();
        }

        // ---------- Setup ----------

        private void SetupTab(ZombiesMapSettings s)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("SPT folder", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("The SPT install with SPZombies in it (the folder with BepInEx and SPT_Runtime). The kit reads SPZombies' models, guns and decals from it, and installs your map there to test.", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                string root = EditorGUILayout.DelayedTextField(KitInstall.SptRoot);
                if (root != KitInstall.SptRoot)
                    SetRoot(root);
                if (GUILayout.Button("Browse...", GUILayout.Width(80)))
                {
                    string picked = EditorUtility.OpenFolderPanel("Your SPT folder", KitInstall.SptRoot, "");
                    if (!string.IsNullOrEmpty(picked))
                        SetRoot(picked);
                }
            }
            if (!KitInstall.IsSpt(KitInstall.SptRoot))
                EditorGUILayout.HelpBox("That isn't an SPT folder (no BepInEx\\plugins in it).", MessageType.Warning);
            Found("SPZombies", KitInstall.Find(KitInstall.CoreDll), KitInstall.CoreVersion);
            Found("SPZombies models", KitInstall.Find(KitModels.ZombiesBundle), null);
            Found("Perk Beverages (perk machines)", KitInstall.Find(KitModels.PerksBundle), null);
            Found("Pack-a-Punch (its machine)", KitInstall.Find(KitModels.PapBundle), null);
            if (GUILayout.Button("Reload models (after updating SPZombies)"))
            {
                KitModels.Reload();
                KitPlace.Forget();
                _status = "Models reloaded.";
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tushonka Mapping SDK", EditorStyles.boldLabel);
            if (!KitSdk.Present)
            {
                EditorGUILayout.HelpBox("The Tushonka Mapping SDK isn't in this project. Zombies maps are built with it (its CustomMapLoader loads them): make your map in its Unity project and add this kit there.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }
            KitSdk.MapDef def = KitSdk.For(s != null ? s.MapId : null);
            EditorGUILayout.LabelField("Map Definition", def != null ? $"{def.MapId} ({def.ScenePaths.Count} scenes)" : "none found (make the map with the SDK first)");
            List<Component> spawns = KitSdk.PlayerSpawns();
            EditorGUILayout.LabelField("Player spawns", spawns.Count > 0 ? string.Join(", ", spawns.Select(c => c.name)) : "none");
            if (GUILayout.Button("Add player spawns 1-4 where the Scene view looks"))
            {
                SceneView view = SceneView.lastActiveSceneView;
                Vector3 at = view != null ? view.pivot : Vector3.zero;
                Physics.SyncTransforms();
                if (view != null && Physics.Raycast(new Ray(view.camera.transform.position, view.camera.transform.forward), out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                    at = hit.point;
                Vector3 fwd = view != null ? Vector3.ProjectOnPlane(view.rotation * Vector3.forward, Vector3.up) : Vector3.forward;
                Quaternion facing = fwd.sqrMagnitude > 0.001f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;
                _status = KitSdk.AddPlayerSpawns(KitExport.MapScene(), at, facing);
                _statusType = MessageType.Info;
            }
            EditorGUILayout.LabelField("AI doors holder", KitSdk.HasAIDoorsHolder ? "yes" : "no (the Waypoints mod needs one)");
            if (!KitSdk.HasAIDoorsHolder && GUILayout.Button("Add an AI doors holder"))
                _status = KitSdk.AddAIDoorsHolder(KitExport.MapScene());
            string server = s != null ? KitSdk.ServerSettingsFile(s.MapId) : null;
            List<string> problems = server != null ? KitSdk.ServerProblems(server) : null;
            EditorGUILayout.LabelField("Server settings", server == null ? "not found (make them with the SDK's Edit Server Data)" : problems.Count == 0 ? "zombies-ready" : string.Join(", ", problems));
            if (server != null && problems.Count > 0 && GUILayout.Button("Make the server settings zombies-ready"))
                _status = KitSdk.FixServer(server);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"SPZombies Map Kit {KitInfo.KitVersion}", EditorStyles.miniLabel);
            if (GUILayout.Button("Open the guide"))
                OpenGuide();
            EditorGUILayout.EndScrollView();
        }

        private void SetRoot(string root)
        {
            KitInstall.SptRoot = root;
            KitModels.Reload();
            KitPlace.Forget();
        }

        private static void Found(string what, string path, string version)
        {
            EditorGUILayout.LabelField(what, path != null ? "found" + (string.IsNullOrEmpty(version) ? "" : " (" + version + ")") : "not found");
        }
    }
}
