using UnityEngine;

namespace SPZombies.MapKit
{
    /// <summary>
    /// The kinds of thing a zombies map holds. The names are what SPZombies reads from the layout file: never rename one.
    /// </summary>
    public enum ZombiesKind
    {
        PlayerStart, ZombieSpawn, Window, BoxSpot, PerkMachine, PackAPunch, WallBuy, Door, Debris, Blocker, PlayAreaWall,
        Prop, PowerLight, PowerSwitch, Zone, Rock, Lightning, Lamp, Decal, ExfilActivator, ExfilHold, ExfilZone,
    }

    /// <summary>
    /// One thing of the zombies layout: a perk machine, a box spot, a barricade, a wall buy, a zombie spawn... Place it with
    /// the SPZombies Map Kit window (SPZombies > Map Kit). Where it stands is its spot (its feet), its blue arrow (forward) is
    /// the way it faces: out of a wall for a wall buy, the outside a window's zombies come from, the side a player uses a
    /// machine from. For a window, door, debris, blocker, zone or exfil area its Scale is its size in metres.
    /// The marker is editor only (tagged EditorOnly): it never goes into the map build. The kit writes every marker into
    /// the layout file SPZombies reads (Export), and shows the real model in the Scene view while you place it.
    /// </summary>
    [AddComponentMenu("SPZombies/Zombies Marker")]
    [DisallowMultipleComponent]
    [SelectionBase]
    public class ZombiesMarker : MonoBehaviour
    {
        [Tooltip("What this is.")]
        public ZombiesKind Kind = ZombiesKind.ZombieSpawn;

        [Tooltip("Which one: the perk, the wall buy's gun, the door or debris look, the rock or decal type.")]
        public string What = "";

        [Tooltip("Points to buy it (doors, debris, wall buys).")]
        public int Price;

        [Tooltip("Zombie spawns and windows: the zone they belong to (0: worked out from the zone it stands in). Zones: the zone's number.")]
        public int Zone;

        [Tooltip("Doors and debris: not bought, they open when the power comes on.")]
        public bool OpensWithPower;

        [Tooltip("Wall buys: the chalk straight on the wall, without the wooden board.")]
        public bool NoBoard;

        [Tooltip("Box spots: the mystery box always starts here (else a random spot).")]
        public bool BoxStartsHere;

        [Tooltip("Doors, debris and power lights: an object of your own map to use as the look (the kit keeps a copy of it in the map for SPZombies).")]
        public GameObject CustomLook;

        /// <summary>The custom look's own tag before the kit took it out of the map build (Turn into), to put it back.</summary>
        [HideInInspector] public string OriginalTag = "";

        /// <summary>
        /// Set by the kit's editor code: the space the preview model takes, in this marker's own space at scale 1 (used for
        /// the clickable box). Not saved.
        /// </summary>
        [System.NonSerialized] public Bounds PreviewBox;
        [System.NonSerialized] public bool HasPreviewBox;

        /// <summary>Kinds whose Scale is their size in metres (width, height, depth).</summary>
        public static bool IsSized(ZombiesKind k) =>
            k == ZombiesKind.Window || k == ZombiesKind.Door || k == ZombiesKind.Debris || k == ZombiesKind.Blocker
            || k == ZombiesKind.PlayAreaWall || k == ZombiesKind.Zone || k == ZombiesKind.ExfilHold || k == ZombiesKind.ExfilZone
            || k == ZombiesKind.Decal;

        /// <summary>Its size in metres (sized kinds): the object's scale.</summary>
        public Vector3 Size
        {
            get
            {
                Vector3 s = transform.lossyScale;
                return new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }
        }

#if UNITY_EDITOR
        /// <summary>Told when a marker's settings change in the Inspector (the kit rebuilds its preview).</summary>
        public static System.Action<ZombiesMarker> Edited;

        private void OnValidate()
        {
            if (Edited != null)
                Edited(this);
        }

        // A faint box the size of the thing: clicking it in the Scene view selects the marker
        private void OnDrawGizmos()
        {
            Bounds b = Box();
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 1f, 1f, 0.02f);
            Gizmos.DrawCube(b.center, b.size);
        }

        private void OnDrawGizmosSelected()
        {
            Bounds b = Box();
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);
        }

        private Bounds Box()
        {
            if (IsSized(Kind))
            {
                Vector3 s = Size;
                return new Bounds(new Vector3(0f, s.y * 0.5f, 0f), s);
            }
            if (HasPreviewBox)
                return PreviewBox;
            return new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 1.8f, 0.6f));
        }
#endif
    }
}
