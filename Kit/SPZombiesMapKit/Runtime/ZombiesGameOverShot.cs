using UnityEngine;

namespace SPZombies.MapKit
{
    /// <summary>
    /// A camera shot for the game over screen. SPZombies moves the camera itself: it stands here, looking the way this
    /// object looks (its blue arrow; tilt up or down too, no roll), and over 7 seconds drifts 2.5 m sideways (the line drawn
    /// across it) while it keeps looking at the first thing ahead (the yellow point; 12 m out when nothing is within 60 m).
    /// The shots play in order, top of the hierarchy first, and loop. With none placed, SPZombies makes its own shots.
    /// </summary>
    [AddComponentMenu("SPZombies/Zombies Game Over Shot")]
    public class ZombiesGameOverShot : MonoBehaviour
    {
        /// <summary>SPZombies' own numbers: how long a shot lasts and how far the camera drifts.</summary>
        public const float Seconds = 7f, Drift = 2.5f, LookReach = 60f, LookFallback = 12f;

        /// <summary>The way the camera looks (no roll).</summary>
        public Quaternion Look => Quaternion.Euler(transform.eulerAngles.x, transform.eulerAngles.y, 0f);

        /// <summary>Where the camera starts and ends its drift.</summary>
        public Vector3 From => transform.position - Look * Vector3.right * (Drift * 0.5f);
        public Vector3 To => transform.position + Look * Vector3.right * (Drift * 0.5f);

        /// <summary>What it keeps looking at: the first thing ahead, else 12 m out.</summary>
        public Vector3 Target =>
            Physics.Raycast(transform.position, Look * Vector3.forward, out RaycastHit hit, LookReach, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : transform.position + Look * Vector3.forward * LookFallback;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Vector3 target = Target;
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Look, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, 50f, 3f, 0.2f, 16f / 9f);
            Gizmos.matrix = Matrix4x4.identity;
            // The drift
            Gizmos.DrawLine(From, To);
            Gizmos.DrawSphere(From, 0.08f);
            Gizmos.DrawSphere(To, 0.14f);
            // What it looks at
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.6f);
            Gizmos.DrawLine(transform.position, target);
            Gizmos.DrawWireSphere(target, 0.3f);
        }
#endif
    }
}
