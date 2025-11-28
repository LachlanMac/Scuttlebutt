using UnityEngine;
using Pathfinding;
using Pathfinding.RVO;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Handles unit movement using A* Pathfinding Pro.
    /// Integrates with TileOccupancy for tile-based positioning.
    /// Uses RVO for real-time local avoidance.
    /// </summary>
    [RequireComponent(typeof(Seeker))]
    public class UnitMovement : MonoBehaviour
    {
        private const float ARRIVAL_THRESHOLD = 0.001f;

        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5f;

        // Components
        private Seeker seeker;
        private Rigidbody2D rb;
        private RVOController rvoController;

        // Pathfinding state
        private Path currentPath;
        private int currentWaypoint;
        private bool isMoving;
        private Vector3 targetPosition;
        private Vector3Int targetTile;

        // References
        private TileOccupancy tileOccupancy;

        public bool IsMoving => isMoving;
        public Vector3 TargetPosition => targetPosition;
        public Vector3Int TargetTile => targetTile;
        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = value;
        }

        private void Awake()
        {
            seeker = GetComponent<Seeker>();
            rb = GetComponent<Rigidbody2D>();
            rvoController = GetComponent<RVOController>();
        }

        private void Start()
        {
            tileOccupancy = TileOccupancy.Instance;

            // Register initial position
            if (tileOccupancy != null)
            {
                tileOccupancy.OccupyTile(gameObject, transform.position);
            }
        }

        private void Update()
        {
            if (!isMoving || currentPath == null) return;

            FollowPath();
        }

        /// <summary>
        /// Moves to a specific tile position.
        /// </summary>
        public void MoveToTile(Vector3Int tilePosition)
        {
            // Convert tile to world position for A* checks
            Vector3 worldPos = TileToWorld(tilePosition);

            // Check if tile is walkable on A* graph
            GraphNode node = null;
            if (AstarPath.active != null)
            {
                node = AstarPath.active.GetNearest(worldPos).node;
            }
            if (node == null || !node.Walkable)
            {
                Debug.LogWarning($"[UnitMovement] Tile {tilePosition} is not walkable");
                return;
            }

            // Check if tile is occupied by another unit
            if (tileOccupancy != null && !tileOccupancy.IsTileAvailable(tilePosition, gameObject))
            {
                Debug.LogWarning($"[UnitMovement] Tile {tilePosition} is occupied");
                return;
            }

            // Reserve the target tile
            if (tileOccupancy != null)
            {
                tileOccupancy.ReserveTile(gameObject, tilePosition);
            }

            targetTile = tilePosition;
            targetPosition = worldPos;

            RequestPath(targetPosition);
        }

        private Vector3 TileToWorld(Vector3Int tilePosition)
        {
            if (tileOccupancy != null)
            {
                return tileOccupancy.TileToWorld(tilePosition);
            }
            // Fallback: assume 1x1 tiles centered at integers
            return new Vector3(tilePosition.x, tilePosition.y, 0);
        }

        /// <summary>
        /// Moves to a world position. Converts to tile position.
        /// </summary>
        public void MoveTo(Vector3 worldPosition)
        {
            if (tileOccupancy != null)
            {
                var tilePos = tileOccupancy.WorldToTile(worldPosition);
                MoveToTile(tilePos);
            }
            else
            {
                targetPosition = worldPosition;
                RequestPath(worldPosition);
            }
        }

        /// <summary>
        /// Moves to cover position relative to a threat.
        /// </summary>
        public bool MoveToCover(Vector3 threatPosition)
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null)
            {
                Debug.LogWarning("[UnitMovement] CoverQuery not available");
                return false;
            }

            var coverResult = coverQuery.FindBestCover(transform.position, threatPosition);

            if (coverResult.HasValue)
            {
                MoveToTile(coverResult.Value.TilePosition);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Stops movement immediately.
        /// </summary>
        public void Stop()
        {
            isMoving = false;
            currentPath = null;

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            if (rvoController != null)
            {
                rvoController.Move(Vector3.zero);
            }

            // Clear reservation and occupy current tile
            if (tileOccupancy != null)
            {
                tileOccupancy.ClearReservation(gameObject);
                tileOccupancy.OccupyTile(gameObject, transform.position);
            }
        }

        private void RequestPath(Vector3 destination)
        {
            seeker.StartPath(transform.position, destination, OnPathComplete);
        }

        private void OnPathComplete(Path p)
        {
            if (p.error)
            {
                Debug.LogWarning($"[UnitMovement] Path error: {p.errorLog}");
                return;
            }

            currentPath = p;
            currentWaypoint = 0;
            isMoving = true;
        }

        private void FollowPath()
        {
            // Check if we've arrived at final destination
            float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
            if (distanceToTarget <= ARRIVAL_THRESHOLD)
            {
                OnReachedDestination();
                return;
            }

            // Get current waypoint or final target
            Vector3 currentTarget = currentWaypoint < currentPath.vectorPath.Count
                ? currentPath.vectorPath[currentWaypoint]
                : targetPosition;

            Vector3 direction = (currentTarget - transform.position).normalized;
            float distanceToWaypoint = Vector3.Distance(transform.position, currentTarget);

            // Calculate movement this frame
            float moveDistance = moveSpeed * Time.deltaTime;

            // Move using RVO if available, otherwise direct movement
            if (rvoController != null)
            {
                rvoController.SetTarget(currentTarget, moveSpeed, moveSpeed * 1.2f, targetPosition);
                Vector3 rvoVelocity = rvoController.velocity;
                transform.position += rvoVelocity * Time.deltaTime;

                if (rvoVelocity.sqrMagnitude > 0.01f)
                {
                    direction = rvoVelocity.normalized;
                }
            }
            else if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                rb.linearVelocity = direction * moveSpeed;
            }
            else if (rb != null && rb.bodyType == RigidbodyType2D.Kinematic)
            {
                rb.MovePosition(transform.position + direction * moveDistance);
            }
            else
            {
                // Direct transform movement - clamp to not overshoot
                if (moveDistance >= distanceToTarget)
                {
                    transform.position = targetPosition;
                }
                else
                {
                    transform.position += direction * moveDistance;
                }
            }

            // Advance to next waypoint when close enough
            if (distanceToWaypoint <= moveDistance && currentWaypoint < currentPath.vectorPath.Count)
            {
                currentWaypoint++;
            }
        }

        private void OnReachedDestination()
        {
            // Snap to exact tile position
            transform.position = targetPosition;

            Stop();

            // Update occupancy
            if (tileOccupancy != null)
            {
                tileOccupancy.OccupyTile(gameObject, targetTile);
            }
        }

        private void OnDestroy()
        {
            // Release tile when unit is destroyed
            if (tileOccupancy != null)
            {
                tileOccupancy.ReleaseTile(gameObject);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (currentPath == null || !isMoving) return;

            Gizmos.color = Color.cyan;
            for (int i = currentWaypoint; i < currentPath.vectorPath.Count - 1; i++)
            {
                Gizmos.DrawLine(currentPath.vectorPath[i], currentPath.vectorPath[i + 1]);
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(targetPosition, 0.3f);
        }
#endif
    }
}
