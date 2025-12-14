using UnityEngine;

namespace Starbelter.Space
{
    /// <summary>
    /// Marks an exit/entry point on a SpaceVessel where ships can launch from or land at.
    /// Place as a child of SpaceVessel at the position where fighters should appear/disappear.
    /// </summary>
    public class HangarExit : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique ID to match with HangarEntrance in the arena")]
        [SerializeField] private string exitId = "hangar_main";

        [Header("Approach")]
        [Tooltip("Transform where ships fly to before final approach. Once reached, ship parents to mothership.")]
        [SerializeField] private Transform approachVector;

        // Properties
        public string ExitId => exitId;
        public Transform ApproachVector => approachVector;

        /// <summary>
        /// Get the world position of this exit point (where ships enter/exit the hangar).
        /// </summary>
        public Vector2 Position => transform.position;

        /// <summary>
        /// Get the world position where ships should start their landing approach.
        /// </summary>
        public Vector2 ApproachPosition => approachVector != null
            ? (Vector2)approachVector.position
            : Position + Vector2.right * 50f; // Fallback

        /// <summary>
        /// Get the rotation ships should have when exiting (facing away from hangar).
        /// </summary>
        public float ExitRotation
        {
            get
            {
                if (approachVector == null) return 0f;
                Vector2 dir = (ApproachPosition - Position).normalized;
                return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            }
        }

        /// <summary>
        /// Get the rotation ships should have when approaching (facing toward hangar).
        /// </summary>
        public float ApproachRotation
        {
            get
            {
                if (approachVector == null) return 0f;
                Vector2 dir = (Position - ApproachPosition).normalized;
                return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw exit point (hangar door)
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 2f);

            // Draw approach vector and line
            if (approachVector != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(approachVector.position, 1.5f);

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, approachVector.position);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw exit ID label
            UnityEditor.Handles.Label(transform.position + Vector3.up * 3f, exitId);

            if (approachVector != null)
            {
                UnityEditor.Handles.Label(approachVector.position + Vector3.up * 2f, "Approach");
            }
        }
#endif
    }
}
