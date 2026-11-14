using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SPZombies.MapKit.Editor
{
    /// <summary>The marker's Inspector: the right choices for its kind, its size in metres, and quick buttons.</summary>
    [CustomEditor(typeof(ZombiesMarker)), CanEditMultipleObjects]
    internal class ZombiesMarkerEditor : UnityEditor.Editor
    {
        private SerializedProperty _kind, _what, _price, _zone, _power, _noBoard, _boxStart, _look;

        private void OnEnable()
        {
            _kind = serializedObject.FindProperty(nameof(ZombiesMarker.Kind));
            _what = serializedObject.FindProperty(nameof(ZombiesMarker.What));
            _price = serializedObject.FindProperty(nameof(ZombiesMarker.Price));
            _zone = serializedObject.FindProperty(nameof(ZombiesMarker.Zone));
            _power = serializedObject.FindProperty(nameof(ZombiesMarker.OpensWithPower));
            _noBoard = serializedObject.FindProperty(nameof(ZombiesMarker.NoBoard));
            _boxStart = serializedObject.FindProperty(nameof(ZombiesMarker.BoxStartsHere));
            _look = serializedObject.FindProperty(nameof(ZombiesMarker.CustomLook));
        }

        public override void OnInspectorGUI()
        {
            var first = (ZombiesMarker)target;
            ZombiesMarker[] all = targets.Cast<ZombiesMarker>().ToArray();
            string[] oldLabels = all.Select(KitInfo.Label).ToArray();
            ZombiesKind oldKind = first.Kind;
            bool sameKind = all.All(m => m.Kind == first.Kind);

            serializedObject.Update();
            if (sameKind)
                EditorGUILayout.HelpBox(KitInfo.Help(first.Kind), MessageType.None);
            EditorGUILayout.PropertyField(_kind);
            if (sameKind)
            {
                ZombiesKind k = first.Kind;
                bool custom = KitInfo.CanHaveCustomLook(k) && all.All(m => m.CustomLook != null);
                List<(string id, string label)> choices = KitInfo.Choices(k);
                if (choices != null && !custom)
                    WhatPopup(choices);
                else if (k == ZombiesKind.Prop || ((k == ZombiesKind.PowerLight || k == ZombiesKind.Lamp) && !custom))
                    EditorGUILayout.PropertyField(_what, new GUIContent(k == ZombiesKind.Prop ? "What (scene:<name> or item:<id>)" : "Fixture (a map object's name)"));
                if (KitInfo.HasPrice(k))
                    EditorGUILayout.PropertyField(_price, new GUIContent("Price (points)"));
                if (k == ZombiesKind.Door || k == ZombiesKind.Debris)
                    EditorGUILayout.PropertyField(_power, new GUIContent("Opens with the power"));
                if (k == ZombiesKind.Zone)
                    EditorGUILayout.PropertyField(_zone, new GUIContent("Zone number"));
                else if (KitInfo.HasZone(k))
                    EditorGUILayout.PropertyField(_zone, new GUIContent("Zone (0: the one it stands in)"));
                if (k == ZombiesKind.WallBuy)
                    EditorGUILayout.PropertyField(_noBoard, new GUIContent("No board (chalk on the wall)"));
                if (k == ZombiesKind.BoxSpot)
                    EditorGUILayout.PropertyField(_boxStart, new GUIContent("Box starts here"));
                if (KitInfo.CanHaveCustomLook(k))
                {
                    EditorGUILayout.PropertyField(_look, new GUIContent("Custom look"));
                    if (custom)
                    {
                        EditorGUILayout.HelpBox("Uses an object of your map as the look: on Export the kit keeps a copy of it far under the map " +
                                                "(SPZombies copies it from there in a match). Save the scene and build the map after the first export.", MessageType.None);
                        if (all.Length == 1 && KitTurnInto.OriginalStillBuilt(first))
                        {
                            EditorGUILayout.HelpBox($"The original ({first.CustomLook.name}) is still in the map build: in a match it would stay standing after this is bought.", MessageType.Warning);
                            if (GUILayout.Button("Take the original out of the map build"))
                                KitTurnInto.TakeOut(first);
                        }
                        if (all.Length == 1 && GUILayout.Button("Put the original back (and remove this marker)"))
                        {
                            KitTurnInto.PutBack(first);
                            GUIUtility.ExitGUI();
                        }
                    }
                    else if (all.Length == 1)
                        EditorGUILayout.LabelField("Tip: select an object of your map and use SPZombies > Turn Selected Into to make it one of these.", EditorStyles.wordWrappedMiniLabel);
                }
            }
            serializedObject.ApplyModifiedProperties();

            // A new kind: a sensible "What" and size for it
            if (first.Kind != oldKind)
                foreach (ZombiesMarker m in all)
                    Retype(m);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == oldLabels[i] && KitInfo.Label(all[i]) != oldLabels[i])
                {
                    Undo.RecordObject(all[i].gameObject, "Rename marker");
                    all[i].name = KitInfo.Label(all[i]);
                }

            EditorGUILayout.Space();
            if (all.Length == 1)
                SizeField(first);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Drop to ground"))
                    foreach (ZombiesMarker m in all)
                        KitPlace.DropToGround(m.transform);
                if (GUILayout.Button("Face the camera"))
                    FaceCamera(all);
                if (GUILayout.Button("Select all like this"))
                    Selection.objects = KitPreviews.Markers.Where(m => m != null && m.Kind == first.Kind).Select(m => (Object)m.gameObject).ToArray();
            }
            if (all.Length == 1 && first.Kind == ZombiesKind.Window)
            {
                if (GUILayout.Button("Check this window's sill"))
                {
                    string v = KitCheck.WindowVerdict(first, out Color c, out _);
                    KitCheck.WindowNotes[first] = (v, c);
                }
                if (KitCheck.WindowNotes.TryGetValue(first, out var note))
                    EditorGUILayout.HelpBox(note.text, MessageType.None);
            }
        }

        private void WhatPopup(List<(string id, string label)> choices)
        {
            int index = choices.FindIndex(c => c.id == _what.stringValue);
            var labels = choices.Select(c => c.label).ToList();
            if (index < 0)
            {
                labels.Add(string.IsNullOrEmpty(_what.stringValue) ? "(none)" : $"(other: {_what.stringValue})");
                index = labels.Count - 1;
            }
            EditorGUI.showMixedValue = _what.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("What", index, labels.ToArray());
            if (EditorGUI.EndChangeCheck() && picked < choices.Count)
                _what.stringValue = choices[picked].id;
            EditorGUI.showMixedValue = false;
        }

        private static void Retype(ZombiesMarker m)
        {
            Undo.RecordObject(m, "Change kind");
            Undo.RecordObject(m.transform, "Change kind");
            List<(string id, string label)> choices = KitInfo.Choices(m.Kind);
            if (choices != null && choices.Count > 0 && choices.All(c => c.id != m.What))
                m.What = choices[0].id;
            else if (choices == null && m.Kind != ZombiesKind.Prop && m.Kind != ZombiesKind.PowerLight && m.Kind != ZombiesKind.Lamp)
                m.What = "";
            if (KitInfo.HasPrice(m.Kind) && m.Price <= 0)
                m.Price = KitInfo.DefaultPrice(m.Kind);
            if (ZombiesMarker.IsSized(m.Kind))
            {
                if (m.transform.localScale == Vector3.one)
                    m.transform.localScale = KitInfo.DefaultSize(m.Kind);
            }
            else
                m.transform.localScale = Vector3.one;
            if (m.Kind == ZombiesKind.Zone && m.Zone < 1)
                m.Zone = 1;
        }

        private static void SizeField(ZombiesMarker m)
        {
            Transform t = m.transform;
            Vector3 parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
            if (ZombiesMarker.IsSized(m.Kind))
            {
                EditorGUI.BeginChangeCheck();
                Vector3 size = EditorGUILayout.Vector3Field(m.Kind == ZombiesKind.Decal ? "Size (width, height, -)" : "Size (metres: width, height, depth)", m.Size);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "Size");
                    t.localScale = new Vector3(size.x / Safe(parent.x), size.y / Safe(parent.y), size.z / Safe(parent.z));
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                float scale = EditorGUILayout.FloatField("Scale (1 = as made)", m.Size.x);
                if (EditorGUI.EndChangeCheck() && scale > 0.01f)
                {
                    Undo.RecordObject(t, "Scale");
                    t.localScale = new Vector3(scale / Safe(parent.x), scale / Safe(parent.y), scale / Safe(parent.z));
                }
            }
        }

        private static float Safe(float f) => Mathf.Abs(f) < 1e-4f ? 1f : f;

        private static void FaceCamera(ZombiesMarker[] all)
        {
            Camera cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            if (cam == null)
                return;
            foreach (ZombiesMarker m in all)
            {
                Vector3 to = cam.transform.position - m.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 1e-4f)
                    continue;
                Undo.RecordObject(m.transform, "Face the camera");
                m.transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            }
        }
    }

    /// <summary>The zone outline's corners: click a corner's dot to move it; add and remove corners in the Inspector.</summary>
    [CustomEditor(typeof(ZombiesZoneOutline))]
    internal class ZombiesZoneOutlineEditor : UnityEditor.Editor
    {
        private int _corner = -1;

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("A zone's outline on the floor. Click a corner's dot in the Scene view to move it. Zones 1 and 2 are open from the start; " +
                                    "the others open when a door or debris touching them is bought. Zombie spawns and windows inside belong to it.", MessageType.None);
            DrawDefaultInspector();
            var z = (ZombiesZoneOutline)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add a corner"))
                {
                    Undo.RecordObject(z, "Add corner");
                    int at = _corner >= 0 && _corner < z.Corners.Count ? _corner : z.Corners.Count - 1;
                    Vector3 a = z.Corners.Count > 0 ? z.Corners[Mathf.Max(0, at)] : Vector3.zero;
                    Vector3 b = z.Corners.Count > 0 ? z.Corners[(at + 1) % z.Corners.Count] : Vector3.forward;
                    z.Corners.Insert(at + 1, (a + b) * 0.5f);
                    _corner = at + 1;
                }
                GUI.enabled = _corner >= 0 && _corner < z.Corners.Count && z.Corners.Count > 3;
                if (GUILayout.Button("Remove the picked corner"))
                {
                    Undo.RecordObject(z, "Remove corner");
                    z.Corners.RemoveAt(_corner);
                    _corner = -1;
                }
                GUI.enabled = true;
                if (GUILayout.Button("Corners to the ground"))
                {
                    Undo.RecordObject(z, "Corners to the ground");
                    Physics.SyncTransforms();
                    for (int i = 0; i < z.Corners.Count; i++)
                    {
                        Vector3 w = z.transform.TransformPoint(z.Corners[i]);
                        if (Physics.Raycast(w + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
                            z.Corners[i] = z.transform.InverseTransformPoint(hit.point);
                    }
                }
            }
        }

        private void OnSceneGUI()
        {
            var z = (ZombiesZoneOutline)target;
            for (int i = 0; i < z.Corners.Count; i++)
            {
                Vector3 w = z.transform.TransformPoint(z.Corners[i]);
                float size = HandleUtility.GetHandleSize(w) * 0.09f;
                Handles.color = i == _corner ? Color.yellow : KitInfo.ZoneColour(Mathf.Max(1, z.Id));
                if (Handles.Button(w, Quaternion.identity, size, size * 1.4f, Handles.DotHandleCap))
                {
                    _corner = i;
                    Repaint();
                }
                if (i != _corner)
                    continue;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(w, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(z, "Move corner");
                    z.Corners[i] = z.transform.InverseTransformPoint(moved);
                }
            }
        }
    }

    /// <summary>The nav link's far end as a handle in the Scene view.</summary>
    [CustomEditor(typeof(ZombiesNavLink))]
    internal class ZombiesNavLinkEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Zombies walk from this object's spot to the far end (the second handle) as if it were ground. Both ends must be on the baked navmesh.", MessageType.None);
            DrawDefaultInspector();
        }

        private void OnSceneGUI()
        {
            var l = (ZombiesNavLink)target;
            EditorGUI.BeginChangeCheck();
            Vector3 end = Handles.PositionHandle(l.WorldEnd, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(l, "Move link end");
                l.End = l.transform.InverseTransformPoint(end);
            }
        }
    }
}

