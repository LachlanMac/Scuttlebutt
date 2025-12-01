using UnityEngine;
using Pathfinding;
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
        private const float ARRIVAL_THRESHOLD = 0.05f; // 5cm threshold to avoid floating point jitter

        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5f;

        // Components
        private Seeker seeker;
        private Rigidbody2D rb;

        // Pathfinding state
        private Path currentPath;
        private int currentWaypoint;
        private bool isMoving;
        private Vector3 targetPosition;
        private Vector3Int targetTile;

        // Path request throttling
        private float lastPathRequestTime;
        private const float PATH_REQUEST_COOLDOWN = 1f;

        // Acceleration
        private float currentSpeed;
        private const float BASE_ACCELERATION_TIME = 1f; // Time to reach full speed at average agility

        // References
        private TileOccupancy tileOccupancy;
        private UnitController unitController;

        // Facing direction (for perception)
        private Vector2 facingDirection = Vector2.right;

        public bool IsMoving => isMoving;
        public Vector2 FacingDirection => facingDirection;

        /// <summary>
        /// Set facing direction toward a world position (e.g., when watching an enemy).
        /// </summary>
        public void FaceToward(Vector3 targetPosition)
        {
            Vector2 dir = (targetPosition - transform.position).normalized;
            if (dir.sqrMagnitude > 0.01f)
            {
                facingDirection = dir;
            }
        }
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
            unitController = GetComponent<UnitController>();
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
            if (!isMoving) return;

            FollowPath();
        }

        /// <summary>
        /// Moves to a specific tile position.
        /// </summary>
        /// <returns>True if movement was started, false if blocked or throttled</returns>
        public bool MoveToTile(Vector3Int tilePosition)
        {
            // Convert tile to world position for A* checks
            Vector3 worldPos = TileToWorld(tilePosition);

            // Check if we're already at this tile (avoid unnecessary path request)
            float distanceToTarget = Vector3.Distance(transform.position, worldPos);
            if (distanceToTarget <= ARRIVAL_THRESHOLD)
            {
                // Already here, no movement needed
                return false;
            }

            // Check if tile is walkable on A* graph
            GraphNode node = null;
            if (AstarPath.active != null)
            {
                node = AstarPath.active.GetNearest(worldPos).node;
            }
            if (node == null || !node.Walkable)
            {
                Debug.LogWarning($"[UnitMovement] {gameObject.name} tile {tilePosition} is not walkable");
                return false;
            }

            // Check if tile is occupied by another unit
            if (tileOccupancy != null && !tileOccupancy.IsTileAvailable(tilePosition, gameObject))
            {
                Debug.LogWarning($"[UnitMovement] {gameObject.name} tile {tilePosition} is occupied");
                return false;
            }

            targetTile = tilePosition;
            targetPosition = worldPos;

            // Request path - only reserve tile if request was accepted
            if (RequestPath(targetPosition))
            {
                if (tileOccupancy != null)
                {
                    tileOccupancy.ReserveTile(gameObject, tilePosition);
                }
                return true;
            }
            else
            {
                // Path request throttled - don't spam logs
                return false;
            }
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
        /// <returns>True if movement was started, false if blocked or throttled</returns>
        public bool MoveTo(Vector3 worldPosition)
        {
            if (tileOccupancy != null)
            {
                var tilePos = tileOccupancy.WorldToTile(worldPosition);
                return MoveToTile(tilePos);
            }
            else
            {
                targetPosition = worldPosition;
                return RequestPath(worldPosition);
            }
        }

        /// <summary>
        /// Moves to cover position relative to a threat.
        /// </summary>
        public bool MoveToCover(Vector3 threatPosition)
        {
            return MoveToCover(threatPosition, CoverSearchParams.Default, -1f);
        }

        /// <summary>
        /// Moves to cover position with tactical parameters.
        /// </summary>
        public bool MoveToCover(Vector3 threatPosition, CoverSearchParams searchParams, float maxDistance = -1f)
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null)
            {
                Debug.LogWarning("[UnitMovement] CoverQuery not available");
                return false;
            }

            // Pass this unit's gameObject to exclude from occupancy check
            var coverResult = coverQuery.FindBestCover(transform.position, threatPosition, searchParams, maxDistance, gameObject);

            if (coverResult.HasValue)
            {
                MoveToTile(coverResult.Value.TilePosition);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Stops movement at the nearest tile center (either current tile or next waypoint).
        /// Use this instead of Stop() when interrupting movement to ensure unit ends on a valid tile.
        /// </summary>
        public void StopAtNearestTile()
        {
            if (!isMoving || currentPath == null)
            {
                Stop();
                return;
            }

            // Find current tile center
            var currentTile = tileOccupancy != null
                ? tileOccupancy.WorldToTile(transform.position)
                : new Vector3Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y), 0);
            var currentTileCenter = tileOccupancy != null
                ? tileOccupancy.TileToWorld(currentTile)
                : new Vector3(currentTile.x, currentTile.y, 0);

            float distToCurrentTile = Vector3.Distance(transform.position, currentTileCenter);

            // Find next waypoint tile center (if we have waypoints remaining)
            Vector3 nextTileCenter = currentTileCenter;
            float distToNextTile = float.MaxValue;

            if (currentWaypoint < currentPath.vectorPath.Count)
            {
                var nextWaypointPos = currentPath.vectorPath[currentWaypoint];
                var nextTile = tileOccupancy != null
                    ? tileOccupancy.WorldToTile(nextWaypointPos)
                    : new Vector3Int(Mathf.RoundToInt(nextWaypointPos.x), Mathf.RoundToInt(nextWaypointPos.y), 0);
                nextTileCenter = tileOccupancy != null
                    ? tileOccupancy.TileToWorld(nextTile)
                    : new Vector3(nextTile.x, nextTile.y, 0);
                distToNextTile = Vector3.Distance(transform.position, nextTileCenter);
            }

            // Pick the closer tile
            Vector3 closestTileCenter;
            Vector3Int closestTile;
            if (distToCurrentTile <= distToNextTile)
            {
                closestTileCenter = currentTileCenter;
                closestTile = currentTile;
            }
            else
            {
                closestTileCenter = nextTileCenter;
                closestTile = tileOccupancy != null
                    ? tileOccupancy.WorldToTile(nextTileCenter)
                    : new Vector3Int(Mathf.RoundToInt(nextTileCenter.x), Mathf.RoundToInt(nextTileCenter.y), 0);
            }

            float distToClosest = Vector3.Distance(transform.position, closestTileCenter);

            // If very close, just snap
            if (distToClosest <= ARRIVAL_THRESHOLD)
            {
                transform.position = closestTileCenter;
                Stop();
                return;
            }

            // Otherwise, redirect to closest tile center
            Debug.Log($"[{gameObject.name}] StopAtNearestTile: Redirecting to {closestTile} (dist={distToClosest:F2})");
            targetPosition = closestTileCenter;
            targetTile = closestTile;

            // Clear the path waypoints - just go directly to tile center
            currentPath = null;

            // We'll let FollowPath handle moving to targetPosition
            // But we need a simple path, so create a minimal one
            isMoving = true;
        }

        /// <summary>
        /// Stops movement immediately.
        /// </summary>
        public void Stop()
        {
            // Log if we're stopping away from a tile center
            if (tileOccupancy != null)
            {
                var currentTile = tileOccupancy.WorldToTile(transform.position);
                var tileCenter = tileOccupancy.TileToWorld(currentTile);
                float distFromCenter = Vector3.Distance(transform.position, tileCenter);

                if (distFromCenter > ARRIVAL_THRESHOLD)
                {
                    // Get caller info for debugging
                    var stackTrace = new System.Diagnostics.StackTrace(1, true);
                    var callerFrame = stackTrace.GetFrame(0);
                    string callerInfo = callerFrame != null
                        ? $"{callerFrame.GetMethod()?.DeclaringType?.Name}.{callerFrame.GetMethod()?.Name}"
                        : "Unknown";

                    Debug.LogWarning($"[{gameObject.name}] STOP OFF-TILE: pos={transform.position}, tile={currentTile}, center={tileCenter}, dist={distFromCenter:F2}, caller={callerInfo}");
                }
            }

            isMoving = false;
            currentPath = null;
            currentSpeed = 0f;

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }

            // Clear reservation and occupy current tile
            if (tileOccupancy != null)
            {
                tileOccupancy.ClearReservation(gameObject);
                tileOccupancy.OccupyTile(gameObject, transform.position);
            }
        }

        /// <summary>
        /// Updates current speed with acceleration based on Agility stat.
        /// Higher agility = faster acceleration.
        /// </summary>
        private void UpdateSpeed()
        {
            // Get agility stat (default to 10 if no character)
            int agility = unitController?.Character?.Agility ?? 10;

            // Calculate acceleration rate based on agility
            // Agility 1 = 0.5x acceleration (2 seconds to full speed)
            // Agility 10 = 1x acceleration (1 second to full speed)
            // Agility 20 = 2x acceleration (0.5 seconds to full speed)
            float agilityMultiplier = 0.5f + (agility / 20f) * 1.5f;
            float acceleration = (moveSpeed / BASE_ACCELERATION_TIME) * agilityMultiplier;

            // Smoothly accelerate toward max speed
            currentSpeed = Mathf.MoveTowards(currentSpeed, moveSpeed, acceleration * Time.deltaTime);
        }

        private bool RequestPath(Vector3 destination)
        {
            // Throttle path requests to avoid spamming the pathfinder
            if (Time.time - lastPathRequestTime < PATH_REQUEST_COOLDOWN)
            {
                return false;
            }

            lastPathRequestTime = Time.time;
            isMoving = true; // Set immediately so states know we're about to move

            // Reset threat buckets when starting movement
            // Old threat directions are relative to our old position and become stale
            // We'll build up fresh threat data as we move to the new position
            if (unitController != null && unitController.PerceptionManager != null)
            {
                unitController.PerceptionManager.ResetThreats(0.01f);
                Debug.Log($"[{gameObject.name}] Movement started - reset threat buckets");
            }

            seeker.StartPath(transform.position, destination, OnPathComplete);
            return true;
        }

        private void OnPathComplete(Path p)
        {
            if (p.error)
            {
                Debug.LogWarning($"[UnitMovement] Path error: {p.errorLog}");
                // Clear movement state on path failure
                isMoving = false;
                currentPath = null;
                // Clear reservation since we can't reach the target
                if (tileOccupancy != null)
                {
                    tileOccupancy.ClearReservation(gameObject);
                }
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
            // If no path (e.g., from StopAtNearestTile), just go directly to targetPosition
            Vector3 currentTarget = targetPosition;
            if (currentPath != null && currentWaypoint < currentPath.vectorPath.Count)
            {
                currentTarget = currentPath.vectorPath[currentWaypoint];
            }

            Vector3 direction = (currentTarget - transform.position).normalized;
            float distanceToWaypoint = Vector3.Distance(transform.position, currentTarget);

            // Update facing direction
            if (direction.sqrMagnitude > 0.01f)
            {
                facingDirection = new Vector2(direction.x, direction.y).normalized;
            }

            // Apply acceleration based on Agility stat
            UpdateSpeed();
            float moveDistance = currentSpeed * Time.deltaTime;

            // Move using Rigidbody2D or direct transform
            if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                rb.linearVelocity = direction * currentSpeed;
            }
            else if (rb != null && rb.bodyType == RigidbodyType2D.Kinematic)
            {
                // Clamp to not overshoot target
                if (moveDistance >= distanceToTarget)
                {
                    rb.MovePosition(targetPosition);
                }
                else
                {
                    rb.MovePosition(transform.position + direction * moveDistance);
                }
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
            if (currentPath != null && distanceToWaypoint <= moveDistance && currentWaypoint < currentPath.vectorPath.Count)
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
