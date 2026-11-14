using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Test only (copied into the test project, never shipped): imports Stronghold's real layout, builds every preview from
    /// the installed bundles, exports it again and compares with the original, runs the check and the SDK helpers, and
    /// renders pictures of the previews. Run: Unity -batchmode -executeMethod SPZombies.MapKit.Editor.KitSelfTest.Run
    /// </summary>
    internal static class KitSelfTest
    {
        private const string Spt = @"D:\OTHER LAUNCHERS\BUTTERSTATE";
        private const string SdkExampleBase = @"C:\Git\2_References\TushonkaMappingSDK-1.9.3\Server\maps\sdk_example\base.json";
        private static string Out => Path.GetFullPath(Path.Combine(KitExport.ProjectRoot, "..", "tools", "selftest"));
        private static readonly List<string> Log = new List<string>();

        private static void Say(string s)
        {
            Log.Add(s);
            Debug.Log("[SelfTest] " + s);
        }

        public static void Run()
        {
            if (Directory.Exists(Out))
                Directory.Delete(Out, true);
            Directory.CreateDirectory(Out);
            try
            {
                Steps();
            }
            catch (Exception e)
            {
                Say("EXCEPTION " + e);
            }
            try
            {
                PathsTest();
            }
            catch (Exception e)
            {
                Say("PATHS EXCEPTION " + e);
            }
            try
            {
                WallAndTurnTest();
            }
            catch (Exception e)
            {
                Say("PATHS EXCEPTION " + e);
            }
            try
            {
                MapSettingsTest();
            }
            catch (Exception e)
            {
                Say("SETTINGS EXCEPTION " + e);
            }
            File.WriteAllLines(Path.Combine(Out, "selftest.txt"), Log);
        }

        // ---------- Map settings: files, conversion, tips, start gun, perk prices ----------

        private static string AudioIn => Path.GetFullPath(Path.Combine(KitExport.ProjectRoot, "..", "tools", "audio_in"));

        private static (float peak, float seconds, int channels, int rate) WavStats(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int channels = BitConverter.ToInt16(b, 22), rate = BitConverter.ToInt32(b, 24);
            int data = BitConverter.ToInt32(b, 40);
            int peak = 0;
            for (int i = 44; i + 1 < 44 + data && i + 1 < b.Length; i += 2)
                peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(b, i)));
            return (peak / 32767f, data / 2f / channels / rate, channels, rate);
        }

        private static void MapSettingsTest()
        {
            Say("---- MAP SETTINGS TEST ----");
            Say($"CORE version '{KitInstall.CoreVersion}', too old for the kit: {KitInstall.CoreTooOld}");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ZombiesMapSettings s = KitExport.MakeSettings(scene);
            s.MapId = "zm_kittest";
            s.MapName = "KitTest";
            Say($"folder {KitExport.FolderName(s)}");

            // Sounds from outside the project, every kind
            foreach (string input in Directory.Exists(AudioIn) ? Directory.GetFiles(AudioIn) : new string[0])
            {
                string name = "snd_" + Path.GetFileNameWithoutExtension(input);
                AudioClip clip = KitFiles.AddSound(s, input, name, out string msg);
                string asset = clip != null ? AssetDatabase.GetAssetPath(clip) : null;
                string problem = asset != null ? KitExport.WavProblem(asset) : "no clip";
                var st = asset != null ? WavStats(asset) : default;
                Say($"SOUND {Path.GetFileName(input)} (source problem: {KitExport.WavProblem(input) ?? "none"}) -> {msg} | now: {problem ?? "ok"}, " +
                    $"{st.seconds:0.00} s, peak {st.peak:0.00}, {st.channels} ch, {st.rate} Hz; leftovers {Directory.GetFiles(Path.Combine(KitExport.ProjectRoot, KitFiles.DataFolder(s)), "_converting_*").Length}");
            }
            // A clip already in the project that can't be played: converted beside it
            string raw = Path.Combine(AudioIn, "tone_24bit.wav");
            if (File.Exists(raw))
            {
                Directory.CreateDirectory(Path.Combine(KitExport.ProjectRoot, "Assets", "SelfTest"));
                File.Copy(raw, Path.Combine(KitExport.ProjectRoot, "Assets", "SelfTest", "raw24.wav"), true);
                AssetDatabase.ImportAsset("Assets/SelfTest/raw24.wav");
                AudioClip made = KitFiles.ConvertClip(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SelfTest/raw24.wav"), out string msg);
                string asset = made != null ? AssetDatabase.GetAssetPath(made) : null;
                Say($"CONVERT in project -> {msg} | {asset}: {(asset != null ? KitExport.WavProblem(asset) ?? "ok" : "-")}, peak {(asset != null ? WavStats(asset).peak : 0f):0.00}");
            }
            // A picture
            var tex = new Texture2D(1920, 1080);
            string png = Path.Combine(Out, "pic_in.png");
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Texture2D pic = KitFiles.AddPicture(s, png, "banner", out string pmsg);
            Say($"PICTURE -> {pmsg} | {AssetDatabase.GetAssetPath(pic)} {KitFiles.PictureSize(pic)}");
            s.Banner = pic;

            // Things on the map
            void Mark(ZombiesKind k, string what, int price = 0, bool power = false)
            {
                var go = new GameObject(k.ToString()) { tag = "EditorOnly" };
                go.transform.SetParent(KitExport.Folder(scene, KitExport.Category(k)), false);
                ZombiesMarker m = go.AddComponent<ZombiesMarker>();
                m.Kind = k;
                m.What = what;
                m.Price = price;
                m.OpensWithPower = power;
            }
            Mark(ZombiesKind.PerkMachine, "jugg");
            Mark(ZombiesKind.PerkMachine, "revive");
            Mark(ZombiesKind.PerkMachine, "speed");
            Mark(ZombiesKind.PackAPunch, "");
            Mark(ZombiesKind.PowerSwitch, "");
            Mark(ZombiesKind.BoxSpot, "");
            Mark(ZombiesKind.BoxSpot, "");
            Mark(ZombiesKind.Rock, "easter_egg");
            Mark(ZombiesKind.Rock, "easter_egg");
            Mark(ZombiesKind.Rock, "lore");
            Mark(ZombiesKind.Door, "metal_rusted", 0, true);
            KitInstall.Gun wall = KitInstall.Guns().FirstOrDefault(g => g.Role == "wall" && g.Id != KitInfo.GrenadesWallId);
            if (wall != null)
                Mark(ZombiesKind.WallBuy, wall.Id, 1250);
            Mark(ZombiesKind.WallBuy, KitInfo.GrenadesWallId, 250);

            KitInstall.Gun pistol = KitInstall.Guns().FirstOrDefault(g => g.Class == "pistol" && g.Id != KitInfo.DefaultStartGun)
                                    ?? KitInstall.Guns().FirstOrDefault(g => g.Id != KitInfo.DefaultStartGun && g.Id != KitInfo.GrenadesWallId);
            s.StartGun = pistol?.Id ?? "m9a3";
            s.MaxPerks = 5;
            s.CustomPerkPrices = true;
            s.PerkPrices = KitInfo.PerkIds.Select(p => new ZombiesMapSettings.PerkPrice { Id = p, Price = KitInfo.PerkDefaultPrice(p) }).ToList();
            s.PerkPrices.First(p => p.Id == "jugg").Price = 3000;
            s.PerkPrices.First(p => p.Id == "revive").Price = 2000;

            List<string> tips = KitTips.Missing(s);
            foreach (string t in tips)
                Say("TIP " + t);
            s.Tips = string.Join("\n", tips);
            Say($"tips missing after adding: {KitTips.Missing(s).Count}; wrong prices: {KitTips.WrongPrices(s).Count}");
            s.PerkPrices.First(p => p.Id == "jugg").Price = 3500;
            s.PerkPrices.First(p => p.Id == "revive").Price = 2200;
            foreach (string w in KitTips.WrongPrices(s))
                Say("WRONG " + w);

            string err = KitExport.Export(out string summary);
            Say("EXPORT " + (err ?? summary));
            string file = Path.Combine(KitExport.MapsDir(s), s.MapId + "zombies.json");
            var o = (KitJson.JsonObject)KitJson.Parse(File.ReadAllText(file));
            Say($"layout StartGun {o.Str("StartGun")}, MaxPerks {o.Num("MaxPerks")}, PerkPrices {(o.Obj("PerkPrices") != null ? string.Join(",", o.Obj("PerkPrices").Keys.Select(k => k + "=" + o.Obj("PerkPrices").Num(k))) : "none")}, " +
                $"in {file.Contains(Path.Combine("SPZombies_Maps", "ZM_KitTest"))}");
            Say("files: " + string.Join(", ", Directory.GetFiles(KitExport.MapsDir(s)).Select(Path.GetFileName)));
            foreach (KitCheck.Item r in KitCheck.Run().Where(r => r.Level != KitCheck.Level.Good))
                Say($"CHECK {r.Level}: {r.Text}");

            // Read back into a fresh scene
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Say("IMPORT " + KitExport.Import(file, true));
            ZombiesMapSettings back = KitExport.Settings();
            Say($"back: name {back.MapName}, start gun {back.StartGun}, custom prices {back.CustomPerkPrices}, jugg {back.PriceOf("jugg")}, revive {back.PriceOf("revive")}, " +
                $"speed {back.PriceOf("speed")}, max perks {back.MaxPerks}, tips {KitTips.Lines(back.Tips).Count}");
            // A plain map writes no start gun or prices (older SPZombies reads the same file)
            back.StartGun = KitInfo.DefaultStartGun;
            back.CustomPerkPrices = false;
            KitExport.Export(out _);
            var plain = (KitJson.JsonObject)KitJson.Parse(File.ReadAllText(file));
            Say($"plain: StartGun {(plain["StartGun"] == null ? "null" : plain.Str("StartGun"))}, PerkPrices {(plain["PerkPrices"] == null ? "null" : "set")}");
        }

        // ---------- Wall buy chalk and turning an object into a door ----------

        private static void WallAndTurnTest()
        {
            Say("---- WALL + TURN TEST ----");
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.62f);
            KitExport.MakeSettings(UnityEngine.SceneManagement.SceneManager.GetActiveScene()).MapId = "zm_test";
            string[] guns = { "mp5k", "deagle", "vector", "thundergun", "uzi", KitInfo.GrenadesWallId, "m1a" };
            for (int i = 0; i < guns.Length; i++)
                Mark(ZombiesKind.WallBuy, new Vector3(i * 1.3f, 1.2f, 0f), 0f, Vector3.one, guns[i]);
            KitPreviews.RefreshNow();
            foreach (ZombiesMarker m in KitExport.InOrder<ZombiesMarker>())
                Say($"  wall {m.What}: board {m.PreviewBox.size}, chalk broken {KitInstall.ChalkBroken(m.What)}");
            var camGo = new GameObject("cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.45f, 0.45f, 0.48f);
            var rt = new RenderTexture(1600, 500, 24);
            cam.targetTexture = rt;
            cam.fieldOfView = 30f;
            Snap(cam, rt, new Vector3(3.9f, 1.4f, 9f), new Vector3(3.9f, 1.4f, 0f), "wallbuys");
            rt.Release();
            Object.DestroyImmediate(camGo);

            // A two-piece "door" with a detail group, picked by its first piece (as a click in the Scene view does)
            var door = new GameObject("Old Barn Door");
            door.transform.SetPositionAndRotation(new Vector3(10f, 0f, 5f), Quaternion.Euler(0f, 30f, 0f));
            GameObject a = GameObject.CreatePrimitive(PrimitiveType.Cube);
            a.name = "Barn_LOD0";
            a.transform.SetParent(door.transform, false);
            a.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            a.transform.localScale = new Vector3(1.6f, 2.2f, 0.12f);
            door.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.01f, new Renderer[] { a.GetComponent<Renderer>() }) });
            Say("TURN " + KitTurnInto.TurnInto(a, ZombiesKind.Door, new KitPlace.Options()));
            ZombiesMarker made = KitExport.InOrder<ZombiesMarker>().FirstOrDefault(m => m.Kind == ZombiesKind.Door);
            Say($"  marker at {made?.transform.position}, turn {made?.transform.eulerAngles.y:0}, size {made?.Size}, look {made?.CustomLook?.name}; original active {door.activeSelf}, tag {door.tag}, still built {(made != null && KitTurnInto.OriginalStillBuilt(made))}");
            KitPreviews.RefreshNow();
            Say($"  preview box {made?.PreviewBox.size}");
            string err = KitExport.Export(out string summary);
            Say("  EXPORT " + (err ?? summary).Replace('\n', '|'));
            KitTurnInto.PutBack(made);
            Say($"  put back: original active {door.activeSelf}, tag {door.tag}, markers left {KitExport.InOrder<ZombiesMarker>().Count(m => m.Kind == ZombiesKind.Door)}");
        }

        // ---------- Zombie paths on a small built map with known answers ----------

        private static GameObject Block(string name, Vector3 centre, Vector3 size)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = centre;
            go.transform.localScale = size;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
            return go;
        }

        private static ZombiesMarker Mark(ZombiesKind kind, Vector3 at, float turn, Vector3 scale, string what = "")
        {
            var go = new GameObject(kind.ToString()) { tag = "EditorOnly" };
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, turn, 0f));
            go.transform.localScale = scale;
            ZombiesMarker m = go.AddComponent<ZombiesMarker>();
            m.Kind = kind;
            m.What = what;
            m.Price = 750;
            return m;
        }

        private static void PathsTest()
        {
            Say("---- PATHS TEST ----");
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            Block("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(70f, 1f, 70f));
            // A 10 x 10 room, z 5..15: a window (sill 0.9 m) in the south wall, a doorway in the north wall
            const float H = 3f, T = 0.3f;
            Block("West", new Vector3(-5f, H / 2, 10f), new Vector3(T, H, 10f));
            Block("East", new Vector3(5f, H / 2, 10f), new Vector3(T, H, 10f));
            Block("South L", new Vector3(-2.85f, H / 2, 5f), new Vector3(4.3f, H, T));
            Block("South R", new Vector3(2.85f, H / 2, 5f), new Vector3(4.3f, H, T));
            Block("Sill", new Vector3(0f, 0.45f, 5f), new Vector3(1.4f, 0.9f, T));
            Block("Lintel S", new Vector3(0f, 2.65f, 5f), new Vector3(1.4f, 0.7f, T));
            Block("North L", new Vector3(-3.15f, H / 2, 15f), new Vector3(3.7f, H, T));
            Block("North R", new Vector3(3.15f, H / 2, 15f), new Vector3(3.7f, H, T));
            Block("Lintel N", new Vector3(0f, 2.6f, 15f), new Vector3(2.6f, 0.8f, T));
            // A cut-off platform
            Block("Platform", new Vector3(20f, 1.5f, -20f), new Vector3(5f, 3f, 5f));
            // The navmesh bake wants a saved scene
            Directory.CreateDirectory(Path.Combine(KitExport.ProjectRoot, "Assets", "SelfTest"));
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/SelfTest/PathsTest.unity");
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            Say($"agent radius {UnityEngine.AI.NavMesh.GetSettingsByIndex(0).agentRadius}, navmesh triangles {UnityEngine.AI.NavMesh.CalculateTriangulation().indices.Length / 3}");

            ZombiesMarker window = Mark(ZombiesKind.Window, new Vector3(0f, 0.9f, 5f), 180f, new Vector3(1.4f, 1.4f, 0.3f));
            ZombiesMarker door = Mark(ZombiesKind.Door, new Vector3(0f, 0f, 15f), 0f, new Vector3(2.6f, 2.2f, 0.3f), "metal_rusted");
            ZombiesMarker a = Mark(ZombiesKind.ZombieSpawn, new Vector3(0f, 0f, -5f), 0f, Vector3.one);
            a.name = "Spawn A (in front of the window)";
            ZombiesMarker b = Mark(ZombiesKind.ZombieSpawn, new Vector3(20f, 3f, -20f), 0f, Vector3.one);
            b.name = "Spawn B (cut-off platform)";
            ZombiesMarker c = Mark(ZombiesKind.ZombieSpawn, new Vector3(0f, 0f, 25f), 180f, Vector3.one);
            c.name = "Spawn C (north, no window faces it)";
            ZombiesMarker start = Mark(ZombiesKind.PlayerStart, new Vector3(0f, 0f, 10f), 0f, Vector3.one);

            KitPaths.Run();
            Report("with the door shut");
            PathsShot("paths_door_shut");

            Object.DestroyImmediate(door.gameObject);
            KitPaths.Run();
            Report("without a door in the doorway");
            PathsShot("paths_no_door");
        }

        private static void Report(string what)
        {
            Say($"[{what}] navmesh {KitPaths.HasNavMesh}, triangles {KitPaths.Triangles}, leaks {KitPaths.Leaks.Count}");
            Say("  detail: " + KitPaths.Detail);
            foreach (KitPaths.SpawnRoute r in KitPaths.Routes)
                Say($"  route {r.Spawn.name}: {r.Status} via {(r.Window != null ? r.Window.name : "-")}, {r.Corners.Length} corners, {r.Length:0.0} m{(r.Status == KitPaths.Route.Partial ? $", stops {r.Short:0.0} m short" : "")}");
            foreach (KitPaths.WindowInfo w in KitPaths.Windows)
                Say($"  window: feeds {w.Feeds}, outside on mesh {w.OutsideOnMesh} at {w.Outside}, inside {w.InsideOnMesh}, inside reaches players {w.InsideReaches}, {w.Verdict}");
            Say($"  areas: zombie {KitPaths.ZombieArea:0} m2, player {KitPaths.PlayerArea:0} m2, shut {KitPaths.BlockedArea:0} m2, nobody {KitPaths.LoneArea:0} m2");
            foreach (KitPaths.Note n in KitPaths.Notes)
                Say($"  NOTE {n.Level}: {n.Text}");
        }

        // The walk areas and routes from above (a stand-in for the Scene view drawing)
        private static void PathsShot(string name)
        {
            var temp = new List<GameObject>();
            var mat = new Material(Shader.Find("Sprites/Default"));
            if (KitPaths.Areas != null)
            {
                var area = new GameObject("areas");
                area.AddComponent<MeshFilter>().sharedMesh = KitPaths.Areas;
                area.AddComponent<MeshRenderer>().sharedMaterial = mat;
                temp.Add(area);
            }
            void Line(Vector3[] pts, Color c)
            {
                if (pts.Length < 2)
                    return;
                var go = new GameObject("line");
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.startColor = lr.endColor = c;
                lr.widthMultiplier = 0.35f;
                lr.positionCount = pts.Length;
                lr.SetPositions(pts.Select(p => p + Vector3.up * 0.3f).ToArray());
                temp.Add(go);
            }
            foreach (KitPaths.SpawnRoute r in KitPaths.Routes)
                Line(r.Status == KitPaths.Route.None ? new[] { r.From, r.To } : r.Corners, KitPathsView.Colour(r.Status));
            foreach (KitPaths.Leak l in KitPaths.Leaks)
                Line(l.Route, KitPathsView.LeakColour);
            var camGo = new GameObject("cam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.22f);
            var rt = new RenderTexture(1024, 1024, 24);
            cam.targetTexture = rt;
            Snap(cam, rt, new Vector3(5f, 60f, 2f), new Vector3(5f, 0f, 2.01f), name);
            rt.Release();
            Object.DestroyImmediate(camGo);
            foreach (GameObject go in temp)
                Object.DestroyImmediate(go);
        }

        private static void Steps()
        {
            KitInstall.SptRoot = Spt;
            Say($"core {KitInstall.CoreInstalled} {KitInstall.CoreVersion} at {KitInstall.CoreDir}; guns {KitInstall.Guns().Count}; decals {KitInstall.DecalNames().Count}");
            Say($"bundles: zombies {KitModels.Bundle(KitModels.ZombiesBundle) != null}, perks {KitModels.Bundle(KitModels.PerksBundle) != null}, pap {KitModels.Bundle(KitModels.PapBundle) != null}");
            Say("door looks: " + string.Join(",", KitModels.Looks("zm_door_", KitInfo.DoorLooks)));
            Say("debris looks: " + string.Join(",", KitModels.Looks("zm_debris_", KitInfo.DebrisLooks)));
            Say("zombies bundle names: " + string.Join(",", KitModels.Names(KitModels.ZombiesBundle).Take(80)));
            Say("perk bundle names: " + string.Join(",", KitModels.Names(KitModels.PerksBundle).Where(n => n.StartsWith("perk_")).Take(40)));
            Say($"SDK present {KitSdk.Present}, Map Definition type {KitSdk.MapDefinitionType}");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);
            // A map object named like Stronghold's street lamp fixture (with a detail group, as SPZombies looks for)
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lamp.name = "Streetlight_lamp_07_B_source_on";
            lamp.transform.position = new Vector3(0f, -50f, 0f);
            lamp.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
            lamp.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.01f, new Renderer[] { lamp.GetComponent<Renderer>() }) });
            string layout = Path.Combine(Spt, @"BepInEx\plugins\Kennerz_ZM_Stronghold\maps\zm_strongholdzombies.json");
            Say(KitExport.Import(layout, true));
            KitPreviews.Show = true;
            KitPreviews.RefreshNow();
            ZombiesMarker lampMarker = KitExport.InOrder<ZombiesMarker>().FirstOrDefault(m => m.Kind == ZombiesKind.Lamp);
            Say($"NAMED LOOK lamp preview box {lampMarker?.PreviewBox.size} (a 0.3 x 4 x 0.3 cylinder when the map object is found)");
            List<ZombiesMarker> markers = KitExport.InOrder<ZombiesMarker>();
            Say($"markers {markers.Count}, previews: " + string.Join(", ", markers.GroupBy(m => m.Kind).Select(g => $"{g.Key} {g.Count(m => m.HasPreviewBox)}/{g.Count()}")));
            foreach (var g in markers.GroupBy(m => m.Kind))
            {
                ZombiesMarker m = g.First();
                Say($"  {g.Key} '{m.What}': preview box {m.PreviewBox.size} at {m.PreviewBox.center}");
            }

            string err = KitExport.Export(out string summary);
            Say("EXPORT " + (err ?? summary));
            ZombiesMapSettings s = KitExport.Settings();
            Compare(layout, Path.Combine(KitExport.MapsDir(s), s.MapId + "zombies.json"));
            CompareExtras(Path.GetDirectoryName(layout), KitExport.MapsDir(s), s.MapId);

            foreach (KitCheck.Item r in KitCheck.Run())
                Say($"CHECK {r.Level}: {r.Text}");

            Shots(markers);
            Thumbs();

            // A custom door look: export keeps a copy of it in the map, the layout names that copy
            GameObject myDoor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            myDoor.name = "My Fancy Door";
            myDoor.transform.localScale = new Vector3(1.2f, 2.1f, 0.1f);
            GameObjectUtility.SetStaticEditorFlags(myDoor, StaticEditorFlags.BatchingStatic);
            ZombiesMarker door = KitExport.InOrder<ZombiesMarker>().First(m => m.Kind == ZombiesKind.Door);
            door.CustomLook = myDoor;
            KitPreviews.RefreshNow();
            Say($"CUSTOM door preview box {door.PreviewBox.size}");
            err = KitExport.Export(out summary);
            Say("EXPORT 2 " + (err ?? summary));
            GameObject looksRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == KitExport.LooksRootName);
            Transform copy = looksRoot != null && looksRoot.transform.childCount > 0 ? looksRoot.transform.GetChild(0) : null;
            Say($"CUSTOM looks root {(looksRoot != null)}, copy {copy?.name}, detail group {(copy != null && copy.GetComponent<LODGroup>() != null)}, " +
                $"static {(copy != null ? GameObjectUtility.GetStaticEditorFlags(copy.gameObject).ToString() : "-")}, tag {copy?.tag}, scale {copy?.lossyScale}");
            var again = (KitJson.JsonObject)KitJson.Parse(File.ReadAllText(Path.Combine(KitExport.MapsDir(s), s.MapId + "zombies.json")));
            Say("CUSTOM layout What: " + string.Join(",", again.Arr("Entries").Cast<KitJson.JsonObject>().Where(e => e.Str("Type") == "Door").Select(e => e.Str("What"))));

            // Placing: one of each tab (no Scene view in batch mode: at the origin)
            int before = KitExport.InOrder<ZombiesMarker>().Count;
            foreach (string tab in KitPlace.Tabs)
            {
                KitPlace.Item item = KitPlace.Items().FirstOrDefault(i => i.Tab == tab);
                if (item != null)
                    Say("PLACE " + KitPlace.Place(item, new KitPlace.Options { Zone = 3 }));
            }
            KitPreviews.RefreshNow();
            Say($"markers after placing {KitExport.InOrder<ZombiesMarker>().Count} (was {before})");

            Say("SPAWNS " + KitSdk.AddPlayerSpawns(scene, new Vector3(208f, 2f, -146f), Quaternion.identity));
            Say($"player spawns {string.Join(",", KitSdk.PlayerSpawns().Select(c => c.name))}; bot zone {KitSdk.HasBotZone}; spawn points {KitSdk.SpawnPointCount}");
            Say("SPAWNS again " + KitSdk.AddPlayerSpawns(scene, new Vector3(208f, 2f, -146f), Quaternion.identity));
            Say("DOORS " + KitSdk.AddAIDoorsHolder(scene) + $" -> {KitSdk.HasAIDoorsHolder}");

            string tmp = Path.Combine(Out, "base_test.json");
            File.Copy(SdkExampleBase, tmp, true);
            Say("server before: " + string.Join("; ", KitSdk.ServerProblems(tmp)));
            Say(KitSdk.FixServer(tmp));
            Say("server after: " + string.Join("; ", KitSdk.ServerProblems(tmp)));

            Directory.CreateDirectory(Path.Combine(KitExport.ProjectRoot, "Assets", "SelfTest"));
            EditorSceneManager.SaveScene(scene, "Assets/SelfTest/SelfTest.unity");
            string yaml = File.ReadAllText(Path.Combine(KitExport.ProjectRoot, "Assets", "SelfTest", "SelfTest.unity"));
            Say($"saved scene holds previews: {yaml.Contains(KitPreviews.RootName)}; holds markers: {yaml.Contains("Kind:")}");
            int editorOnly = markers.Count(m => m.gameObject.CompareTag("EditorOnly"));
            Say($"markers tagged EditorOnly: {editorOnly}/{markers.Count}");
        }

        private static void Compare(string original, string exported)
        {
            var a = (KitJson.JsonObject)KitJson.Parse(File.ReadAllText(original));
            var b = (KitJson.JsonObject)KitJson.Parse(File.ReadAllText(exported));
            foreach (string key in new[] { "Map", "ZombiesMap", "MapSelectSpot", "Moonlight", "MaxPerks", "AlwaysPowered", "Hour" })
                Say($"  setting {key}: {Show(a[key])} -> {Show(b[key])}");
            Say($"  weather: {KitJson.Write(a["Weather"]).Trim().Replace("\n", "")} -> {KitJson.Write(b["Weather"]).Trim().Replace("\n", "")}");
            List<KitJson.JsonObject> ea = a.Arr("Entries").Cast<KitJson.JsonObject>().ToList(), eb = b.Arr("Entries").Cast<KitJson.JsonObject>().ToList();
            Say($"  entries {ea.Count} -> {eb.Count}");
            var used = new HashSet<int>();
            int bad = 0;
            for (int i = 0; i < ea.Count; i++)
            {
                KitJson.JsonObject x = ea[i];
                Vector3 px = V(x.Floats("Position"));
                int j = eb.FindIndex(y => !used.Contains(eb.IndexOf(y)) && y.Str("Type") == x.Str("Type") && (V(y.Floats("Position")) - px).magnitude < 0.002f);
                if (j < 0)
                {
                    Say($"  MISSING {x.Str("Type")} at {px}");
                    bad++;
                    continue;
                }
                used.Add(j);
                KitJson.JsonObject y2 = eb[j];
                var diffs = new List<string>();
                if ((x.Str("What") ?? "") != (y2.Str("What") ?? ""))
                    diffs.Add($"What {x.Str("What")} vs {y2.Str("What")}");
                foreach (string k in new[] { "Price", "Zone" })
                    if (Math.Abs(x.Num(k) - y2.Num(k)) > 0.001f)
                        diffs.Add($"{k} {x.Num(k)} vs {y2.Num(k)}");
                foreach (string k in new[] { "OpensWithPower", "NoBoard" })
                    if (x.Bool(k) != y2.Bool(k))
                        diffs.Add($"{k} {x.Bool(k)} vs {y2.Bool(k)}");
                Quaternion qa = Quaternion.Euler(x.Num("Tilt"), x.Num("Turn"), x.Num("Roll")), qb = Quaternion.Euler(y2.Num("Tilt"), y2.Num("Turn"), y2.Num("Roll"));
                if (Quaternion.Angle(qa, qb) > 0.05f)
                    diffs.Add($"turn {Quaternion.Angle(qa, qb):0.000} deg off");
                float[] sa = x.Floats("Size"), sb = y2.Floats("Size");
                if (sa != null && (sb == null || Math.Abs(sa[0] - sb[0]) > 0.001f || Math.Abs(sa[1] - sb[1]) > 0.001f || (x.Str("Type") != "Decal" && Math.Abs(sa[2] - sb[2]) > 0.001f)))
                    diffs.Add($"Size {Show(x["Size"])} vs {Show(y2["Size"])}");
                if (sa == null && Math.Abs(x.Num("Scale", 1f) - y2.Num("Scale", 1f)) > 0.001f)
                    diffs.Add($"Scale {x.Num("Scale")} vs {y2.Num("Scale")}");
                if (diffs.Count > 0)
                {
                    bad++;
                    Say($"  DIFF {x.Str("Type")} {x.Str("What")} at {px}: {string.Join("; ", diffs)}");
                }
            }
            // The box's start spot is the same box spot
            int boxA = (int)a.Num("BoxStart", -1), boxB = (int)b.Num("BoxStart", -1);
            string startA = boxA >= 0 ? V(ea[boxA].Floats("Position")).ToString("F3") : "none", startB = boxB >= 0 ? V(eb[boxB].Floats("Position")).ToString("F3") : "none";
            Say($"  box start spot {startA} -> {startB} {(startA == startB ? "SAME" : "DIFFERENT")}");
            Say($"  ROUND TRIP: {bad} entries differ of {ea.Count}");
        }

        private static void CompareExtras(string fromDir, string toDir, string id)
        {
            foreach (string suffix in new[] { "_loading.wav", "_ambience.wav", "_eesong.wav", "_banner.png", "_lore.jpg", "_tips.txt", "_navlinks.txt", "_gameover_shots.txt" })
            {
                string a = Path.Combine(fromDir, id + suffix), b = Path.Combine(toDir, id + suffix);
                if (!File.Exists(a))
                {
                    Say($"  extra {suffix}: not in the original");
                    continue;
                }
                if (!File.Exists(b))
                {
                    Say($"  extra {suffix}: MISSING from the export");
                    continue;
                }
                if (suffix.EndsWith(".txt"))
                {
                    var la = Lines(a);
                    var lb = Lines(b);
                    Say($"  extra {suffix}: {la.Count} lines -> {lb.Count} lines, {(Same(la, lb) ? "same values" : "VALUES DIFFER")}");
                }
                else
                    Say($"  extra {suffix}: {(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)) ? "identical" : "BYTES DIFFER")}");
            }
        }

        private static List<string> Lines(string f) => File.ReadAllLines(f).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();

        private static bool Same(List<string> a, List<string> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                string[] x = a[i].Split(' '), y = b[i].Split(' ');
                if (x.Length != y.Length)
                    return false;
                for (int k = 0; k < x.Length; k++)
                {
                    bool nx = float.TryParse(x[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fx);
                    bool ny = float.TryParse(y[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fy);
                    if (nx && ny ? Math.Abs(fx - fy) > 0.011f : x[k] != y[k])
                        return false;
                }
            }
            return true;
        }

        private static Vector3 V(float[] f) => f != null && f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : Vector3.zero;

        private static string Show(object o) => o == null ? "null" : o is List<object> l ? "[" + string.Join(",", l) + "]" : o.ToString();

        private static void Shots(List<ZombiesMarker> markers)
        {
            var camGo = new GameObject("SelfTest Camera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.38f, 0.42f);
            cam.fieldOfView = 50f;
            cam.farClipPlane = 1000f;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            Vector3 mid = markers.Aggregate(Vector3.zero, (acc, m) => acc + m.transform.position) / Mathf.Max(1, markers.Count);
            Snap(cam, rt, mid + new Vector3(0f, 70f, -70f), mid, "overview");
            foreach (ZombiesKind k in new[] { ZombiesKind.PerkMachine, ZombiesKind.BoxSpot, ZombiesKind.PackAPunch, ZombiesKind.Window, ZombiesKind.Door, ZombiesKind.Debris, ZombiesKind.WallBuy, ZombiesKind.Rock, ZombiesKind.ZombieSpawn, ZombiesKind.ExfilActivator })
            {
                ZombiesMarker m = markers.FirstOrDefault(x => x.Kind == k && (k != ZombiesKind.WallBuy || x.What != KitInfo.GrenadesWallId));
                if (m == null)
                    continue;
                float reach = m.HasPreviewBox ? Mathf.Max(2.5f, m.PreviewBox.size.magnitude * 1.6f) : 3f;
                Vector3 at = m.transform.position + Vector3.up * (m.HasPreviewBox ? m.PreviewBox.center.y : 1f);
                Vector3 fwd = m.transform.forward;
                fwd.y = 0f;
                Snap(cam, rt, at + fwd.normalized * reach + Vector3.up * reach * 0.35f + m.transform.right * reach * 0.3f, at, k + "_" + m.What);
            }
            // All the perk machines side by side
            var perks = markers.Where(m => m.Kind == ZombiesKind.PerkMachine).ToList();
            float x0 = 0f;
            foreach (ZombiesMarker p in perks)
            {
                p.transform.SetPositionAndRotation(new Vector3(500f + x0, 0f, 500f), Quaternion.Euler(0f, 180f, 0f));
                x0 += 1.3f;
            }
            KitPreviews.RefreshNow();
            Snap(cam, rt, new Vector3(500f + x0 * 0.5f - 0.65f, 1.3f, 494f), new Vector3(500f + x0 * 0.5f - 0.65f, 1.0f, 500f), "perks_row");
            Object.DestroyImmediate(camGo);
            rt.Release();
        }

        private static void Snap(Camera cam, RenderTexture rt, Vector3 from, Vector3 to, string name)
        {
            cam.transform.position = from;
            cam.transform.LookAt(to);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(Out, "shot_" + KitExport.Clean(name) + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static void Thumbs()
        {
            foreach (KitPlace.Item item in KitPlace.Items().Where(i => i.Label == "Jugger-Nog" || i.Label == "Pack-a-Punch" || i.Label == "Mystery box spot"
                                                                       || i.Label.StartsWith("Barricade") || i.Tab == "Doors" || i.Label == "M1A").Take(8))
            {
                Texture2D t = KitPlace.Thumb(item);
                Say($"THUMB {item.Label}: {(t != null ? t.width + "x" + t.height : "none")}");
                if (t != null)
                    File.WriteAllBytes(Path.Combine(Out, "thumb_" + KitExport.Clean(item.Label) + ".png"), t.EncodeToPNG());
            }
        }
    }
}
