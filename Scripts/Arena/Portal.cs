using UnityEngine;
using Starbelter.AI;

namespace Starbelter.Arena
{
    /// <summary>
    /// Connection point between two arenas.
    /// Units can transition through portals to move between arenas.
    /// Examples: Airlock, landing ramp, elevator.
    /// </summary>
    public class Portal : MonoBehaviour
    {
        [Header("Portal Identity")]
        [SerializeField] private string portalId;

        [Header("Connection")]
        [Tooltip("The arena this portal belongs to")]
        [SerializeField] private Arena ownerArena;

        [Tooltip("The floor this portal is on (auto-detected if not set)")]
        [SerializeField] private ArenaFloor ownerFloor;

        [Tooltip("The portal this connects to (in another arena)")]
        [SerializeField] private Portal connectedPortal;

        [Tooltip("Is this portal currently active/usable?")]
        [SerializeField] private bool isActive = true;

        [Header("Transition")]
        [Tooltip("Tile offset from portal position where units appear")]
        [SerializeField] private Vector3Int exitOffset = Vector3Int.zero;

        [Tooltip("Time to transition through portal (for animations)")]
        [SerializeField] private float transitionTime = 0.5f;

        [Header("Space Connection")]
        [Tooltip("If true, this portal connects to Space (hangar exit, etc.)")]
        [SerializeField] private bool connectsToSpace;

        [Tooltip("Offset from parent ship where vessels appear in space")]
        [SerializeField] private Vector2 spaceExitOffset;

        // Events
        public event System.Action<UnitController> OnUnitEntered;
        public event System.Action<UnitController> OnUnitExited;

        // Properties
        public string PortalId => portalId;
        public Arena OwnerArena => ownerArena;
        public ArenaFloor OwnerFloor => ownerFloor;
        public Portal ConnectedPortal => connectedPortal;
        public bool IsActive => isActive;
        public bool ConnectsToSpace => connectsToSpace;
        public Vector2 SpaceExitOffset => spaceExitOffset;
        public float TransitionTime => transitionTime;

        public Vector3 ExitPosition => ownerArena != null
            ? ownerArena.TileToWorld(ownerArena.WorldToTile(transform.position) + exitOffset)
            : transform.position + new Vector3(exitOffset.x, exitOffset.y, 0);

        private void Awake()
        {
            if (string.IsNullOrEmpty(portalId))
            {
                portalId = $"Portal_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
            }
        }

        /// <summary>
        /// Set the owner arena (called by Arena on initialization).
        /// Auto-detects the floor based on position if not already set.
        /// </summary>
        public void SetArena(Arena arena)
        {
            ownerArena = arena;

            // Auto-detect floor if not set
            if (ownerFloor == null && arena != null)
            {
                ownerFloor = arena.GetFloorAtPosition(transform.position);
            }
        }

        /// <summary>
        /// Connect this portal to another portal.
        /// </summary>
        public void ConnectTo(Portal other)
        {
            connectedPortal = other;
            if (other != null && other.connectedPortal != this)
            {
                other.connectedPortal = this;
            }
        }

        /// <summary>
        /// Disconnect this portal.
        /// </summary>
        public void Disconnect()
        {
            if (connectedPortal != null)
            {
                connectedPortal.connectedPortal = null;
                connectedPortal = null;
            }
        }

        /// <summary>
        /// Activate or deactivate the portal.
        /// </summary>
        public void SetActive(bool active)
        {
            isActive = active;
        }

        /// <summary>
        /// Attempt to transition a unit through this portal.
        /// </summary>
        public bool TryTransition(UnitController unit)
        {
            if (!isActive)
            {
                Debug.Log($"[Portal] Portal '{portalId}' is not active");
                return false;
            }

            if (connectsToSpace)
            {
                // Hand off to SpaceManager
                if (ArenaManager.Instance != null)
                {
                    ArenaManager.Instance.TransitionToSpace(unit, this);
                    OnUnitExited?.Invoke(unit);
                    return true;
                }
                return false;
            }

            if (connectedPortal == null)
            {
                Debug.Log($"[Portal] Portal '{portalId}' has no connection");
                return false;
            }

            if (connectedPortal.ownerArena == null)
            {
                Debug.Log($"[Portal] Connected portal has no arena");
                return false;
            }

            // Transition to connected arena
            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.TransitionUnit(unit, this, connectedPortal);
                OnUnitExited?.Invoke(unit);
                connectedPortal.OnUnitEntered?.Invoke(unit);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Get the world position where a unit should appear after transitioning TO this portal.
        /// </summary>
        public Vector3 GetArrivalPosition()
        {
            return ExitPosition;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Auto-transition when unit enters portal trigger
            var unit = other.GetComponentInParent<UnitController>();
            if (unit != null && isActive)
            {
                // Only auto-transition if unit is moving toward the portal
                // (prevents accidental transitions when just passing by)
                // For now, require explicit TryTransition call
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw portal
            Gizmos.color = isActive ? Color.cyan : Color.gray;
            Gizmos.DrawWireSphere(transform.position, 0.5f);

            // Draw exit position
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(ExitPosition, 0.3f);

            // Draw connection line
            if (connectedPortal != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, connectedPortal.transform.position);
            }

            // Draw space exit direction
            if (connectsToSpace)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawRay(transform.position, (Vector3)spaceExitOffset.normalized * 2f);
            }
        }
#endif
    }
}