namespace SPZombies.MapKit.Editor
{
    /// <summary>A game over shot's Inspector: look through it, play it (or all of them) in the Scene view as the game over will.</summary>
    [UnityEditor.CustomEditor(typeof(ZombiesGameOverShot)), UnityEditor.CanEditMultipleObjects]
    internal class ZombiesGameOverShotEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "SPZombies moves the camera itself. It stands here looking along the blue arrow (tilt counts, roll doesn't), and over 7 s drifts 2.5 m " +
                "sideways (the red line) while it keeps looking at the yellow point, the first thing ahead. Shots play in order, top of the hierarchy first, and loop. " +
                "With none placed, SPZombies makes its own shots.", UnityEditor.MessageType.None);
            var shot = (ZombiesGameOverShot)target;
            using (new UnityEngine.GUILayout.HorizontalScope())
            {
                if (UnityEngine.GUILayout.Button("Look through it"))
                    KitShotPlayer.LookThrough(shot);
                if (UnityEngine.GUILayout.Button("Play it"))
                    KitShotPlayer.Play(new[] { shot });
                if (UnityEngine.GUILayout.Button("Play all shots"))
                    KitShotPlayer.Play(KitExport.InOrder<ZombiesGameOverShot>().ToArray());
            }
            if (UnityEngine.GUILayout.Button("Move it to where the Scene view camera is"))
                KitShotPlayer.MoveToView(shot);
            if (KitShotPlayer.Playing && UnityEngine.GUILayout.Button("Stop playing"))
                KitShotPlayer.Stop();
        }
    }

    /// <summary>Flies the Scene view camera through game over shots exactly as SPZombies' game over does.</summary>
    internal static class KitShotPlayer
    {
        private static ZombiesGameOverShot[] _shots;
        private static double _started;

        public static bool Playing => _shots != null;

        private static void Pose(UnityEditor.SceneView view, Vector3 at, Vector3 look)
        {
            Vector3 dir = look - at;
            if (dir.sqrMagnitude < 1e-4f)
                dir = Vector3.forward;
            float dist = dir.magnitude;
            view.orthographic = false;
            float fov = view.cameraSettings.fieldOfView;
            // The Scene view orbits its pivot: the pivot on the target, at the camera's distance from it
            view.LookAtDirect(look, Quaternion.LookRotation(dir, Vector3.up), dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            view.Repaint();
        }

        public static void LookThrough(ZombiesGameOverShot shot)
        {
            UnityEditor.SceneView view = UnityEditor.SceneView.lastActiveSceneView;
            if (view != null)
                Pose(view, shot.transform.position, shot.Target);
        }

        public static void Play(ZombiesGameOverShot[] shots)
        {
            if (shots == null || shots.Length == 0 || UnityEditor.SceneView.lastActiveSceneView == null)
                return;
            Stop();
            _shots = shots;
            _started = UnityEditor.EditorApplication.timeSinceStartup;
            UnityEditor.EditorApplication.update += Tick;
        }

        public static void Stop()
        {
            UnityEditor.EditorApplication.update -= Tick;
            _shots = null;
        }

        private static void Tick()
        {
            UnityEditor.SceneView view = UnityEditor.SceneView.lastActiveSceneView;
            double t = UnityEditor.EditorApplication.timeSinceStartup - _started;
            int i = (int)(t / ZombiesGameOverShot.Seconds);
            if (view == null || _shots == null || i >= _shots.Length || _shots[i] == null)
            {
                Stop();
                return;
            }
            ZombiesGameOverShot s = _shots[i];
            float u = Mathf.SmoothStep(0f, 1f, (float)(t % ZombiesGameOverShot.Seconds) / ZombiesGameOverShot.Seconds);
            // What it looks at is worked out once, from where it stands (as the game over does)
            Pose(view, Vector3.Lerp(s.From, s.To, u), s.Target);
        }

        public static void MoveToView(ZombiesGameOverShot shot)
        {
            UnityEditor.SceneView view = UnityEditor.SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
                return;
            UnityEditor.Undo.RecordObject(shot.transform, "Move game over shot");
            shot.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);
        }
    }
}
