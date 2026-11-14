using System.Collections.Generic;
using UnityEngine;

namespace SPZombies.MapKit
{
    /// <summary>
    /// A zone drawn as an outline on the floor (its corners, in order, in this object's space), standing from Below under
    /// its lowest corner to Height over its highest. Whatever stands inside (zombie spawns, windows) belongs to the zone.
    /// Zones 1 and 2 are open from the start; any other zone opens when a door or debris touching it (within 2.5 m) is
    /// bought or cleared. Zombie spawns in a closed zone never spawn. A Zone marker (a box) does the same for simple rooms.
    /// </summary>
    [AddComponentMenu("SPZombies/Zombies Zone Outline")]
    [DisallowMultipleComponent]
    public class ZombiesZoneOutline : MonoBehaviour
    {
        [Min(1), Tooltip("The zone's number (1 and 2 are open from the start).")]
        public int Id = 1;

        [Min(0.5f), Tooltip("Metres over the highest corner.")]
        public float Height = 4f;

        [Min(0f), Tooltip("Metres under the lowest corner.")]
        public float Below = 1f;

        [Tooltip("The corners, in order round the outline, in this object's space.")]
        public List<Vector3> Corners = new List<Vector3>
        {
            new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, -5f), new Vector3(5f, 0f, 5f), new Vector3(-5f, 0f, 5f),
        };

        /// <summary>The corners in the world.</summary>
        public List<Vector3> WorldCorners()
        {
            var list = new List<Vector3>(Corners.Count);
            foreach (Vector3 c in Corners)
                list.Add(transform.TransformPoint(c));
            return list;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // A small clickable block at its middle
            List<Vector3> w = WorldCorners();
            if (w.Count == 0)
                return;
            Vector3 mid = Vector3.zero;
            foreach (Vector3 p in w)
                mid += p;
            mid /= w.Count;
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            Gizmos.DrawCube(mid + Vector3.up * 0.25f, new Vector3(0.5f, 0.5f, 0.5f));
        }
#endif
    }
}
