using UnityEngine;

namespace SPZombies.MapKit
{
    /// <summary>
    /// A walk-across link on the navmesh, added when a match starts: zombies path from this object's spot to End as if it
    /// were ground. For small gaps the navmesh bake leaves where pieces meet, or a drop through a hole in the floor
    /// (One Way: only from here to End). Both ends must be on the baked navmesh (within 1 m).
    /// </summary>
    [AddComponentMenu("SPZombies/Zombies Nav Link")]
    public class ZombiesNavLink : MonoBehaviour
    {
        [Tooltip("The far end, in this object's space.")]
        public Vector3 End = new Vector3(0f, 0f, 2f);

        [Min(0.1f), Tooltip("How wide the link is (metres).")]
        public float Width = 1f;

        [Tooltip("Only from this object's spot to End (a drop), never back.")]
        public bool OneWay;

        public Vector3 WorldEnd => transform.TransformPoint(End);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Vector3 a = transform.position, b = WorldEnd;
            Gizmos.color = OneWay ? new Color(1f, 0.6f, 0.1f) : new Color(0.2f, 0.9f, 1f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(a, 0.12f);
            Gizmos.DrawWireSphere(b, 0.12f);
        }
#endif
    }
}
