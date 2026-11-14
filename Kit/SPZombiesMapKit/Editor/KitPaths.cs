using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Works out how zombies will move on the baked navmesh, the way SPZombies decides it in a match:
    /// - each zombie spawn's barricade (the nearest within 35 m whose outside faces the spawn, at about the spawn's height
    ///   or one it can walk up to) and the route there; a spawn with none is "inside" and hunts the players at once;
    /// - each barricade's outside spot (where zombies tear the boards) and inside landing, and whether the inside leads to
    ///   the players;
    /// - the walk areas with every door, debris pile and barricade shut (as at the start of a match): the zombie side, the
    ///   player side, and any leak (a way from the zombie side to the players or a window's inside without a barricade).
    /// The results are drawn in the Scene view (KitPathsView) and listed as sentences in the kit's Paths tab and the check.
    /// </summary>
    internal static class KitPaths
    {
        // SPZombies' own numbers (ZombiesRounds)
        public const float FeedReach = 35f, FeedHeight = 2.5f;
        private const float Agent = 0.35f;

        internal enum Route { Walks, Partial, None, OffMesh, Inside }

        internal class SpawnRoute
        {
            public ZombiesMarker Spawn, Window;
            public Route Status;
            public Vector3[] Corners = new Vector3[0];
            public Vector3 From, To, StopAt;
            public float Length, Short;
            public bool Stuck;      // an inside spawn whose route to the players stops short
        }

        internal class WindowInfo
        {
            public ZombiesMarker Window;
            public Vector3 Outside, Inside;
            public bool OutsideOnMesh, InsideOnMesh, InsideReaches;
            public int Feeds;
            public Vector3[] InsideRoute = new Vector3[0];
            public string Verdict;
            public Color VerdictColour;
        }

        internal class Leak
        {
            public Vector3[] Route;
            public string Text;
            public Object Target;
        }

        internal class Note
        {
            public KitCheck.Level Level;
            public string Text;
            public Object Target;
            public Vector3? Where;
        }

        public static readonly List<SpawnRoute> Routes = new List<SpawnRoute>();
        public static readonly List<WindowInfo> Windows = new List<WindowInfo>();
        public static readonly List<Leak> Leaks = new List<Leak>();
        public static readonly List<Note> Notes = new List<Note>();
        public static readonly List<Vector3> PlayerSpots = new List<Vector3>();
        public static Mesh Areas;
        public static bool HasNavMesh, Ran;
        public static DateTime RanAt;
        public static int Triangles;
        public static float ZombieArea, PlayerArea, BlockedArea, LoneArea;

        /// <summary>For the log: how many navmesh pieces, seeds found, links.</summary>
        public static string Detail = "";

        private static readonly NavMeshPath Scratch = new NavMeshPath();

        private static void Say(KitCheck.Level level, string text, Object target = null, Vector3? where = null) =>
            Notes.Add(new Note { Level = level, Text = text, Target = target, Where = where });

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        // ---------- Running ----------

        public static void Run()
        {
            Routes.Clear();
            Windows.Clear();
            Leaks.Clear();
            Notes.Clear();
            PlayerSpots.Clear();
            if (Areas != null)
                Object.DestroyImmediate(Areas);
            Areas = null;
            Ran = true;
            RanAt = DateTime.Now;
            try
            {
                Work();
            }
            catch (Exception e)
            {
                Say(KitCheck.Level.Error, "Working out the paths stopped on an error: " + e.Message);
                Debug.LogException(e);
            }
            Notes.Sort((a, b) => a.Level.CompareTo(b.Level));
            SceneView.RepaintAll();
        }

        private static void Work()
        {
            List<ZombiesMarker> markers = KitExport.InOrder<ZombiesMarker>();
            var spawns = markers.Where(m => m.Kind == ZombiesKind.ZombieSpawn).ToList();
            var windows = markers.Where(m => m.Kind == ZombiesKind.Window).ToList();
            var shut = markers.Where(m => m.Kind == ZombiesKind.Door || m.Kind == ZombiesKind.Debris || m.Kind == ZombiesKind.Window).ToList();
            PlayerSpots.AddRange(KitSdk.PlayerSpawns().Select(c => c.transform.position));
            PlayerSpots.AddRange(markers.Where(m => m.Kind == ZombiesKind.PlayerStart).Select(m => m.transform.position));

            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            HasNavMesh = tri.vertices != null && tri.vertices.Length > 0;
            Triangles = HasNavMesh ? tri.indices.Length / 3 : 0;
            if (!HasNavMesh)
            {
                Say(KitCheck.Level.Error, "No navmesh baked in the open scenes: zombies can't walk. Bake it with the SDK, then work out the paths again.");
                return;
            }
            if (spawns.Count == 0)
                Say(KitCheck.Level.Error, "No zombie spawns placed yet (Place tab > Spawns).");
            if (PlayerSpots.Count == 0)
                Say(KitCheck.Level.Warning, "No player spawns yet (Setup tab): the routes inside to the players can't be checked.");

            // ---------- Barricades ----------
            foreach (ZombiesMarker w in windows)
            {
                var info = new WindowInfo { Window = w };
                info.OutsideOnMesh = OutsideSpot(w, out info.Outside);
                Vector3 fwd = Flat(w.transform.forward);
                info.InsideOnMesh = NavMesh.SamplePosition(w.transform.position - fwd * 1.5f, out NavMeshHit inHit, 2f, NavMesh.AllAreas);
                info.Inside = info.InsideOnMesh ? inHit.position : w.transform.position - fwd * 1.5f;
                info.Verdict = KitCheck.WindowVerdict(w, out info.VerdictColour, out _);
                KitCheck.WindowNotes[w] = (info.Verdict, info.VerdictColour);
                if (info.InsideOnMesh && PlayerSpots.Count > 0)
                {
                    Vector3 nearest = PlayerSpots.OrderBy(p => (p - info.Inside).sqrMagnitude).First();
                    if (Path(info.Inside, nearest, out Vector3[] corners, out NavMeshPathStatus status, 3f))
                    {
                        info.InsideRoute = corners;
                        info.InsideReaches = status == NavMeshPathStatus.PathComplete;
                    }
                }
                Windows.Add(info);
            }

            // ---------- Spawns ----------
            foreach (ZombiesMarker s in spawns)
            {
                var r = new SpawnRoute { Spawn = s, From = s.transform.position };
                r.Window = FeedFor(s.transform.position, windows);
                if (!NavMesh.SamplePosition(s.transform.position, out NavMeshHit at, 3f, NavMesh.AllAreas))
                {
                    r.Status = Route.OffMesh;
                    Routes.Add(r);
                    continue;
                }
                r.From = at.position;
                if (r.Window == null)
                {
                    // Spawned inside: hunts the players at once
                    r.Status = Route.Inside;
                    if (PlayerSpots.Count > 0)
                    {
                        Vector3 nearest = PlayerSpots.OrderBy(p => (p - r.From).sqrMagnitude).First();
                        r.To = nearest;
                        if (Path(r.From, nearest, out r.Corners, out NavMeshPathStatus ist, 3f))
                        {
                            r.Length = Length(r.Corners);
                            if (ist != NavMeshPathStatus.PathComplete)
                            {
                                r.Stuck = true;
                                r.StopAt = r.Corners[r.Corners.Length - 1];
                                r.Short = Vector3.Distance(r.StopAt, nearest);
                            }
                        }
                        else
                            r.Stuck = true;
                    }
                    Routes.Add(r);
                    continue;
                }
                WindowInfo wi = Windows.First(x => x.Window == r.Window);
                wi.Feeds++;
                r.To = wi.Outside;
                if (!Path(r.From, wi.Outside, out r.Corners, out NavMeshPathStatus st, 2f))
                {
                    r.Status = Route.None;
                    r.Corners = new[] { r.From, wi.Outside };
                }
                else if (st == NavMeshPathStatus.PathComplete)
                    r.Status = Route.Walks;
                else
                {
                    r.Status = Route.Partial;
                    r.StopAt = r.Corners.Length > 0 ? r.Corners[r.Corners.Length - 1] : r.From;
                    r.Short = Vector3.Distance(r.StopAt, wi.Outside);
                }
                r.Length = Length(r.Corners);
                Routes.Add(r);
            }

            // ---------- Areas and leaks ----------
            Areas = WalkAreas(tri, shut, spawns.Where(s => Routes.Any(r => r.Spawn == s && r.Status != Route.Inside && r.Status != Route.OffMesh)).Select(s => s.transform.position).ToList());

            Summarise(spawns, windows);
        }

        private static float Length(Vector3[] c)
        {
            float l = 0f;
            for (int i = 1; i < c.Length; i++)
                l += Vector3.Distance(c[i - 1], c[i]);
            return l;
        }

        private static bool Path(Vector3 from, Vector3 to, out Vector3[] corners, out NavMeshPathStatus status, float toReach)
        {
            corners = new Vector3[0];
            status = NavMeshPathStatus.PathInvalid;
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out NavMeshHit b, toReach, NavMesh.AllAreas))
                return false;
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, Scratch) || Scratch.status == NavMeshPathStatus.PathInvalid)
                return false;
            corners = Scratch.corners;
            status = Scratch.status;
            return corners.Length > 0;
        }

        /// <summary>The barricade a zombie from this spawn goes in by (SPZombies' rule), or null: it spawned inside.</summary>
        public static ZombiesMarker FeedFor(Vector3 spawn, List<ZombiesMarker> windows) =>
            windows.Where(w => Vector3.Dot(spawn - w.transform.position, w.transform.forward) > 0f
                               && (Mathf.Abs(spawn.y - w.transform.position.y) <= FeedHeight || Reaches(spawn, w.transform.position + w.transform.forward * 1.2f)))
                .OrderBy(w => Vector3.Distance(w.transform.position, spawn))
                .FirstOrDefault(w => Vector3.Distance(w.transform.position, spawn) <= FeedReach);

        private static bool Reaches(Vector3 from, Vector3 to) =>
            Path(from, to, out _, out NavMeshPathStatus s, 2f) && s == NavMeshPathStatus.PathComplete;

        // Where zombies stand to tear the boards: on the navmesh just outside, below the sill (SPZombies' OutsideGround)
        private static bool OutsideSpot(ZombiesMarker w, out Vector3 spot)
        {
            Vector3 fwd = Flat(w.transform.forward), p0 = w.transform.position;
            float sill = p0.y;
            foreach (bool low in new[] { true, false })
                foreach (float d in new[] { 0.8f, 1.1f, 1.4f, 1.8f, 2.2f })
                {
                    Vector3 p = p0 + fwd * d;
                    if (!NavMesh.SamplePosition(p - Vector3.up, out NavMeshHit hit, 1.6f, NavMesh.AllAreas))
                        continue;
                    Vector3 flat = hit.position - p;
                    flat.y = 0f;
                    if (flat.magnitude > 0.7f || hit.position.y > sill + 0.3f || (low && hit.position.y > sill - 0.4f))
                        continue;
                    spot = hit.position;
                    return true;
                }
            spot = p0 + fwd * 0.8f;
            return false;
        }

        // ---------- Text ----------

        private static void Summarise(List<ZombiesMarker> spawns, List<ZombiesMarker> windows)
        {
            int walks = Routes.Count(r => r.Status == Route.Walks), inside = Routes.Count(r => r.Status == Route.Inside);
            int bad = Routes.Count(r => r.Status == Route.Partial || r.Status == Route.None || r.Status == Route.OffMesh);
            if (spawns.Count > 0)
                Say(bad == 0 ? KitCheck.Level.Good : KitCheck.Level.Note,
                    $"{spawns.Count} zombie spawns: {walks} walk to their barricade, {bad} can't, {inside} start inside (no barricade within {FeedReach:0} m facing them: they hunt the players at once).");

            foreach (SpawnRoute r in Routes)
            {
                string w = r.Window != null ? r.Window.name : "";
                switch (r.Status)
                {
                    case Route.OffMesh:
                        Say(KitCheck.Level.Error, $"{r.Spawn.name} isn't on the navmesh (nothing within 3 m): zombies can't come up there. Move it onto walkable ground.", r.Spawn, r.From);
                        break;
                    case Route.None:
                        Say(KitCheck.Level.Error, $"{r.Spawn.name} has no route at all to {w}, the barricade it feeds (red line). They're on separate pieces of navmesh: join them, or move the spawn nearer a barricade it can reach.", r.Spawn, r.From);
                        break;
                    case Route.Partial:
                        Say(KitCheck.Level.Warning, $"{r.Spawn.name}'s route to {w} stops {r.Short:0.0} m short (orange line, X where it stops): a gap in the navmesh. Close the gap and bake again, or add a Nav link across it.", r.Spawn, r.StopAt);
                        break;
                    case Route.Inside when PlayerSpots.Count > 0 && r.Stuck:
                        Say(KitCheck.Level.Warning, $"{r.Spawn.name} starts inside (no barricade faces it within {FeedReach:0} m) and can't walk to the players" +
                                                    (r.Corners.Length > 0 ? $": its route stops {r.Short:0.0} m short (blue line, X)." : ".") +
                                                    " If it's meant to be outside, put a barricade facing it within 35 m.", r.Spawn, r.Corners.Length > 0 ? r.StopAt : r.From);
                        break;
                }
            }
            foreach (WindowInfo w in Windows)
            {
                if (w.Feeds == 0)
                    Say(KitCheck.Level.Warning, $"{w.Window.name} gets no zombies: no zombie spawn within {FeedReach:0} m is in front of it (its red arrow points inside; spawns go on the other side).", w.Window, w.Window.transform.position);
                else
                    Say(KitCheck.Level.Good, $"{w.Window.name}: {w.Feeds} spawn{(w.Feeds == 1 ? "" : "s")} feed it.", w.Window, w.Window.transform.position);
                if (!w.OutsideOnMesh)
                    Say(KitCheck.Level.Error, $"{w.Window.name}: no navmesh just outside it, below the sill (red dot): zombies can't stand there to tear the boards. Check its arrow points into the building and the ground outside is baked.", w.Window, w.Outside);
                if (!w.InsideOnMesh)
                    Say(KitCheck.Level.Warning, $"{w.Window.name}: no navmesh just inside it: zombies climbing in land off the navmesh.", w.Window, w.Inside);
                else if (PlayerSpots.Count > 0 && !w.InsideReaches)
                    Say(KitCheck.Level.Warning, $"{w.Window.name}: from just inside it there's no route to the players (with doors open). Zombies that climb in get stuck.", w.Window, w.Inside);
            }
            foreach (Leak l in Leaks)
                Say(KitCheck.Level.Warning, l.Text, l.Target, l.Route.Length > 0 ? l.Route[l.Route.Length / 2] : (Vector3?)null);
            if (Areas != null && Leaks.Count == 0 && PlayerSpots.Count > 0 && Routes.Any(r => r.Status != Route.Inside))
                Say(KitCheck.Level.Good, "No leaks: with every door, debris pile and barricade shut, zombies outside can't walk in to the players.");
            if (Areas != null)
                Say(KitCheck.Level.Note, $"Walk areas: zombie side {ZombieArea:0} m², player side {PlayerArea:0} m², shut by barricades/doors/debris {BlockedArea:0} m², reached by nobody {LoneArea:0} m².");
        }

        // ---------- Walk areas (the navmesh as a graph) ----------

        private class Graph
        {
            public Vector3[] V;          // welded vertices
            public int[] T;              // 3 per triangle
            public List<int>[] Next;     // neighbours
            public bool[] Shut;

            public Vector3 Centre(int t) => (V[T[t * 3]] + V[T[t * 3 + 1]] + V[T[t * 3 + 2]]) / 3f;

            public float Area(int t) => Vector3.Cross(V[T[t * 3 + 1]] - V[T[t * 3]], V[T[t * 3 + 2]] - V[T[t * 3]]).magnitude * 0.5f;
        }

        private static Mesh WalkAreas(NavMeshTriangulation tri, List<ZombiesMarker> shut, List<Vector3> zombieSeeds)
        {
            Graph g = Build(tri);
            int n = g.T.Length / 3;

            // Shut: every triangle a door, debris pile or barricade (with a zombie's width round it) covers
            var boxes = shut.Select(m => (m, centre: m.transform.position + m.transform.up * (m.Size.y * 0.5f), rot: m.transform.rotation,
                half: m.Size * 0.5f + new Vector3(Agent, 0.3f, Agent))).ToList();
            g.Shut = new bool[n];
            for (int t = 0; t < n; t++)
            {
                Vector3 a = g.V[g.T[t * 3]], b = g.V[g.T[t * 3 + 1]], c = g.V[g.T[t * 3 + 2]];
                foreach (var box in boxes)
                {
                    if ((box.centre - a).sqrMagnitude > (box.half.sqrMagnitude + (b - a).sqrMagnitude + (c - a).sqrMagnitude) * 4f)
                        continue;
                    if (TriInBox(a, b, c, box.centre, box.rot, box.half))
                    {
                        g.Shut[t] = true;
                        break;
                    }
                }
            }

            // Nav links: extra steps between the triangles under their ends
            var links = new List<(int from, int to, bool oneWay)>();
            foreach (ZombiesNavLink l in KitExport.InOrder<ZombiesNavLink>())
            {
                int a = TriAt(g, l.transform.position), b = TriAt(g, l.WorldEnd);
                if (a >= 0 && b >= 0)
                    links.Add((a, b, l.OneWay));
            }

            List<int> zSeeds = zombieSeeds.Select(p => TriAt(g, p)).ToList();
            int[] zombieFrom = Flood(g, zSeeds.Where(t => t >= 0).Distinct().ToList(), links, out int[] parent);
            var playerSeeds = PlayerSpots.Select(p => TriAt(g, p)).Where(t => t >= 0).ToList();
            if (playerSeeds.Count == 0)
                playerSeeds = Windows.Where(w => w.InsideOnMesh).Select(w => TriAt(g, w.Inside)).Where(t => t >= 0).ToList();
            int[] playerFrom = Flood(g, playerSeeds.Distinct().ToList(), links, out _);
            Detail = $"{n} pieces, zombie seeds found {zSeeds.Count(t => t >= 0)}/{zSeeds.Count}, player seeds {playerSeeds.Count}, links {links.Count}";

            // Leaks: a player spawn or a barricade's inside the zombie side reaches without a barricade
            var targets = new List<(Vector3 p, string name, Object obj)>();
            targets.AddRange(KitSdk.PlayerSpawns().Select(c => (c.transform.position, c.name, (Object)c.gameObject)));
            targets.AddRange(KitExport.InOrder<ZombiesMarker>().Where(m => m.Kind == ZombiesKind.PlayerStart).Select(m => (m.transform.position, m.name, (Object)m)));
            targets.AddRange(Windows.Where(w => w.InsideOnMesh).Select(w => (w.Inside, "the inside of " + w.Window.name, (Object)w.Window)));
            var leakedSeeds = new HashSet<int>();
            foreach (var target in targets)
            {
                int t = TriAt(g, target.p);
                if (t < 0 || zombieFrom[t] < 0 || !leakedSeeds.Add(zombieFrom[t]) || Leaks.Count >= 6)
                    continue;
                var route = new List<Vector3>();
                for (int k = t; k >= 0; k = parent[k])
                {
                    route.Add(g.Centre(k));
                    if (parent[k] == k)
                        break;
                }
                route.Reverse();
                Leaks.Add(new Leak
                {
                    Route = Smooth(route),
                    Target = target.obj,
                    Text = $"LEAK: zombies can walk from outside to {target.name} without going through a barricade, door or debris (red dashed line). " +
                           "Close the gap in the map, or put a barricade, door, debris or blocker in it.",
                });
            }

            // The tinted mesh: zombie side, player side, shut, reached by nobody
            ZombieArea = PlayerArea = BlockedArea = LoneArea = 0f;
            var verts = new List<Vector3>(n * 3);
            var cols = new List<Color32>(n * 3);
            var zombieCol = new Color32(255, 120, 40, 70);
            var leakCol = new Color32(255, 30, 30, 110);
            var playerCol = new Color32(60, 220, 90, 60);
            var shutCol = new Color32(90, 90, 100, 120);
            var loneCol = new Color32(80, 140, 255, 40);
            bool leaking = Leaks.Count > 0;
            for (int t = 0; t < n; t++)
            {
                float area = g.Area(t);
                Color32 c;
                if (g.Shut[t])
                {
                    c = shutCol;
                    BlockedArea += area;
                }
                else if (zombieFrom[t] >= 0)
                {
                    c = leaking && playerFrom[t] >= 0 ? leakCol : zombieCol;
                    ZombieArea += area;
                }
                else if (playerFrom[t] >= 0)
                {
                    c = playerCol;
                    PlayerArea += area;
                }
                else
                {
                    c = loneCol;
                    LoneArea += area;
                }
                for (int k = 0; k < 3; k++)
                {
                    verts.Add(g.V[g.T[t * 3 + k]] + Vector3.up * 0.04f);
                    cols.Add(c);
                }
            }
            var mesh = new Mesh { name = "SPZombies walk areas", hideFlags = HideFlags.HideAndDontSave, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(Enumerable.Range(0, verts.Count).ToArray(), 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3[] Smooth(List<Vector3> pts)
        {
            if (pts.Count < 3)
                return pts.ToArray();
            var o = new Vector3[pts.Count];
            o[0] = pts[0];
            o[pts.Count - 1] = pts[pts.Count - 1];
            for (int i = 1; i < pts.Count - 1; i++)
                o[i] = (pts[i - 1] + pts[i] * 2f + pts[i + 1]) * 0.25f;
            return o;
        }

        // Over the open pieces from the seeds, nearest first (walking distance between piece middles): for each piece, the
        // seed it was reached from (-1: not reached), and the piece before it on the shortest way there
        private static int[] Flood(Graph g, List<int> seeds, List<(int from, int to, bool oneWay)> links, out int[] parent)
        {
            int n = g.T.Length / 3;
            var from = new int[n];
            var dist = new float[n];
            parent = new int[n];
            for (int i = 0; i < n; i++)
            {
                from[i] = -1;
                parent[i] = -1;
                dist[i] = float.MaxValue;
            }
            var heap = new Heap();
            foreach (int s in seeds)
                if (!g.Shut[s] && from[s] < 0)
                {
                    from[s] = s;
                    parent[s] = s;
                    dist[s] = 0f;
                    heap.Push(0f, s);
                }
            var linkFrom = new Dictionary<int, List<int>>();
            foreach (var l in links)
            {
                if (!linkFrom.TryGetValue(l.from, out var list))
                    linkFrom[l.from] = list = new List<int>();
                list.Add(l.to);
                if (!l.oneWay)
                {
                    if (!linkFrom.TryGetValue(l.to, out var back))
                        linkFrom[l.to] = back = new List<int>();
                    back.Add(l.from);
                }
            }
            var centre = new Vector3[n];
            for (int i = 0; i < n; i++)
                centre[i] = g.Centre(i);
            while (heap.Count > 0)
            {
                heap.Pop(out float d, out int t);
                if (d > dist[t])
                    continue;
                IEnumerable<int> next = g.Next[t];
                if (linkFrom.TryGetValue(t, out var extra))
                    next = next.Concat(extra);
                foreach (int u in next)
                {
                    if (g.Shut[u])
                        continue;
                    float nd = d + Vector3.Distance(centre[t], centre[u]);
                    if (nd >= dist[u])
                        continue;
                    dist[u] = nd;
                    from[u] = from[t];
                    parent[u] = t;
                    heap.Push(nd, u);
                }
            }
            return from;
        }

        // A plain binary heap of (distance, piece), smallest first
        private class Heap
        {
            private readonly List<(float d, int t)> _items = new List<(float, int)>();

            public int Count => _items.Count;

            public void Push(float d, int t)
            {
                _items.Add((d, t));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int up = (i - 1) / 2;
                    if (_items[up].d <= _items[i].d)
                        break;
                    (_items[up], _items[i]) = (_items[i], _items[up]);
                    i = up;
                }
            }

            public void Pop(out float d, out int t)
            {
                (d, t) = _items[0];
                int last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, small = i;
                    if (l < _items.Count && _items[l].d < _items[small].d)
                        small = l;
                    if (r < _items.Count && _items[r].d < _items[small].d)
                        small = r;
                    if (small == i)
                        break;
                    (_items[small], _items[i]) = (_items[i], _items[small]);
                    i = small;
                }
            }
        }

        private static Graph _graph;
        private static (int, int, Vector3) _graphKey;

        // Pieces at most this long a side: a door shuts its doorway, not the whole room floor one big triangle spans
        private const float Piece = 1f;

        // The navmesh as a graph, made once per bake (kept while the baked navmesh is the same)
        private static Graph Build(NavMeshTriangulation tri)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < tri.vertices.Length; i += Mathf.Max(1, tri.vertices.Length / 64))
                sum += tri.vertices[i];
            var key = (tri.vertices.Length, tri.indices.Length, sum);
            if (_graph != null && _graphKey == key)
            {
                _graph.Shut = null;
                return _graph;
            }

            // Split big triangles into a grid of pieces
            var pv = new List<Vector3>(tri.vertices.Length * 2);
            var pi = new List<int>(tri.indices.Length * 2);
            for (int t = 0; t < tri.indices.Length; t += 3)
            {
                Vector3 a = tri.vertices[tri.indices[t]], b = tri.vertices[tri.indices[t + 1]], c = tri.vertices[tri.indices[t + 2]];
                float longest = Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude));
                int k = Mathf.Clamp(Mathf.CeilToInt(longest / Piece), 1, 64);
                int Point(int i, int j)
                {
                    pv.Add(a + (b - a) * ((float)i / k) + (c - a) * ((float)j / k));
                    return pv.Count - 1;
                }
                var grid = new int[k + 1, k + 1];
                for (int i = 0; i <= k; i++)
                    for (int j = 0; j <= k - i; j++)
                        grid[i, j] = Point(i, j);
                for (int i = 0; i < k; i++)
                    for (int j = 0; j < k - i; j++)
                    {
                        pi.Add(grid[i, j]);
                        pi.Add(grid[i + 1, j]);
                        pi.Add(grid[i, j + 1]);
                        if (i + j < k - 1)
                        {
                            pi.Add(grid[i + 1, j]);
                            pi.Add(grid[i + 1, j + 1]);
                            pi.Add(grid[i, j + 1]);
                        }
                    }
            }

            // Weld the vertices (each piece comes with its own), 1 cm apart
            var ids = new Dictionary<(int, int, int), int>();
            var welded = new List<Vector3>();
            var map = new int[pv.Count];
            for (int i = 0; i < pv.Count; i++)
            {
                Vector3 v = pv[i];
                var wk = (Mathf.RoundToInt(v.x * 100f), Mathf.RoundToInt(v.y * 100f), Mathf.RoundToInt(v.z * 100f));
                if (!ids.TryGetValue(wk, out int id))
                {
                    id = welded.Count;
                    ids[wk] = id;
                    welded.Add(v);
                }
                map[i] = id;
            }
            var g = new Graph { V = welded.ToArray(), T = pi.Select(i => map[i]).ToArray() };
            int n = g.T.Length / 3;
            g.Next = new List<int>[n];
            for (int t = 0; t < n; t++)
                g.Next[t] = new List<int>(3);

            // Triangles sharing an edge are neighbours
            var edges = new Dictionary<long, int>();
            var open = new List<(int t, int a, int b)>();
            for (int t = 0; t < n; t++)
                for (int e = 0; e < 3; e++)
                {
                    int a = g.T[t * 3 + e], b = g.T[t * 3 + (e + 1) % 3];
                    long ek = a < b ? (long)a * int.MaxValue + b : (long)b * int.MaxValue + a;
                    if (edges.TryGetValue(ek, out int other))
                    {
                        if (other >= 0)
                        {
                            Link(g, t, other);
                            edges[ek] = -1 - other;
                        }
                        else
                            Link(g, t, -1 - other);
                    }
                    else
                        edges[ek] = t;
                }
            // Edges with one triangle: the navmesh's outline, plus the seams between baked tiles (their vertices don't
            // always line up). Two such edges lying along each other join their triangles.
            foreach (var kv in edges)
                if (kv.Value >= 0)
                {
                    long ek = kv.Key;
                    int a = (int)(ek / int.MaxValue), b = (int)(ek % int.MaxValue);
                    open.Add((kv.Value, a, b));
                }
            Stitch(g, open);
            _graph = g;
            _graphKey = key;
            return g;
        }

        private static void Link(Graph g, int a, int b)
        {
            if (a == b || g.Next[a].Contains(b))
                return;
            g.Next[a].Add(b);
            g.Next[b].Add(a);
        }

        private static void Stitch(Graph g, List<(int t, int a, int b)> open)
        {
            const float Cell = 2f;
            var grid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < open.Count; i++)
            {
                Vector3 m = (g.V[open[i].a] + g.V[open[i].b]) * 0.5f;
                var key = (Mathf.FloorToInt(m.x / Cell), Mathf.FloorToInt(m.z / Cell));
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < open.Count; i++)
            {
                Vector3 a = g.V[open[i].a], b = g.V[open[i].b];
                Vector3 m = (a + b) * 0.5f;
                int cx = Mathf.FloorToInt(m.x / Cell), cz = Mathf.FloorToInt(m.z / Cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue((cx + dx, cz + dz), out var near))
                            continue;
                        foreach (int j in near)
                        {
                            if (j <= i || open[j].t == open[i].t)
                                continue;
                            if (AlongEachOther(a, b, g.V[open[j].a], g.V[open[j].b]))
                                Link(g, open[i].t, open[j].t);
                        }
                    }
            }
        }

        // Two edges on the same line (within 3 cm across, 15 cm up) that overlap by more than 2 cm
        private static bool AlongEachOther(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.01f)
                return false;
            Vector3 dir = ab / len;
            float Off(Vector3 p)
            {
                Vector3 ap = p - a;
                Vector3 across = ap - dir * Vector3.Dot(ap, dir);
                return new Vector2(across.x, across.z).magnitude + Mathf.Max(0f, Mathf.Abs(across.y) - 0.15f) * 10f;
            }
            if (Off(c) > 0.03f || Off(d) > 0.03f)
                return false;
            float s0 = Vector3.Dot(c - a, dir), s1 = Vector3.Dot(d - a, dir);
            float lo = Mathf.Max(0f, Mathf.Min(s0, s1)), hi = Mathf.Min(len, Mathf.Max(s0, s1));
            return hi - lo > 0.02f;
        }

        private static bool TriInBox(Vector3 a, Vector3 b, Vector3 c, Vector3 centre, Quaternion rot, Vector3 half)
        {
            Quaternion inv = Quaternion.Inverse(rot);
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 4 - i; j++)
                {
                    float u = i / 4f, v = j / 4f;
                    Vector3 p = a + (b - a) * u + (c - a) * v;
                    Vector3 l = inv * (p - centre);
                    if (Mathf.Abs(l.x) <= half.x && Mathf.Abs(l.y) <= half.y && Mathf.Abs(l.z) <= half.z)
                        return true;
                }
            return false;
        }

        // The triangle under a point (on the navmesh within 2 m), or -1
        private static int TriAt(Graph g, Vector3 p)
        {
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                return -1;
            Vector3 q = hit.position;
            int best = -1;
            float bestDy = 0.6f;
            int n = g.T.Length / 3;
            for (int t = 0; t < n; t++)
            {
                Vector3 a = g.V[g.T[t * 3]], b = g.V[g.T[t * 3 + 1]], c = g.V[g.T[t * 3 + 2]];
                if (q.x < Mathf.Min(a.x, b.x, c.x) - 0.01f || q.x > Mathf.Max(a.x, b.x, c.x) + 0.01f || q.z < Mathf.Min(a.z, b.z, c.z) - 0.01f || q.z > Mathf.Max(a.z, b.z, c.z) + 0.01f)
                    continue;
                if (!InTriangleXZ(q, a, b, c, out float y))
                    continue;
                float dy = Mathf.Abs(y - q.y);
                if (dy < bestDy)
                {
                    bestDy = dy;
                    best = t;
                }
            }
            return best;
        }

        private static bool InTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
            if (Mathf.Abs(d) < 1e-8f)
                return false;
            float w1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
            float w2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
            float w3 = 1f - w1 - w2;
            const float e = -0.01f;
            if (w1 < e || w2 < e || w3 < e)
                return false;
            y = w1 * a.y + w2 * b.y + w3 * c.y;
            return true;
        }
    }
}
