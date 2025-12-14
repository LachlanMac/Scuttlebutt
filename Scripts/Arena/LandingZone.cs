using UnityEngine;

namespace Starbelter.Arena
{
    /// <summary>
    /// Defines a landing zone where ships park after entering the hangar.
    /// Ships will navigate here and face the specified direction when parked.
    /// </summary>
    public class LandingZone : MonoBehaviour
    {
        public enum FacingDirection
        {
            North,  // Up (0 degrees)
            East,   // Right (-90 degrees)
            South,  // Down (180 degrees)
            West    // Left (90 degrees)
        }

        [Header("Settings")]
        [Tooltip("Direction the ship should face when parked")]
        [SerializeField] private FacingDirection parkedFacing = FacingDirection.North;

        [Tooltip("Is this landing zone currently occupied?")]
        [SerializeField] private bool isOccupied = false;

        // Properties
        public bool IsOccupied => isOccupied;
        public Vector3 Position => transform.position;

        /// <summary>
        /// Get the rotation for the parked facing direction.
        /// </summary>
        public float ParkedRotation => parkedFacing switch
        {
            FacingDirection.North => 0f,
            FacingDirection.East => -90f,
            FacingDirection.South => 180f,
            FacingDirection.West => 90f,
            _ => 0f
        };

        /// <summary>
        /// Mark this zone as occupied.
        /// </summary>
        public void SetOccupied(bool occupied)
        {
            isOccupied = occupied;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw the landing zone
            Gizmos.color = isOccupied ? Color.red : Color.green;
            Gizmos.DrawWireCube(transform.position, new Vector3(3f, 3f, 0f));

            // Draw facing direction arrow
            Gizmos.color = Color.cyan;
            Vector3 facingDir = parkedFacing switch
            {
                FacingDirection.North => Vector3.up,
                FacingDirection.East => Vector3.right,
                FacingDirection.South => Vector3.down,
                FacingDirection.West => Vector3.left,
                _ => Vector3.up
            };
            Gizmos.DrawRay(transform.position, facingDir * 1.5f);
        }
#endif
    }
}
