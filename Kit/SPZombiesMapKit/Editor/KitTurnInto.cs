using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SPZombies.MapKit.Editor
{
    /// <summary>
    /// Turns an object of the map into a buyable door, debris pile, power light or lamp: a marker on the object's exact spot,
    /// turned as it is and sized to it, with the object as its look. The original is switched off and kept out of the map
    /// build (tagged EditorOnly): in a match SPZombies puts its copy there instead, and takes it away when it's bought.
    /// </summary>
    internal static class KitTurnInto
    {
        [MenuItem("SPZombies/Turn Selected Into/Buyable Door", false, 30)]
        [MenuItem("GameObject/SPZombies/Turn Into Buyable Door", false, 0)]
        private static void Door() => Run(ZombiesKind.Door);

        [MenuItem("SPZombies/Turn Selected Into/Buyable Debris", false, 31)]
        [MenuItem("GameObject/SPZombies/Turn Into Buyable Debris", false, 1)]
        private static void Debris() => Run(ZombiesKind.Debris);

        [MenuItem("SPZombies/Turn Selected Into/Power Light", false, 32)]
        [MenuItem("GameObject/SPZombies/Turn Into Power Light", false, 2)]
        private static void PowerLight() => Run(ZombiesKind.PowerLight);

        [MenuItem("SPZombies/Turn Selected Into/Lamp (Always On)", false, 33)]
        [MenuItem("GameObject/SPZombies/Turn Into Lamp (Always On)", false, 3)]
        private static void Lamp() => Run(ZombiesKind.Lamp);

        private static double _lastRun;

        private static void Run(ZombiesKind kind)
        {
            // The Hierarchy's right-click menu calls once per selected object: all of them are done on the first call
            if (EditorApplication.timeSinceStartup - _lastRun < 0.3)
                return;
            _lastRun = EditorApplication.timeSinceStartup;
            var said = new List<string>();
            foreach (GameObject go in Selection.gameObjects.ToList())
                said.Add(TurnInto(go, kind, new KitPlace.Options()));
            if (said.Count == 0)
                said.Add("Select an object of your map first (in the Scene view or the Hierarchy).");
            Debug.Log("[SPZombies Kit] " + string.Join("\n", said));
            EditorUtility.DisplayDialog("SPZombies Map Kit", string.Join("\n\n", said), "OK");
        }

        public static string TurnInto(GameObject obj, ZombiesKind kind, KitPlace.Options o)
        {
            string what = KitInfo.KindName(kind).ToLowerInvariant();
            if (obj == null)
                return $"Select an object of your map first (in the Scene view or the Hierarchy), then turn it into a {what}.";
            if (EditorUtility.IsPersistent(obj) || !obj.scene.IsValid())
                return $"{obj.name} is in the Project, not the scene: drag it into the scene first.";
            if (obj.GetComponentInParent<ZombiesMarker>() != null || obj.GetComponentInChildren<ZombiesMarker>(true) != null
                || obj.GetComponentInParent<ZombiesMapSettings>() != null
                || obj.transform.root.name == KitExport.LooksRootName || obj.transform.root.name == KitExport.KitRootName)
                return $"{obj.name} is one of the kit's own objects.";
            // A click often picks one detail piece of a model: the whole object is used
            LODGroup group = obj.GetComponentInParent<LODGroup>();
            string whole = "";
            if (group != null && group.gameObject != obj)
            {
                whole = $" (the whole object, not just its piece {obj.name})";
                obj = group.gameObject;
            }

            // Its size in its own turned frame, and its bottom middle: where the marker stands
            Quaternion rot = obj.transform.rotation;
            Vector3 origin = obj.transform.position;
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            var skip = new HashSet<Renderer>();
            foreach (LODGroup g in obj.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null)
                            skip.Add(r);
            }
            foreach (Renderer r in obj.GetComponentsInChildren<Renderer>(true))
            {
                if (skip.Contains(r) || !r.enabled || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                    continue;
                MeshFilter mf = r is MeshRenderer ? r.GetComponent<MeshFilter>() : null;
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                if (mesh == null)
                    continue;
                Bounds lb = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 local = Quaternion.Inverse(rot) * (r.transform.TransformPoint(corner) - origin);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }
            if (min.x > max.x)
                return $"{obj.name} has nothing to see (no meshes).";
            Vector3 size = max - min;
            Vector3 foot = origin + rot * new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);

            Scene scene = KitExport.MapScene();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Turn into " + what);
            var go = new GameObject(KitInfo.KindName(kind)) { tag = "EditorOnly" };
            go.transform.SetParent(KitExport.Folder(scene, KitExport.Category(kind)), false);
            go.transform.SetPositionAndRotation(foot, rot);
            go.transform.localScale = ZombiesMarker.IsSized(kind) ? Vector3.Max(size, Vector3.one * 0.05f) : Vector3.one;
            ZombiesMarker m = go.AddComponent<ZombiesMarker>();
            m.Kind = kind;
            m.What = "";
            m.Price = KitInfo.HasPrice(kind) ? (o.Price >= 0 ? o.Price : KitInfo.DefaultPrice(kind)) : 0;
            m.OpensWithPower = (kind == ZombiesKind.Door || kind == ZombiesKind.Debris) && o.Power;
            m.CustomLook = obj;
            m.OriginalTag = obj.tag;
            go.name = KitInfo.Label(m) + " - " + obj.name;
            Undo.RegisterCreatedObjectUndo(go, "Turn into " + what);

            // The original out of the map build: SPZombies' copy stands there in a match
            Undo.RecordObject(obj, "Turn into " + what);
            obj.tag = "EditorOnly";
            obj.SetActive(false);
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.MarkSceneDirty(obj.scene);
            Selection.activeGameObject = go;
            bool opens = kind == ZombiesKind.Door || kind == ZombiesKind.Debris;
            return $"{obj.name}{whole} is now a {what}{(opens ? $" ({m.Price} points)" : "")}. The original is switched off and kept out of the map build; " +
                   "the kit keeps a copy in the map for SPZombies when you Export." +
                   (opens ? " Bake the navmesh again so the way through is walkable: SPZombies shuts it until it's bought." : "");
        }

        /// <summary>Puts a turned-into object back as a plain map object and removes its marker.</summary>
        public static void PutBack(ZombiesMarker m)
        {
            GameObject obj = m.CustomLook;
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            if (obj != null)
            {
                Undo.RecordObject(obj, "Put the original back");
                obj.tag = string.IsNullOrEmpty(m.OriginalTag) || m.OriginalTag == "EditorOnly" ? "Untagged" : m.OriginalTag;
                obj.SetActive(true);
                Selection.activeGameObject = obj;
            }
            Undo.DestroyObjectImmediate(m.gameObject);
            Undo.CollapseUndoOperations(undo);
        }

        /// <summary>Whether the look's original is still in the map build (it would stay standing after the door is bought).</summary>
        public static bool OriginalStillBuilt(ZombiesMarker m)
        {
            if (m.CustomLook == null)
                return false;
            for (Transform t = m.CustomLook.transform; t != null; t = t.parent)
                if (t.gameObject.CompareTag("EditorOnly"))
                    return false;
            return m.CustomLook.activeInHierarchy;
        }

        /// <summary>Takes the look's original out of the map build (switched off, editor only).</summary>
        public static void TakeOut(ZombiesMarker m)
        {
            GameObject obj = m.CustomLook;
            if (obj == null)
                return;
            Undo.RecordObject(obj, "Take the original out of the build");
            Undo.RecordObject(m, "Take the original out of the build");
            if (string.IsNullOrEmpty(m.OriginalTag))
                m.OriginalTag = obj.tag;
            obj.tag = "EditorOnly";
            obj.SetActive(false);
            EditorSceneManager.MarkSceneDirty(obj.scene);
        }
    }
}
