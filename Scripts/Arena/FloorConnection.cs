using UnityEngine;
using Pathfinding;
using Starbelter.AI;

namespace Starbelter.Arena
{
    /// <summary>
    /// Connects two floors within an Arena (stairs, elevator, ladder, etc.)
    /// Creates a NodeLink2 for pathfinding across floors.
    /// </summary>
    public class FloorConnection : MonoBehaviour
    {
        [Header("Connection")]
        [Tooltip("The floor this connection starts on")]
        [SerializeField] private ArenaFloor fromFloor;

        [Tooltip("The floor this connection leads to")]
        [SerializeField] private ArenaFloor toFloor;

        [Tooltip("Position on the destination floor where units appear")]
        [SerializeField] private Transform exitPoint;

        [Header("Settings")]
        [Tooltip("Type of connection")]
        [SerializeField] private ConnectionType connectionType = ConnectionType.Stairs;

        [Tooltip("Time to traverse this connection")]
        [SerializeField] private float traversalTime = 1f;

        [Tooltip("Is this connection bidirectional?")]
        [SerializeField] private bool bidirectional = true;

        [Tooltip("Cost multiplier for pathfinding (higher = less preferred)")]
        [SerializeField] private float costMultiplier = 1f;

        // Runtime
        private Arena parentArena;
        private NodeLink2 nodeLink;

        // Properties
        public ArenaFloor FromFloor => fromFloor;
        public ArenaFloor ToFloor => toFloor;
        public ConnectionType Type => connectionType;
        public float TraversalTime => traversalTime;
        public bool IsBidirectional => bidirectional;

        public Vector3 EntryPosition => transform.position;
        public Vector3 ExitPosition => exitPoint != null ? exitPoint.position : transform.position;

        /// <summary>
        /// Initialize the connection. Called by parent Arena.
        /// </summary>
        public void Initialize(Arena arena)
        {
            parentArena = arena;

            // Auto-detect floors if not set
            if (fromFloor == null)
            {
                fromFloor = arena.GetFloorAtPosition(transform.position);
            }
            if (toFloor == null && exitPoint != null)
            {
                toFloor = arena.GetFloorAtPosition(exitPoint.position);
            }

            if (fromFloor == null || toFloor == null)
            {
                Debug.LogWarning($"[FloorConnection] Could not determine floors for connection at {transform.position}");
                return;
            }

            // Create NodeLink2 for pathfinding
            CreateNodeLink();

            Debug.Log($"[FloorConnection] Initialized {connectionType} from {fromFloor.FloorId} to {toFloor.FloorId}");
        }

        private void CreateNodeLink()
        {
            // Check if NodeLink2 already exists
            nodeLink = GetComponent<NodeLink2>();
            if (nodeLink == null)
            {
                nodeLink = gameObject.AddComponent<NodeLink2>();
            }

            // Configure the node link
            nodeLink.end = exitPoint;
            nodeLink.oneWay = !bidirectional;
            nodeLink.costFactor = costMultiplier;

            // The NodeLink2 will automatically connect nodes when the graphs are scanned
            // It finds the nearest walkable nodes at start and end positions
        }

        /// <summary>
        /// Transition a unit through this connection.
        /// </summary>
        public void TransitionUnit(UnitController unit)
        {
            if (unit == null || parentArena == null) return;

            // Move unit to exit position
            unit.transform.position = ExitPosition;

            // Update floor registration
            parentArena.SetUnitFloor(unit, toFloor);

            // Update tile occupancy
            toFloor.OccupyTile(unit.gameObject, ExitPosition);

            Debug.Log($"[FloorConnection] Unit '{unit.name}' moved from {fromFloor.FloorId} to {toFloor.FloorId}");
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Optional: Auto-transition when unit enters trigger
            // For now, require explicit TransitionUnit call or let pathfinding handle it
        }

        private void OnDestroy()
        {
            // NodeLink2 cleans itself up
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw connection
            Gizmos.color = connectionType switch
            {
                ConnectionType.Stairs => Color.green,
                ConnectionType.Elevator => Color.blue,
                ConnectionType.Ladder => Color.yellow,
                _ => Color.white
            };

            Gizmos.DrawWireSphere(transform.position, 0.5f);

            if (exitPoint != null)
            {
                Gizmos.DrawLine(transform.position, exitPoint.position);
                Gizmos.DrawWireSphere(exitPoint.position, 0.3f);

                // Draw arrow direction
                Vector3 dir = (exitPoint.position - transform.position).normalized;
                Vector3 midPoint = (transform.position + exitPoint.position) / 2f;
                Gizmos.DrawRay(midPoint, Quaternion.Euler(0, 0, 30) * -dir * 0.5f);
                Gizmos.DrawRay(midPoint, Quaternion.Euler(0, 0, -30) * -dir * 0.5f);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw floor bounds if assigned
            if (fromFloor != null)
            {
                Gizmos.color = new Color(0, 1, 0, 0.2f);
                Gizmos.DrawWireCube(fromFloor.Bounds.center, fromFloor.Bounds.size);
            }
            if (toFloor != null)
            {
                Gizmos.color = new Color(0, 0, 1, 0.2f);
                Gizmos.DrawWireCube(toFloor.Bounds.center, toFloor.Bounds.size);
            }
        }
#endif
    }

    public enum ConnectionType
    {
        Stairs,
        Elevator,
        Ladder,
        Ramp,
        Teleporter
    }
}
