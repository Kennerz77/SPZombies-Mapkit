using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// The Scene view drawing: a label over each marker (what it is, its price or zone, a window's sill check), the arrow
    /// showing which way it faces, and zone outlines. Only near the camera, so a busy map stays readable.
    /// </summary>
    [InitializeOnLoad]
    internal static class KitScene
    {
        private const string PrefLabels = "SPZombiesMapKit.ShowLabels", PrefArrows = "SPZombiesMapKit.ShowArrows", PrefDistance = "SPZombiesMapKit.LabelDistance";

        public static bool ShowLabels
        {
            get => EditorPrefs.GetBool(PrefLabels, true);
            set => EditorPrefs.SetBool(PrefLabels, value);
        }

        public static bool ShowArrows
        {
            get => EditorPrefs.GetBool(PrefArrows, true);
            set => EditorPrefs.SetBool(PrefArrows, value);
        }

        public static float Distance
        {
            get => EditorPrefs.GetFloat(PrefDistance, 60f);
            set => EditorPrefs.SetFloat(PrefDistance, value);
        }

        private static GUIStyle _style;
        private static Texture2D _back;

        static KitScene()
        {
            SceneView.duringSceneGui += Draw;
        }

        internal static GUIStyle Style()
        {
            if (_style != null && _back != null)
                return _style;
            _back = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _back.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
            _back.Apply();
            _style = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white, background = _back },
                padding = new RectOffset(4, 4, 1, 2),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            return _style;
        }

        private static void Draw(SceneView view)
        {
            if (Event.current.type != EventType.Repaint || view.camera == null)
                return;
            Camera cam = view.camera;
            Vector3 eye = cam.transform.position, look = cam.transform.forward;
            float maxSq = Distance * Distance;
            CompareFunction was = Handles.zTest;
            foreach (ZombiesMarker m in KitPreviews.Markers)
            {
                if (m == null || !m.gameObject.activeInHierarchy)
                    continue;
                Vector3 p = m.transform.position;
                Vector3 to = p - eye;
                if (to.sqrMagnitude > maxSq || Vector3.Dot(to, look) < 0f)
                    continue;
                if (ShowArrows && KitInfo.HasFacing(m.Kind))
                    Arrow(m);
                if (ShowLabels)
                {
                    string text = KitInfo.Label(m);
                    if (m.Kind == ZombiesKind.Window && KitCheck.WindowNotes.TryGetValue(m, out var note))
                        text += "\n" + Short(note.text);
                    float h = m.HasPreviewBox ? m.PreviewBox.max.y : (ZombiesMarker.IsSized(m.Kind) ? m.Size.y : 1.8f);
                    Handles.zTest = CompareFunction.Always;
                    Handles.Label(p + m.transform.up * (h + 0.15f), text, Style());
                }
            }
            foreach (ZombiesZoneOutline z in Object.FindObjectsOfType<ZombiesZoneOutline>())
                if (z.isActiveAndEnabled && (z.transform.position - eye).sqrMagnitude < maxSq * 9f)
                    Outline(z);
            Handles.zTest = was;
        }

        private static string Short(string s)
        {
            int cut = s.IndexOf(':');
            return cut > 0 && s.IndexOf('(') > cut ? s.Substring(0, cut) : (s.Length > 60 ? s.Substring(0, 60) + "..." : s);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        private static void Arrow(ZombiesMarker m)
        {
            Transform t = m.transform;
            Vector3 fwd = Flat(t.forward);
            Vector3 origin = t.position + Vector3.up * 0.05f;
            Vector3 dir;
            Color c;
            if (m.Kind == ZombiesKind.Window)
            {
                // Zombies come in from its front (the outside) to the inside
                dir = -fwd;
                origin += fwd * 0.6f;
                c = new Color(1f, 0.25f, 0.15f, 0.95f);
            }
            else if (m.Kind == ZombiesKind.ZombieSpawn || m.Kind == ZombiesKind.PlayerStart)
            {
                dir = fwd;
                c = new Color(0.3f, 1f, 0.4f, 0.95f);
            }
            else
            {
                // The side the player uses it from
                float depth = m.HasPreviewBox ? m.PreviewBox.size.z : 0.5f;
                origin += fwd * (depth * 0.5f + 0.15f);
                dir = fwd;
                c = new Color(1f, 0.85f, 0.2f, 0.95f);
            }
            Handles.zTest = CompareFunction.LessEqual;
            Handles.color = c;
            Handles.ArrowHandleCap(0, origin, Quaternion.LookRotation(dir, Vector3.up), 0.8f, EventType.Repaint);
        }

        private static void Outline(ZombiesZoneOutline z)
        {
            List<Vector3> pts = z.WorldCorners();
            if (pts.Count < 2)
                return;
            float floor = pts.Min(p => p.y) - z.Below, top = pts.Max(p => p.y) + z.Height;
            Color c = KitInfo.ZoneColour(Mathf.Max(1, z.Id));
            Handles.zTest = CompareFunction.Always;
            Handles.color = new Color(c.r, c.g, c.b, 0.9f);
            var low = pts.Select(p => new Vector3(p.x, floor, p.z)).ToList();
            var high = pts.Select(p => new Vector3(p.x, top, p.z)).ToList();
            low.Add(low[0]);
            high.Add(high[0]);
            Handles.DrawAAPolyLine(3f, low.ToArray());
            Handles.color = new Color(c.r, c.g, c.b, 0.45f);
            Handles.DrawAAPolyLine(2f, high.ToArray());
            for (int i = 0; i < pts.Count; i++)
                Handles.DrawLine(low[i], high[i]);
            // The floor outline at each corner's own height (where it was drawn)
            Handles.color = new Color(c.r, c.g, c.b, 0.7f);
            var own = pts.ToList();
            own.Add(own[0]);
            Handles.DrawAAPolyLine(2f, own.ToArray());
            if (ShowLabels)
            {
                Vector3 mid = new Vector3(pts.Average(p => p.x), top, pts.Average(p => p.z));
                Handles.Label(mid, $"Zone {z.Id}", Style());
            }
        }
    }
}
