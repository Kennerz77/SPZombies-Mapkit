using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// "Check map": everything that would stop the map working in SPZombies, or make it play badly, with a Fix button where
    /// the kit can fix it. Errors break the map; warnings are worth a look; notes are for information.
    /// </summary>
    internal static class KitCheck
    {
        internal enum Level { Error, Warning, Note, Good }

        internal class Item
        {
            public Level Level;
            public string Text;
            public Object Target;
            public string FixLabel;
            public Action Fix;
        }

        public static readonly List<Item> Results = new List<Item>();

        /// <summary>The window check of each barricade (shown beside it in the Scene view).</summary>
        public static readonly Dictionary<ZombiesMarker, (string text, Color colour)> WindowNotes = new Dictionary<ZombiesMarker, (string, Color)>();

        public static DateTime RanAt;

        private static void Add(Level level, string text, Object target = null, string fixLabel = null, Action fix = null) =>
            Results.Add(new Item { Level = level, Text = text, Target = target, FixLabel = fixLabel, Fix = fix });

        // The game's vaulting rules (the server's VaultingSettings): vault OVER 0.25-1.10 m high and at most 0.41 m deep, climb ONTO up to 1.31 m
        private const float VaultMin = 0.25f, VaultMax = 1.10f, VaultMaxDepth = 0.41f, ClimbMax = 1.31f, SweetLow = 0.75f, SweetHigh = 1.0f;

        private class Zone
        {
            public int Id;
            public Func<Vector3, float, bool> Contains;
        }

        public static List<Item> Run()
        {
            Results.Clear();
            WindowNotes.Clear();
            RanAt = DateTime.Now;
            try
            {
                RunChecks();
            }
            catch (Exception e)
            {
                Add(Level.Error, "The check stopped on an error: " + e.Message);
                Debug.LogException(e);
            }
            Results.Sort((a, b) => a.Level.CompareTo(b.Level));
            return Results;
        }

        private static void RunChecks()
        {
            // ---------- Setup ----------
            if (KitInstall.CoreInstalled)
                Add(Level.Good, $"SPZombies {KitInstall.CoreVersion} found in {KitInstall.SptRoot}.");
            else
                Add(Level.Warning, "SPZombies isn't found in the SPT folder (Setup tab): the previews are only boxes and the gun list is empty.");

            List<ZombiesMapSettings> all = KitExport.AllSettings();
            if (all.Count == 0)
            {
                Add(Level.Error, "No map settings: open the Map tab and press Add map settings.");
                return;
            }
            if (all.Count > 1)
                Add(Level.Error, "More than one Zombies Map Settings in the open scenes: keep one.", all[1]);
            ZombiesMapSettings s = all[0];
            if (string.IsNullOrEmpty(KitExport.Clean(s.MapId)))
                Add(Level.Error, "The map id is empty (Map tab).", s);
            KitSdk.MapDef def = KitSdk.For(s.MapId);
            if (KitSdk.Present && def == null)
                Add(Level.Warning, "No SDK Map Definition found for this map id (make the map with the SDK first, or set the map id to its Map Id).", s);
            else if (def != null && def.MapId != s.MapId)
                Add(Level.Warning, $"The map id ({s.MapId}) isn't the Map Definition's ({def.MapId}): SPZombies matches the layout to the map by it.", s,
                    "Use " + def.MapId, () => { Undo.RecordObject(s, "Map id"); s.MapId = def.MapId; });
            if (KitInstall.CoreInstalled && KitInstall.CoreTooOld)
                Add(Level.Warning, $"SPZombies {KitInstall.CoreVersion} is installed: maps made with this kit need {KitInfo.CoreNeeded} or newer " +
                                   $"(it finds maps in BepInEx\\plugins\\{KitExport.MapsFolder}, and reads the starting gun and perk prices). Update SPZombies.");
            if (!string.IsNullOrEmpty(s.StartGun) && s.StartGun != KitInfo.DefaultStartGun && KitInstall.Guns().Count > 1 && KitInstall.FindGun(s.StartGun) == null)
                Add(Level.Error, $"The starting gun '{s.StartGun}' isn't in SPZombies' gun list: players would get the M1911. Pick it again in the Map tab.", s);
            if (s.CustomPerkPrices && s.PerkPrices.Any(p => p != null && p.Price <= 0))
                Add(Level.Warning, "A perk price is 0: that perk uses its normal price (Map tab, Custom perk prices).", s);
            foreach (string wrong in KitTips.WrongPrices(s))
                Add(Level.Warning, "Loading tip with the wrong price: " + wrong, s);
            if (!string.IsNullOrEmpty(s.MapId) && !s.MapId.StartsWith("zm_"))
                Add(Level.Note, "Map ids starting with zm_ are recognised as zombies maps by the server even before the layout is read.", s);
            if (s.MapSelectSpot == "bigmap")
                Add(Level.Note, "Stronghold also shows at the bigmap spot on the map screen: with both installed their icons overlap. Pick another spot (Map tab).", s);

            List<ZombiesMarker> markers = KitExport.InOrder<ZombiesMarker>();
            if (markers.Count == 0)
            {
                Add(Level.Error, "No markers placed yet (Place tab).");
                return;
            }
            foreach (ZombiesMarker m in KitExport.InOrder<ZombiesMarker>(false))
            {
                if (!EditorOnly(m.transform))
                    Add(Level.Warning, $"{m.name} isn't tagged EditorOnly: it would go into the map build (a missing script there).", m, "Tag it",
                        () => { Undo.RecordObject(m.gameObject, "EditorOnly"); m.gameObject.tag = "EditorOnly"; });
                Vector3 sc = m.Size;
                if (sc.x < 0.001f || sc.y < 0.001f || sc.z < 0.001f)
                    Add(Level.Error, $"{m.name} has a zero scale.", m);
            }

            // ---------- Players ----------
            List<Component> spawns = KitSdk.PlayerSpawns();
            var starts = markers.Where(m => m.Kind == ZombiesKind.PlayerStart).ToList();
            if (spawns.Count == 0 && starts.Count == 0)
                Add(Level.Error, "No player start: add the player spawns Spawn_Player_1..4 (Setup tab) or Player start markers.");
            else if (spawns.Count > 0)
                Add(spawns.Count < 4 ? Level.Note : Level.Good, $"{spawns.Count} of 4 player spawns (Spawn_Player_N: player N starts there).", spawns[0]);
            if (KitSdk.Present)
            {
                if (KitSdk.SpawnPointCount == 0)
                    Add(Level.Error, "The map has no spawn points at all: the raid can't start. Add the player spawns (Setup tab).");
                if (!KitSdk.HasBotZone)
                    Add(Level.Error, "The map has no bot zone: the raid can't start. Adding the player spawns (Setup tab) makes one.");
                if (!KitSdk.HasAIDoorsHolder)
                    Add(Level.Warning, "No AIDoorsHolder: with the Waypoints mod installed (it often is) the raid won't start.", null, "Add one",
                        () => Debug.Log("[SPZombies Kit] " + KitSdk.AddAIDoorsHolder(KitExport.MapScene())));
            }

            // ---------- Zones ----------
            List<Zone> zones = Zones(markers);
            var spawnsZ = markers.Where(m => m.Kind == ZombiesKind.ZombieSpawn).ToList();
            var windows = markers.Where(m => m.Kind == ZombiesKind.Window).ToList();
            var openers = markers.Where(m => m.Kind == ZombiesKind.Door || m.Kind == ZombiesKind.Debris).ToList();
            if (spawnsZ.Count == 0)
                Add(Level.Error, "No zombie spawns: no zombie can come.");
            else if (spawnsZ.Count < 4)
                Add(Level.Note, $"Only {spawnsZ.Count} zombie spawns: more spread round the map keep the waves coming.");
            if (windows.Count == 0)
                Add(Level.Warning, "No barricades (windows): zombies walk straight in.");
            if (zones.Count == 0)
                Add(Level.Note, "No zones: every zombie spawn works from round 1, and doors and debris open no areas.");
            else
            {
                int[] ids = zones.Select(z => z.Id).Distinct().OrderBy(i => i).ToArray();
                Add(Level.Good, $"Zones: {string.Join(", ", ids)} (1 and 2 are open from the start).");
                if (!ids.Contains(1))
                    Add(Level.Warning, "No zone 1: players start in zone 1 (and 2), the rest opens by doors and debris.");
                foreach (ZombiesMarker sp in spawnsZ)
                {
                    int z = sp.Zone > 0 ? sp.Zone : ZoneAt(zones, sp.transform.position);
                    if (z == 0)
                        Add(Level.Error, $"{sp.name} is outside every zone: with zones, it never spawns. Put it in a zone or set its Zone.", sp);
                }
                foreach (int id in ids.Where(i => i > 2))
                {
                    bool opens = openers.Any(o => zones.Any(z => z.Id == id && z.Contains(o.transform.position, 2.5f)));
                    if (!opens)
                        Add(Level.Warning, $"Zone {id} has no door or debris touching it (within 2.5 m): it never opens, its spawns never spawn.");
                    if (!spawnsZ.Any(sp => (sp.Zone > 0 ? sp.Zone : ZoneAt(zones, sp.transform.position)) == id))
                        Add(Level.Note, $"Zone {id} has no zombie spawns.");
                }
                foreach (ZombiesMarker o in openers)
                    if (!zones.Any(z => z.Contains(o.transform.position, 2.5f)))
                        Add(Level.Note, $"{o.name} touches no zone (within 2.5 m): buying it opens no area.", o);
            }

            // ---------- Navmesh: the zombie paths (also drawn in the Scene view, Paths tab) ----------
            KitPaths.Run();
            KitPathsView.Seen();
            foreach (KitPaths.Note n in KitPaths.Notes)
                // Each barricade's own "N spawns feed it" stays in the Paths tab
                if (n.Level != Level.Good || !n.Text.Contains(" feed it."))
                    Add(n.Level, n.Text, n.Target);

            // ---------- Windows ----------
            foreach (ZombiesMarker w in windows)
            {
                string verdict = WindowVerdict(w, out Color colour, out Level level);
                WindowNotes[w] = (verdict, colour);
                if (level != Level.Good)
                    Add(level, $"{w.name}: {verdict}", w);
            }
            if (windows.Count > 0 && WindowNotes.Values.All(v => v.text.StartsWith("vaults")))
                Add(Level.Good, $"All {windows.Count} windows have sills zombies vault over.");

            // ---------- Power, machines, box ----------
            bool powerSwitch = markers.Any(m => m.Kind == ZombiesKind.PowerSwitch);
            if (!s.AlwaysPowered && !powerSwitch)
                Add(Level.Error, "No power switch and the map isn't always powered: perks and Pack-a-Punch never work. Place a power switch or tick Always powered (Map tab).", s);
            if (s.AlwaysPowered && powerSwitch)
                Add(Level.Warning, "The map is always powered, so its power switch does nothing.", markers.First(m => m.Kind == ZombiesKind.PowerSwitch));
            if (s.AlwaysPowered)
                foreach (ZombiesMarker o in openers.Where(o => o.OpensWithPower))
                    Add(Level.Warning, $"{o.name} opens with the power, and the map is always powered: it's open from the start.", o);

            var perks = markers.Where(m => m.Kind == ZombiesKind.PerkMachine).ToList();
            foreach (ZombiesMarker p in perks)
            {
                if (!KitInfo.PerkIds.Contains(p.What))
                    Add(Level.Error, $"{p.name}: '{p.What}' isn't a perk SPZombies has.", p);
                else if (KitInstall.CoreInstalled && KitModels.Prefab(KitModels.PerksBundle, "perk_" + p.What) == null)
                    Add(Level.Warning, $"{p.name}: its machine model isn't in the installed Perk Beverages.", p);
            }
            foreach (var dup in perks.GroupBy(p => p.What).Where(g => g.Count() > 1))
                Add(Level.Note, $"{dup.Count()} {KitInfo.PerkName(dup.Key)} machines.", dup.First());
            if (perks.Count == 0)
                Add(Level.Note, "No perk machines.");
            if (!markers.Any(m => m.Kind == ZombiesKind.PackAPunch))
                Add(Level.Note, "No Pack-a-Punch.");
            var boxes = markers.Where(m => m.Kind == ZombiesKind.BoxSpot).ToList();
            if (boxes.Count == 0)
                Add(Level.Warning, "No mystery box spots: no box in this map.");
            if (boxes.Count(b => b.BoxStartsHere) > 1)
                Add(Level.Error, "More than one box spot is ticked 'Box starts here': tick one.", boxes.First(b => b.BoxStartsHere));

            foreach (ZombiesMarker wb in markers.Where(m => m.Kind == ZombiesKind.WallBuy))
            {
                if (KitInstall.CoreInstalled && KitInstall.FindGun(wb.What) == null)
                    Add(Level.Error, $"{wb.name}: '{wb.What}' isn't a gun SPZombies has.", wb);
                if (wb.Price <= 0)
                    Add(Level.Warning, $"{wb.name} is free (price 0).", wb);
                if (KitInstall.CoreInstalled && KitInstall.ChalkBroken(wb.What))
                    Add(Level.Note, $"{wb.name}: SPZombies' saved chalk picture of the {KitInstall.GunName(wb.What)} is broken, so the kit shows a plain board. In a match the chalk is drawn from the gun itself.", wb);
            }
            foreach (ZombiesMarker o in openers)
            {
                if (o.CustomLook == null)
                {
                    bool door = o.Kind == ZombiesKind.Door;
                    if (!KitModels.IsLook(door ? "zm_door_" : "zm_debris_", o.What, door ? KitInfo.AllDoorLooks : KitInfo.AllDebrisLooks))
                        Add(Level.Warning, $"{o.name}: '{o.What}' isn't one of SPZombies' looks (in a match it copies a map object of that name, if there is one).", o);
                }
                else if (KitTurnInto.OriginalStillBuilt(o))
                    Add(Level.Warning, $"{o.name}: its look's original ({o.CustomLook.name}) is still in the map build: in a match it stays standing after it's bought.", o,
                        "Take it out", () => KitTurnInto.TakeOut(o));
                else if (o.CustomLook.GetComponentInChildren<ZombiesMarker>(true) != null)
                    Add(Level.Error, $"{o.name}: its custom look holds a marker. Pick a plain object of the map.", o);
                if (o.Price <= 0 && !o.OpensWithPower)
                    Add(Level.Warning, $"{o.name} is free (price 0) and doesn't open with the power.", o);
            }
            foreach (ZombiesMarker l in markers.Where(m => m.Kind == ZombiesKind.PowerLight || m.Kind == ZombiesKind.Lamp))
                if (l.CustomLook == null && string.IsNullOrEmpty(l.What))
                    Add(Level.Warning, $"{l.name} has no fixture (Custom look): SPZombies shows a small box there.", l);

            // ---------- Exfil ----------
            bool lever = markers.Any(m => m.Kind == ZombiesKind.ExfilActivator);
            bool hold = markers.Any(m => m.Kind == ZombiesKind.ExfilHold);
            bool exit = markers.Any(m => m.Kind == ZombiesKind.ExfilZone);
            if (lever && !hold)
                Add(Level.Warning, "The exfil lever has no hold area: place an Exfil hold area for the team to defend.");
            if (lever && !exit)
                Add(Level.Warning, "The exfil lever has no exit zone: players leave from close to the lever. Place an Exfil exit zone.");
            if (!lever && (hold || exit))
                Add(Level.Warning, "Exfil areas but no exfil lever: the exfil never starts.");
            if (!lever)
                Add(Level.Note, "No exfil: the match ends when everyone is down.");

            // ---------- Standing on the ground ----------
            foreach (ZombiesMarker m in markers.Where(m => OnFloor(m.Kind)))
            {
                Vector3 p = m.transform.position;
                if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
                {
                    float gap = p.y - hit.point.y;
                    if (gap > 0.35f)
                        Add(Level.Warning, $"{m.name} floats {gap:0.00} m over the ground.", m, "Drop it", () => KitPlace.DropToGround(m.transform));
                }
            }

            // ---------- Game over shots ----------
            List<ZombiesGameOverShot> shots = KitExport.InOrder<ZombiesGameOverShot>();
            if (shots.Count == 0)
                Add(Level.Note, "No game over shots: SPZombies makes its own (wide arcs round the start, slow passes by the machines). Place some to pick the views yourself.");
            foreach (ZombiesGameOverShot shot in shots)
                foreach (Vector3 p in new[] { shot.From, shot.transform.position, shot.To })
                    if (Physics.CheckSphere(p, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        Add(Level.Warning, $"{shot.name}: the camera passes through something during its drift (the red line). Move it into open air.", shot);
                        break;
                    }

            // ---------- Files ----------
            CheckWav(s.LoadingMusic, "Loading music");
            CheckWav(s.Ambience, "Ambience");
            CheckWav(s.EasterEggSong, "Easter egg song");
            if (markers.Any(m => m.Kind == ZombiesKind.Rock && m.What != "lore") && s.EasterEggSong == null)
                Add(Level.Note, "Easter egg rocks but no easter egg song (Map tab): SPZombies plays its own.", s);
            string server = KitSdk.ServerSettingsFile(s.MapId);
            if (server == null)
                Add(Level.Note, "The map's server settings (base.json) weren't found in the SPT folder: make them with the SDK's Edit Server Data, then check again.");
            else
            {
                List<string> problems = KitSdk.ServerProblems(server);
                if (problems.Count == 0)
                    Add(Level.Good, "Server settings are zombies-ready.");
                else
                    Add(Level.Warning, "Server settings aren't zombies-ready: " + string.Join(", ", problems) + ".", null, "Fix them",
                        () => Debug.Log("[SPZombies Kit] " + KitSdk.FixServer(server)));
            }
            if (def != null)
            {
                Transform looks = KitExport.MapScene().GetRootGameObjects().FirstOrDefault(g => g.name == KitExport.LooksRootName)?.transform;
                if (looks != null && def.ScenePaths.Count > 0 && !def.ScenePaths.Contains(looks.gameObject.scene.path))
                    Add(Level.Error, $"The custom looks are in {looks.gameObject.scene.name}, which isn't one of the Map Definition's scenes: they won't be in the map.", looks.gameObject);
            }
        }

        private static bool EditorOnly(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.gameObject.CompareTag("EditorOnly"))
                    return true;
            return false;
        }

        private static bool OnFloor(ZombiesKind k) =>
            k == ZombiesKind.PlayerStart || k == ZombiesKind.ZombieSpawn || k == ZombiesKind.BoxSpot || k == ZombiesKind.PerkMachine
            || k == ZombiesKind.PackAPunch || k == ZombiesKind.PowerSwitch || k == ZombiesKind.ExfilActivator || k == ZombiesKind.Debris;

        private static void CheckWav(AudioClip clip, string what)
        {
            if (clip == null)
                return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                Add(Level.Warning, $"{what} ({clip.name}) isn't a .wav file: SPZombies only plays 16-bit PCM .wav.", clip);
                return;
            }
            string problem = KitExport.WavProblem(path);
            if (problem != null)
                Add(Level.Warning, $"{what} ({clip.name}) {problem}", clip);
        }

        // ---------- Zones (as SPZombies works them out) ----------

        private static List<Zone> Zones(List<ZombiesMarker> markers)
        {
            var list = new List<Zone>();
            foreach (ZombiesZoneOutline o in KitExport.InOrder<ZombiesZoneOutline>())
            {
                List<Vector3> pts = o.WorldCorners();
                if (pts.Count < 3)
                    continue;
                float floor = pts.Min(p => p.y) - o.Below, top = pts.Max(p => p.y) + o.Height;
                list.Add(new Zone { Id = Mathf.Max(1, o.Id), Contains = (q, margin) => InOutline(pts, floor, top, q, margin) });
            }
            foreach (ZombiesMarker m in markers.Where(m => m.Kind == ZombiesKind.Zone))
            {
                Vector3 half = m.Size * 0.5f;
                Quaternion rot = m.transform.rotation;
                Vector3 centre = m.transform.position + rot * Vector3.up * half.y;
                list.Add(new Zone
                {
                    Id = Mathf.Max(1, m.Zone),
                    Contains = (p, margin) =>
                    {
                        Vector3 local = Quaternion.Inverse(rot) * (p - centre);
                        return Mathf.Abs(local.x) <= half.x + margin && Mathf.Abs(local.y) <= half.y + margin && Mathf.Abs(local.z) <= half.z + margin;
                    },
                });
            }
            return list;
        }

        private static int ZoneAt(List<Zone> zones, Vector3 p) => zones.FirstOrDefault(z => z.Contains(p, 0f))?.Id ?? 0;

        private static bool InOutline(List<Vector3> pts, float floor, float top, Vector3 q, float margin)
        {
            if (q.y < floor - margin || q.y > top + margin)
                return false;
            bool inside = false;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                float xi = pts[i].x, zi = pts[i].z, xj = pts[j].x, zj = pts[j].z;
                if ((zi > q.z) != (zj > q.z) && q.x < (xj - xi) * (q.z - zi) / (zj - zi) + xi)
                    inside = !inside;
            }
            if (inside || margin <= 0f)
                return inside;
            var p2 = new Vector2(q.x, q.z);
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                var a = new Vector2(pts[j].x, pts[j].z);
                var b = new Vector2(pts[i].x, pts[i].z);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p2 - a, ab) / ab.sqrMagnitude);
                if (Vector2.Distance(p2, a + ab * t) <= margin)
                    return true;
            }
            return false;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        // ---------- Window sills ----------

        /// <summary>How a zombie gets in at this window, from the sill height and wall depth under it.</summary>
        public static string WindowVerdict(ZombiesMarker w, out Color colour, out Level level)
        {
            Vector3 fwd = Flat(w.transform.forward), basePos = w.transform.position;
            float ground = float.NaN, depth = float.NaN;
            if (Physics.Raycast(basePos + fwd * 0.8f + Vector3.up * 1.5f, Vector3.down, out RaycastHit g, 6f, ~0, QueryTriggerInteraction.Ignore))
                ground = g.point.y;
            if (float.IsNaN(ground))
            {
                colour = Color.grey;
                level = Level.Note;
                return "no ground found outside (no colliders there?)";
            }
            float y = Mathf.Lerp(ground, basePos.y, 0.5f);
            Vector3 outside = new Vector3(basePos.x, y, basePos.z) + fwd * 1.5f;
            Vector3 inside = new Vector3(basePos.x, y, basePos.z) - fwd * 1.5f;
            if (basePos.y - ground > 0.1f
                && Physics.Raycast(outside, -fwd, out RaycastHit a, 3f, ~0, QueryTriggerInteraction.Ignore)
                && Physics.Raycast(inside, fwd, out RaycastHit b, 3f, ~0, QueryTriggerInteraction.Ignore))
                depth = Vector3.Dot(a.point - b.point, fwd);
            float sill = basePos.y - ground;
            bool deep = !float.IsNaN(depth) && depth > VaultMaxDepth;
            string measured = $"sill {sill:0.00} m{(float.IsNaN(depth) ? "" : $", wall {depth:0.00} m deep")}";
            if (sill >= VaultMin && sill <= VaultMax && !deep)
            {
                bool sweet = sill >= SweetLow && sill <= SweetHigh;
                colour = sweet ? new Color(0.2f, 1f, 0.3f) : new Color(0.6f, 0.9f, 0.3f);
                level = Level.Good;
                return $"vaults over ({measured}{(sweet ? ", the best height" : "")})";
            }
            level = Level.Note;
            if (sill >= VaultMin && sill <= ClimbMax)
            {
                colour = new Color(1f, 0.6f, 0.1f);
                return $"climbs onto the sill, then steps down ({measured}): for a clean vault keep the sill 0.75-1.0 m high and the wall at most {VaultMaxDepth:0.00} m deep";
            }
            colour = new Color(1f, 0.2f, 0.2f);
            return sill < VaultMin
                ? $"too low to vault ({measured}): a scripted climb is used. Raise the sill to 0.75-1.0 m."
                : $"too high to vault ({measured}): a scripted climb is used. Lower the sill to 0.75-1.0 m.";
        }

        public static void MarkDirty() => EditorSceneManager.MarkSceneDirty(KitExport.MapScene());
    }
}
