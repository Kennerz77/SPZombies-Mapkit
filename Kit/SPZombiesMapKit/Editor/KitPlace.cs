using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SPZombies.MapKit.Editor
{
    /// <summary>What can be placed (by tab), its picture, and placing it where the Scene view looks.</summary>
    internal static class KitPlace
    {
        internal class Item
        {
            public string Tab, Label, What = "";
            public ZombiesKind Kind;
            public Type KitComponent;       // a kit-only thing (zone outline, nav link, game over shot), else a marker
            public string Bundle, Asset;    // its model in SPZombies' bundles
            public Func<string> Picture;    // or a picture file (wall buy chalk, decal)
            public bool Custom;             // a door or debris with a look of the map's own
            public Texture2D Thumb;
            public bool ThumbTried;
        }

        public static readonly string[] Tabs = { "Machines", "Barricades", "Doors", "Debris", "Wall buys", "Spawns", "Zones", "Power + exfil", "Extras" };

        private static List<Item> _items;
        private static int _generation = -1;

        public static List<Item> Items()
        {
            if (_items != null && _generation == KitModels.Generation)
                return _items;
            Forget();
            _generation = KitModels.Generation;
            var list = new List<Item>();
            foreach (string id in KitInfo.PerkIds)
                list.Add(new Item { Tab = "Machines", Label = KitInfo.PerkName(id), Kind = ZombiesKind.PerkMachine, What = id, Bundle = KitModels.PerksBundle, Asset = "perk_" + id });
            list.Add(new Item { Tab = "Machines", Label = "Pack-a-Punch", Kind = ZombiesKind.PackAPunch, Bundle = KitModels.PapBundle, Asset = "pap_machine" });
            list.Add(new Item { Tab = "Machines", Label = "Mystery box spot", Kind = ZombiesKind.BoxSpot, Bundle = KitModels.ZombiesBundle, Asset = "zm_box" });

            list.Add(new Item { Tab = "Barricades", Label = "Barricade (window)", Kind = ZombiesKind.Window, Bundle = KitModels.ZombiesBundle, Asset = "zm_window" });

            foreach (string look in KitModels.Offered("zm_door_", KitInfo.DoorLooks))
                list.Add(new Item { Tab = "Doors", Label = KitInfo.Nice(look), Kind = ZombiesKind.Door, What = look, Bundle = KitModels.ZombiesBundle, Asset = "zm_door_" + look });
            list.Add(new Item { Tab = "Doors", Label = "Your own door (turn the selected object into one)", Kind = ZombiesKind.Door, Custom = true });
            foreach (string look in KitModels.Offered("zm_debris_", KitInfo.DebrisLooks))
                list.Add(new Item { Tab = "Debris", Label = KitInfo.Nice(look), Kind = ZombiesKind.Debris, What = look, Bundle = KitModels.ZombiesBundle, Asset = "zm_debris_" + look });
            list.Add(new Item { Tab = "Debris", Label = "Your own debris (turn the selected object into it)", Kind = ZombiesKind.Debris, Custom = true });

            foreach (KitInstall.Gun g in KitInstall.Guns())
            {
                string id = g.Id;
                list.Add(new Item { Tab = "Wall buys", Label = g.Name + (g.Role == "wall" ? "" : $" ({g.Role})"), Kind = ZombiesKind.WallBuy, What = id, Picture = () => KitInstall.ChalkPicture(id) });
            }

            list.Add(new Item { Tab = "Spawns", Label = "Zombie spawn", Kind = ZombiesKind.ZombieSpawn });
            list.Add(new Item { Tab = "Spawns", Label = "Player start", Kind = ZombiesKind.PlayerStart });

            list.Add(new Item { Tab = "Zones", Label = "Zone (box)", Kind = ZombiesKind.Zone });
            list.Add(new Item { Tab = "Zones", Label = "Zone (outline)", KitComponent = typeof(ZombiesZoneOutline) });
            list.Add(new Item { Tab = "Zones", Label = "Blocker (invisible wall)", Kind = ZombiesKind.Blocker });
            list.Add(new Item { Tab = "Zones", Label = "Play area wall", Kind = ZombiesKind.PlayAreaWall });
            list.Add(new Item { Tab = "Zones", Label = "Nav link", KitComponent = typeof(ZombiesNavLink) });

            list.Add(new Item { Tab = "Power + exfil", Label = "Power switch", Kind = ZombiesKind.PowerSwitch, Bundle = KitModels.ZombiesBundle, Asset = "zm_power_switch" });
            list.Add(new Item { Tab = "Power + exfil", Label = "Power light", Kind = ZombiesKind.PowerLight });
            list.Add(new Item { Tab = "Power + exfil", Label = "Your own power light (turn the selected fixture into one)", Kind = ZombiesKind.PowerLight, Custom = true });
            list.Add(new Item { Tab = "Power + exfil", Label = "Exfil lever", Kind = ZombiesKind.ExfilActivator, Bundle = KitModels.ZombiesBundle, Asset = "zm_power_switch" });
            list.Add(new Item { Tab = "Power + exfil", Label = "Exfil hold area", Kind = ZombiesKind.ExfilHold });
            list.Add(new Item { Tab = "Power + exfil", Label = "Exfil exit zone", Kind = ZombiesKind.ExfilZone });

            list.Add(new Item { Tab = "Extras", Label = "115 rock (easter egg)", Kind = ZombiesKind.Rock, What = "easter_egg", Bundle = KitModels.ZombiesBundle, Asset = "zm_rock_115" });
            list.Add(new Item { Tab = "Extras", Label = "115 rock (lore)", Kind = ZombiesKind.Rock, What = "lore", Bundle = KitModels.ZombiesBundle, Asset = "zm_rock_115" });
            list.Add(new Item { Tab = "Extras", Label = "Lightning (crackle)", Kind = ZombiesKind.Lightning, What = "crackle" });
            list.Add(new Item { Tab = "Extras", Label = "Lightning (storm)", Kind = ZombiesKind.Lightning, What = "storm" });
            list.Add(new Item { Tab = "Extras", Label = "Lamp (always on)", Kind = ZombiesKind.Lamp });
            list.Add(new Item { Tab = "Extras", Label = "Your own lamp (turn the selected fixture into one)", Kind = ZombiesKind.Lamp, Custom = true });
            list.Add(new Item { Tab = "Extras", Label = "Game over shot", KitComponent = typeof(ZombiesGameOverShot) });
            foreach (string d in KitInstall.DecalNames())
            {
                string name = d;
                list.Add(new Item { Tab = "Extras", Label = "Decal: " + KitInfo.Nice(d), Kind = ZombiesKind.Decal, What = d, Picture = () => KitInstall.DecalPicture(name) });
            }
            _items = list;
            return _items;
        }

        public static void Forget()
        {
            if (_items != null)
                foreach (Item i in _items)
                    if (i.Thumb != null)
                        Object.DestroyImmediate(i.Thumb);
            _items = null;
        }

        // ---------- Pictures ----------

        public static Texture2D Thumb(Item item)
        {
            if (item.Thumb != null || item.ThumbTried)
                return item.Thumb;
            item.ThumbTried = true;
            if (item.Kind == ZombiesKind.WallBuy && item.KitComponent == null)
                item.Thumb = KitInstall.LoadChalk(item.What);
            else if (item.Picture != null)
                item.Thumb = KitInstall.LoadPicture(item.Picture());
            else if (item.Asset != null)
            {
                GameObject prefab = KitModels.Prefab(item.Bundle, item.Asset);
                if (prefab != null)
                    item.Thumb = Render(prefab);
            }
            return item.Thumb;
        }

        // A picture of a model on a grey background
        private static Texture2D Render(GameObject model)
        {
            var pru = new PreviewRenderUtility();
            GameObject go = null;
            try
            {
                go = (GameObject)Object.Instantiate(model);
                go.hideFlags = HideFlags.HideAndDontSave;
                KitPreviews.Strip(go);
                pru.AddSingleGO(go);
                Bounds b = KitPreviews.VisibleBounds(go);
                if (b.size == Vector3.zero)
                    return null;
                float radius = b.extents.magnitude;
                pru.camera.transform.position = b.center + new Vector3(0.7f, 0.5f, 1f).normalized * radius * 2.6f;
                pru.camera.transform.LookAt(b.center);
                pru.camera.nearClipPlane = radius * 0.05f;
                pru.camera.farClipPlane = radius * 10f;
                pru.camera.backgroundColor = new Color(0.32f, 0.34f, 0.37f);
                pru.camera.clearFlags = CameraClearFlags.SolidColor;
                pru.lights[0].intensity = 1.2f;
                pru.lights[0].transform.rotation = Quaternion.Euler(40f, 220f, 0f);
                pru.ambientColor = new Color(0.45f, 0.45f, 0.5f);
                pru.BeginStaticPreview(new Rect(0, 0, 192, 192));
                pru.camera.Render();
                Texture2D t = pru.EndStaticPreview();
                t.hideFlags = HideFlags.HideAndDontSave;
                return t;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SPZombies Kit] Picture of {model.name}: {e.Message}");
                return null;
            }
            finally
            {
                pru.Cleanup();
                if (go != null)
                    Object.DestroyImmediate(go);
            }
        }

        // ---------- Placing ----------

        internal class Options
        {
            public int Price = -1, Zone;
            public bool Power, NoBoard;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// <summary>Places the thing where the Scene view's centre points (on a wall for wall things), facing you. Returns what it did.</summary>
        public static string Place(Item item, Options o)
        {
            // Your own door, debris or light: the selected object of the map is turned into it
            if (item.Custom)
                return KitTurnInto.TurnInto(Selection.activeGameObject, item.Kind, o);
            Scene scene = KitExport.MapScene();
            if (!scene.IsValid() || !scene.isLoaded)
                return "Open the map's scene first.";
            SceneView view = SceneView.lastActiveSceneView;
            Camera cam = view != null ? view.camera : null;
            Vector3 at = view != null ? view.pivot : Vector3.zero;
            Quaternion rot = Quaternion.identity;
            bool wall = item.KitComponent == null && KitInfo.OnWall(item.Kind);
            if (cam != null)
            {
                Physics.SyncTransforms();
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                if (Physics.Raycast(ray, out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                {
                    at = hit.point;
                    if (wall)
                        rot = Quaternion.LookRotation(Flat(hit.normal), Vector3.up);
                    else if (hit.normal.y < 0.5f)
                        at = hit.point + hit.normal * 0.6f;
                }
                if (!wall)
                    rot = Quaternion.LookRotation(Flat(cam.transform.position - at), Vector3.up);
            }
            var go = new GameObject(item.Label) { tag = "EditorOnly" };
            go.transform.SetParent(KitExport.Folder(scene, item.KitComponent != null ? (item.KitComponent == typeof(ZombiesGameOverShot) ? "Extras" : "Zones and walls") : KitExport.Category(item.Kind)), false);
            go.transform.SetPositionAndRotation(at, rot);
            Undo.RegisterCreatedObjectUndo(go, "Place " + item.Label);

            if (item.KitComponent == typeof(ZombiesGameOverShot) && cam != null)
                // The shot is what the Scene view sees now
                go.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
            else if (!wall)
                DropToGround(go.transform, false);

            if (item.KitComponent != null)
            {
                Component c = go.AddComponent(item.KitComponent);
                if (c is ZombiesZoneOutline z)
                    z.Id = Mathf.Max(1, o.Zone);
                Selection.activeGameObject = go;
                EditorSceneManager.MarkSceneDirty(scene);
                return $"Placed {item.Label}.";
            }

            ZombiesMarker m = go.AddComponent<ZombiesMarker>();
            m.Kind = item.Kind;
            m.What = item.What ?? "";
            m.Price = KitInfo.HasPrice(item.Kind) ? (o.Price >= 0 ? o.Price : KitInfo.DefaultPrice(item.Kind)) : 0;
            m.Zone = KitInfo.HasZone(item.Kind) ? (item.Kind == ZombiesKind.Zone ? Mathf.Max(1, o.Zone) : o.Zone) : 0;
            m.OpensWithPower = (item.Kind == ZombiesKind.Door || item.Kind == ZombiesKind.Debris) && o.Power;
            m.NoBoard = item.Kind == ZombiesKind.WallBuy && o.NoBoard;
            if (item.Custom)
                m.What = "";
            if (ZombiesMarker.IsSized(item.Kind))
            {
                Vector3 size = KitInfo.DefaultSize(item.Kind);
                if (item.Kind == ZombiesKind.Decal)
                {
                    Texture2D pic = Thumb(item);
                    float aspect = pic != null && pic.width > 0 ? (float)pic.height / pic.width : 1f;
                    size = new Vector3(0.6f, 0.6f * aspect, 1f);
                }
                go.transform.localScale = size;
            }
            go.name = KitInfo.Label(m);
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(scene);
            string tip = item.Custom ? " Now drag an object of your map into its Custom look."
                : item.Kind == ZombiesKind.Window ? " Its red arrow points into the building; scale it to the opening."
                : ZombiesMarker.IsSized(item.Kind) ? " Its Scale is its size in metres." : "";
            return $"Placed {go.name}.{tip}";
        }

        /// <summary>Puts it on whatever is under it.</summary>
        public static void DropToGround(Transform t, bool undo = true)
        {
            Physics.SyncTransforms();
            Vector3 p = t.position;
            if (!Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                return;
            if (undo)
                Undo.RecordObject(t, "Drop to ground");
            t.position = new Vector3(p.x, hit.point.y, p.z);
        }
    }
}
