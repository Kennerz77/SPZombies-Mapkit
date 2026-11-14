using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Draws the worked-out zombie paths (KitPaths) in the Scene view:
    /// - the walk areas tinted: orange the zombie side, green the player side, grey shut by barricades/doors/debris,
    ///   blue reached by nobody, red where they leak together;
    /// - each zombie spawn's route to its barricade, with arrows: green walks there, orange stops short (an X where it
    ///   stops), red no route, blue starts inside (hunts the players); seen faintly through walls;
    /// - at each barricade: where zombies stand to tear the boards (green dot, red when off the navmesh), where they land
    ///   inside (cyan), the sill line coloured by the vault check, and how many spawns feed it;
    /// - leaks as red dashed routes;
    /// - a legend with the counts, bottom left.
    /// Selecting a spawn or a barricade brings its own routes forward.
    /// </summary>
    [InitializeOnLoad]
    internal static class KitPathsView
    {
        private const string Pref = "SPZombiesMapKit.Paths.";

        public static bool Show { get => EditorPrefs.GetBool(Pref + "Show", true); set => EditorPrefs.SetBool(Pref + "Show", value); }
        public static bool ShowAreas { get => EditorPrefs.GetBool(Pref + "Areas", true); set => EditorPrefs.SetBool(Pref + "Areas", value); }
        public static bool OnlyProblems { get => EditorPrefs.GetBool(Pref + "OnlyProblems", false); set => EditorPrefs.SetBool(Pref + "OnlyProblems", value); }
        public static bool ShowInside { get => EditorPrefs.GetBool(Pref + "Inside", false); set => EditorPrefs.SetBool(Pref + "Inside", value); }
        public static bool ShowLegend { get => EditorPrefs.GetBool(Pref + "Legend", true); set => EditorPrefs.SetBool(Pref + "Legend", value); }
        public static bool Live { get => EditorPrefs.GetBool(Pref + "Live", true); set => EditorPrefs.SetBool(Pref + "Live", value); }

        public static readonly Color Walks = new Color(0.25f, 1f, 0.35f);
        public static readonly Color Partial = new Color(1f, 0.6f, 0.1f);
        public static readonly Color None = new Color(1f, 0.15f, 0.15f);
        public static readonly Color Inside = new Color(0.35f, 0.6f, 1f);
        public static readonly Color LeakColour = new Color(1f, 0.1f, 0.35f);

        private static Material _mat;
        private static int _seenRevision = -1;
        private static double _rerunAt;

        static KitPathsView()
        {
            SceneView.duringSceneGui += Draw;
            EditorApplication.update += LiveUpdate;
        }

        // With Live on, the paths are worked out again a moment after markers stop moving
        private static void LiveUpdate()
        {
            if (!Live || !KitPaths.Ran || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            double now = EditorApplication.timeSinceStartup;
            if (KitPreviews.Revision != _seenRevision)
            {
                _seenRevision = KitPreviews.Revision;
                _rerunAt = now + 0.6;
                return;
            }
            if (_rerunAt > 0 && now >= _rerunAt)
            {
                _rerunAt = 0;
                KitPaths.Run();
            }
        }

        public static void Seen() => _seenRevision = KitPreviews.Revision;

        private static Material Mat()
        {
            if (_mat != null)
                return _mat;
            _mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            _mat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return _mat;
        }

        private static void Draw(SceneView view)
        {
            if (!Show || !KitPaths.Ran)
                return;
            if (Event.current.type == EventType.Repaint)
            {
                CompareFunction was = Handles.zTest;
                if (ShowAreas && KitPaths.Areas != null)
                {
                    Mat().SetPass(0);
                    Graphics.DrawMeshNow(KitPaths.Areas, Matrix4x4.identity);
                }
                var picked = new HashSet<GameObject>(Selection.gameObjects);
                bool anyPicked = KitPaths.Routes.Any(r => picked.Contains(r.Spawn.gameObject) || (r.Window != null && picked.Contains(r.Window.gameObject)));

                if (ShowInside)
                    foreach (KitPaths.WindowInfo w in KitPaths.Windows)
                        if (w.InsideRoute.Length > 1)
                            Line(w.InsideRoute, new Color(0.5f, 0.8f, 1f, 0.6f), 2f, false);

                foreach (KitPaths.SpawnRoute r in KitPaths.Routes)
                {
                    if (r.Spawn == null)
                        continue;
                    bool problem = r.Status == KitPaths.Route.Partial || r.Status == KitPaths.Route.None || r.Status == KitPaths.Route.OffMesh || r.Stuck;
                    if (OnlyProblems && !problem)
                        continue;
                    bool focus = picked.Contains(r.Spawn.gameObject) || (r.Window != null && picked.Contains(r.Window.gameObject));
                    float alpha = anyPicked && !focus ? 0.3f : 1f;
                    float width = focus ? 6f : 3.5f;
                    Color c = Colour(r.Status);
                    c.a = alpha;
                    switch (r.Status)
                    {
                        case KitPaths.Route.OffMesh:
                            Ring(r.Spawn.transform.position, c, 0.6f);
                            Label(r.Spawn.transform.position + Vector3.up * 2.2f, "not on the navmesh", true);
                            break;
                        case KitPaths.Route.None:
                            Dashed(new[] { r.From, r.To }, c, width);
                            if (focus || !anyPicked)
                                Label(Vector3.Lerp(r.From, r.To, 0.5f) + Vector3.up * 0.6f, "no route", true);
                            break;
                        default:
                            if (r.Corners.Length > 1)
                                Line(r.Corners, c, width, true);
                            break;
                    }
                    if (r.Status == KitPaths.Route.Partial || (r.Status == KitPaths.Route.Inside && r.Stuck && r.Corners.Length > 0))
                    {
                        Cross(r.StopAt, c, 0.5f);
                        if (focus || !anyPicked)
                            Label(r.StopAt + Vector3.up * 0.8f, $"stops {r.Short:0.0} m short", true);
                    }
                    if (r.Status != KitPaths.Route.OffMesh)
                        Disc(r.From, c, 0.2f);
                }

                foreach (KitPaths.WindowInfo w in KitPaths.Windows)
                {
                    if (w.Window == null)
                        continue;
                    if (OnlyProblems && w.OutsideOnMesh && w.InsideOnMesh && w.Feeds > 0)
                        continue;
                    Disc(w.Outside, w.OutsideOnMesh ? Walks : None, 0.28f);
                    Disc(w.Inside, w.InsideOnMesh ? new Color(0.3f, 0.95f, 1f) : None, 0.2f);
                    // The sill: across the bottom of the opening, coloured by the vault check
                    Transform t = w.Window.transform;
                    float half = w.Window.Size.x * 0.5f;
                    Handles.zTest = CompareFunction.LessEqual;
                    Handles.color = w.VerdictColour;
                    Handles.DrawAAPolyLine(7f, t.position - t.right * half + Vector3.up * 0.02f, t.position + t.right * half + Vector3.up * 0.02f);
                    string feeds = w.Feeds == 0 ? "no zombies come here" : $"{w.Feeds} spawn{(w.Feeds == 1 ? "" : "s")}";
                    Label(t.position + t.up * (w.Window.Size.y + 0.6f), feeds, w.Feeds == 0);
                }

                foreach (KitPaths.Leak l in KitPaths.Leaks)
                {
                    Dashed(l.Route, LeakColour, 7f);
                    if (l.Route.Length > 0)
                        Label(l.Route[l.Route.Length / 2] + Vector3.up * 1.2f, "LEAK: zombies walk in here", true);
                }
                Handles.zTest = was;
            }
            if (ShowLegend)
                Legend(view);
        }

        public static Color Colour(KitPaths.Route r)
        {
            switch (r)
            {
                case KitPaths.Route.Walks: return Walks;
                case KitPaths.Route.Partial: return Partial;
                case KitPaths.Route.Inside: return Inside;
                default: return None;
            }
        }

        // ---------- Drawing pieces ----------

        // A route on the floor: faint where it's behind something, full where seen, with arrows along it
        private static void Line(Vector3[] pts, Color c, float width, bool arrows)
        {
            var lifted = pts.Select(p => p + Vector3.up * 0.12f).ToArray();
            Handles.zTest = CompareFunction.Greater;
            Handles.color = new Color(c.r, c.g, c.b, c.a * 0.25f);
            Handles.DrawAAPolyLine(width * 0.7f, lifted);
            Handles.zTest = CompareFunction.LessEqual;
            Handles.color = c;
            Handles.DrawAAPolyLine(width, lifted);
            if (!arrows)
                return;
            float next = 2.5f, along = 0f;
            for (int i = 1; i < lifted.Length; i++)
            {
                Vector3 a = lifted[i - 1], b = lifted[i];
                float seg = Vector3.Distance(a, b);
                if (seg < 0.01f)
                    continue;
                Vector3 dir = (b - a) / seg;
                while (next <= along + seg)
                {
                    Vector3 p = a + dir * (next - along);
                    Handles.ConeHandleCap(0, p, Quaternion.LookRotation(dir), 0.32f, EventType.Repaint);
                    next += 5f;
                }
                along += seg;
            }
        }

        private static void Dashed(Vector3[] pts, Color c, float width)
        {
            Handles.zTest = CompareFunction.Always;
            Handles.color = c;
            const float Dash = 0.6f, Gap = 0.4f;
            for (int i = 1; i < pts.Length; i++)
            {
                Vector3 a = pts[i - 1] + Vector3.up * 0.15f, b = pts[i] + Vector3.up * 0.15f;
                float seg = Vector3.Distance(a, b);
                if (seg < 0.01f)
                    continue;
                Vector3 dir = (b - a) / seg;
                for (float s = 0f; s < seg; s += Dash + Gap)
                    Handles.DrawAAPolyLine(width, a + dir * s, a + dir * Mathf.Min(seg, s + Dash));
            }
        }

        private static void Disc(Vector3 p, Color c, float r)
        {
            Handles.zTest = CompareFunction.Always;
            Handles.color = c;
            Handles.DrawSolidDisc(p + Vector3.up * 0.06f, Vector3.up, r);
        }

        private static void Ring(Vector3 p, Color c, float r)
        {
            Handles.zTest = CompareFunction.Always;
            Handles.color = c;
            Handles.DrawWireDisc(p + Vector3.up * 0.06f, Vector3.up, r, 4f);
        }

        private static void Cross(Vector3 p, Color c, float r)
        {
            Handles.zTest = CompareFunction.Always;
            Handles.color = c;
            Vector3 q = p + Vector3.up * 0.15f;
            Handles.DrawAAPolyLine(6f, q + new Vector3(-r, 0f, -r), q + new Vector3(r, 0f, r));
            Handles.DrawAAPolyLine(6f, q + new Vector3(-r, 0f, r), q + new Vector3(r, 0f, -r));
        }

        private static GUIStyle _bad;
        private static Texture2D _badBack;

        private static void Label(Vector3 p, string text, bool bad)
        {
            Handles.zTest = CompareFunction.Always;
            GUIStyle style = KitScene.Style();
            if (bad)
            {
                if (_bad == null || _badBack == null)
                {
                    _badBack = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    _badBack.SetPixel(0, 0, new Color(0.55f, 0.05f, 0.05f, 0.85f));
                    _badBack.Apply();
                    _bad = new GUIStyle(style) { normal = { textColor = Color.white, background = _badBack } };
                }
                style = _bad;
            }
            Handles.Label(p, text, style);
        }

        // ---------- The legend ----------

        private static void Legend(SceneView view)
        {
            var rows = new List<(Color c, string text, bool dashed)>();
            int walks = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.Walks);
            int partial = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.Partial);
            int none = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.None || r.Status == KitPaths.Route.OffMesh);
            int inside = KitPaths.Routes.Count(r => r.Status == KitPaths.Route.Inside);
            rows.Add((Walks, $"walks to its barricade: {walks}", false));
            rows.Add((Partial, $"stops short (X): {partial}", false));
            rows.Add((None, $"no route / off the navmesh: {none}", false));
            rows.Add((Inside, $"starts inside, hunts players: {inside}", false));
            rows.Add((LeakColour, $"leak (walks in, no barricade): {KitPaths.Leaks.Count}", true));
            if (ShowAreas && KitPaths.Areas != null)
            {
                rows.Add((new Color(1f, 0.47f, 0.16f), "area: zombie side", false));
                rows.Add((new Color(0.24f, 0.86f, 0.35f), "area: player side", false));
                rows.Add((new Color(0.35f, 0.35f, 0.4f), "area: shut (barricade, door, debris)", false));
                rows.Add((new Color(0.31f, 0.55f, 1f), "area: reached by nobody", false));
            }
            int noZombies = KitPaths.Windows.Count(w => w.Feeds == 0);
            string title = !KitPaths.HasNavMesh ? "Zombie paths: no navmesh baked" : $"Zombie paths ({KitPaths.RanAt:HH:mm:ss})";
            float h = 26f + rows.Count * 16f + (noZombies > 0 ? 16f : 0f);
            var box = new Rect(10f, view.position.height - h - 34f, 262f, h);
            Handles.BeginGUI();
            EditorGUI.DrawRect(box, new Color(0.08f, 0.08f, 0.09f, 0.85f));
            GUI.Label(new Rect(box.x + 8, box.y + 4, box.width - 16, 18), title, EditorStyles.whiteBoldLabel);
            float y = box.y + 24f;
            foreach (var row in rows)
            {
                if (row.dashed)
                {
                    EditorGUI.DrawRect(new Rect(box.x + 8, y + 6, 6, 3), row.c);
                    EditorGUI.DrawRect(new Rect(box.x + 17, y + 6, 6, 3), row.c);
                }
                else
                    EditorGUI.DrawRect(new Rect(box.x + 8, y + 3, 15, 9), row.c);
                GUI.Label(new Rect(box.x + 30, y - 1, box.width - 36, 16), row.text, EditorStyles.whiteMiniLabel);
                y += 16f;
            }
            if (noZombies > 0)
                GUI.Label(new Rect(box.x + 8, y - 1, box.width - 16, 16), $"{noZombies} barricade(s) get no zombies", EditorStyles.whiteMiniLabel);
            Handles.EndGUI();
        }
    }
}
