using UnityEngine;
using Pathfinding;
using Starbelter.Pathfinding;
using Starbelter.Core;
using Starbelter.Arena;

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
        private float speedMultiplier = 1f;

        // Components
        private Seeker seeker;
        private Rigidbody2D rb;

        // Pathfinding state
        private Path currentPath;
        private int currentWaypoint;
        private bool isMoving;
        private Vector3 targetPosition;
        private Vector3Int targetTile;
        private bool isCrossFloorPath; // True when path goes through elevator/stairs

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
        public bool IsCrossFloorPath => isCrossFloorPath;
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

        /// <summary>
        /// Speed multiplier for combat movement, etc. Default is 1.0.
        /// </summary>
        public float SpeedMultiplier
        {
            get => speedMultiplier;
            set => speedMultiplier = Mathf.Clamp(value, 0.1f, 2f);
        }

        /// <summary>
        /// Get the effective move speed (base speed * multiplier).
        /// </summary>
        public float EffectiveSpeed => moveSpeed * speedMultiplier;

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

            // Check if tile is walkable on A* graph (using current floor's graph)
            GraphNode node = null;
            if (AstarPath.active != null)
            {
                var arena = unitController?.CurrentArena;
                if (arena != null && arena.FloorCount > 0)
                {
                    // Use ONLY the current floor's graph
                    var currentFloor = arena.GetFloorForUnit(unitController);
                    var constraint = NearestNodeConstraint.Walkable;
                    if (currentFloor != null && currentFloor.Graph != null)
                    {
                        constraint.graphMask = currentFloor.GetGraphMaskStruct();
                    }
                    else
                    {
                        constraint.graphMask = arena.GetCombinedGraphMask();
                    }
                    node = AstarPath.active.GetNearest(worldPos, constraint).node;
                }
                else
                {
                    node = AstarPath.active.GetNearest(worldPos).node;
                }
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
            // Fallback: assume 1x1 tiles centered at 0.5 offset
            return new Vector3(tilePosition.x + 0.5f, tilePosition.y + 0.5f, 0);
        }

        /// <summary>
        /// Moves to a world position. Converts to tile position.
        /// </summary>
        /// <returns>True if movement was started, false if blocked or throttled</returns>
        public bool MoveTo(Vector3 worldPosition)
        {
            return MoveTo(worldPosition, false);
        }

        /// <summary>
        /// Moves to a world position with optional cross-floor pathfinding.
        /// </summary>
        /// <param name="worldPosition">Target position</param>
        /// <param name="crossFloor">If true, uses combined graph mask for multi-floor pathing via elevators</param>
        /// <returns>True if movement was started, false if blocked or throttled</returns>
        public bool MoveTo(Vector3 worldPosition, bool crossFloor)
        {
            // SANITY CHECK: Reject (0,0,0) destinations unless we're actually near origin
            if (worldPosition.sqrMagnitude < 0.01f && transform.position.sqrMagnitude > 1f)
            {
                Debug.LogError($"[{gameObject.name}] MoveTo REJECTING (0,0,0) destination! Current pos={transform.position}");
                return false;
            }

            isCrossFloorPath = crossFloor;

            if (tileOccupancy != null && !crossFloor)
            {
                var tilePos = tileOccupancy.WorldToTile(worldPosition);
                return MoveToTile(tilePos);
            }
            else
            {
                // Cross-floor or no tile occupancy - request path directly
                return RequestPath(worldPosition, crossFloor);
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
        /// Request a path to destination.
        /// TODO: Implement threat-aware routing using newer A* Pathfinding Pro API.
        /// For now, threat is factored into destination selection (WHERE) not routing (HOW).
        /// </summary>
        public bool RequestThreatAwarePath(Vector3 destination, Team myTeam)
        {
            // For now, use regular pathfinding via MoveTo which properly sets targetPosition
            // Threat awareness is handled in destination selection (CombatUtils.FindFightingPosition)
            return MoveTo(destination);
        }

        /// <summary>
        /// Called after an elevator/stair transition teleports the unit.
        /// Advances the path waypoint to find the next valid node near the new position.
        /// </summary>
        public void OnFloorTransition(ArenaFloor newFloor)
        {
            if (currentPath == null || currentPath.path == null)
            {
                Debug.LogWarning($"[{gameObject.name}] OnFloorTransition: No current path");
                return;
            }

            // Find the waypoint closest to our new position on the new floor
            Vector3 currentPos = transform.position;
            float closestDist = float.MaxValue;
            int closestIndex = currentWaypoint;

            for (int i = currentWaypoint; i < currentPath.path.Count; i++)
            {
                Vector3 nodePos = (Vector3)currentPath.path[i].position;
                float dist = Vector3.Distance(currentPos, nodePos);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestIndex = i;
                }

                // If we're close enough to this node, use it
                if (dist < 1f)
                {
                    currentWaypoint = i;
                    Debug.Log($"[{gameObject.name}] OnFloorTransition: Resuming at waypoint {i}, dist={dist:F2}");
                    return;
                }
            }

            // Use the closest node we found
            currentWaypoint = closestIndex;
            Debug.Log($"[{gameObject.name}] OnFloorTransition: Resuming at waypoint {closestIndex}, dist={closestDist:F2}");
        }

        /// <summary>
        /// Handle transitioning between floors when path crosses graphs.
        /// Teleports unit to the target floor and continues path.
        /// </summary>
        private void HandleFloorTransition(global::Pathfinding.GraphNode targetNode)
        {
            var arena = unitController?.CurrentArena;
            if (arena == null)
            {
                Debug.LogError($"[{gameObject.name}] HandleFloorTransition: No arena!");
                return;
            }

            // Find which floor the target node belongs to
            ArenaFloor targetFloor = null;
            foreach (var floor in arena.Floors)
            {
                if (floor.Graph == targetNode.Graph)
                {
                    targetFloor = floor;
                    break;
                }
            }

            if (targetFloor == null)
            {
                Debug.LogError($"[{gameObject.name}] HandleFloorTransition: Could not find floor for target graph!");
                return;
            }

            var currentFloor = arena.GetFloorForUnit(unitController);
            Debug.Log($"[{gameObject.name}] FLOOR TRANSITION: {currentFloor?.FloorId} -> {targetFloor.FloorId}");

            // Teleport to the target node position (elevator exit on target floor)
            Vector3 targetPos = (Vector3)targetNode.position;
            transform.position = targetPos;

            // Update floor registration
            arena.SetUnitFloor(unitController, targetFloor);

            // Change unit's layer to target floor's layer
            int targetLayer = targetFloor.Layer;
            if (targetLayer >= 0)
            {
                SetLayerRecursive(gameObject, targetLayer);
                Debug.Log($"[{gameObject.name}] Changed layer to {LayerMask.LayerToName(targetLayer)}");
            }

            // Advance waypoint to the target node (which we just teleported to)
            currentWaypoint++;

            // Update tile occupancy on new floor
            if (tileOccupancy != null)
            {
                tileOccupancy.OccupyTile(gameObject, targetPos);
            }
        }

        private void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Stops movement at the nearest tile center (either current tile or next waypoint).
        /// Use this instead of Stop() when interrupting movement to ensure unit ends on a valid tile.
        /// </summary>
        public void StopAtNearestTile()
        {
            // Find current tile center
            var currentTile = tileOccupancy != null
                ? tileOccupancy.WorldToTile(transform.position)
                : new Vector3Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y), 0);
            var currentTileCenter = tileOccupancy != null
                ? tileOccupancy.TileToWorld(currentTile)
                : new Vector3(currentTile.x, currentTile.y, 0);

            float distToCurrentTile = Vector3.Distance(transform.position, currentTileCenter);

            // Find next waypoint tile center (if we have a path with waypoints remaining)
            Vector3 nextTileCenter = currentTileCenter;
            float distToNextTile = float.MaxValue;

            if (currentPath != null && currentWaypoint < currentPath.vectorPath.Count)
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

            // If already at tile center (or very close), just stop
            if (distToClosest <= ARRIVAL_THRESHOLD)
            {
                transform.position = closestTileCenter;
                Stop();
                return;
            }

            // Otherwise, redirect to closest tile center and continue moving
            targetPosition = closestTileCenter;
            targetTile = closestTile;
            currentPath = null;
            isMoving = true;
        }

        /// <summary>
        /// Stops movement immediately.
        /// </summary>
        public void Stop()
        {
            // Log if we're stopping away from a tile center - this shouldn't happen
            if (tileOccupancy != null)
            {
                var currentTile = tileOccupancy.WorldToTile(transform.position);
                var tileCenter = tileOccupancy.TileToWorld(currentTile);
                float distFromCenter = Vector3.Distance(transform.position, tileCenter);

                if (distFromCenter > ARRIVAL_THRESHOLD)
                {
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
        /// Updates current speed with acceleration based on Reflexes stat.
        /// Higher reflexes = faster acceleration.
        /// </summary>
        private void UpdateSpeed()
        {
            // Get reflexes stat (default to 10 if no character)
            int reflexes = unitController?.Character?.Reflexes ?? 10;

            // Calculate target speed with multiplier
            float targetSpeed = EffectiveSpeed;

            // Calculate acceleration rate based on reflexes
            // Reflexes 1 = 0.5x acceleration (2 seconds to full speed)
            // Reflexes 10 = 1x acceleration (1 second to full speed)
            // Reflexes 20 = 2x acceleration (0.5 seconds to full speed)
            float reflexesMultiplier = 0.5f + (reflexes / 20f) * 1.5f;
            float acceleration = (targetSpeed / BASE_ACCELERATION_TIME) * reflexesMultiplier;

            // Smoothly accelerate toward target speed
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.deltaTime);
        }

        private bool RequestPath(Vector3 destination)
        {
            return RequestPath(destination, false);
        }

        private bool RequestPath(Vector3 destination, bool crossFloor)
        {
            // Throttle path requests to avoid spamming the pathfinder
            if (Time.time - lastPathRequestTime < PATH_REQUEST_COOLDOWN)
            {
                return false;
            }

            lastPathRequestTime = Time.time;
            // Don't set isMoving until path completes - otherwise unit moves directly toward target

            var arena = unitController?.CurrentArena;
            string floorInfo = "no arena";

            if (arena != null && arena.FloorCount > 0)
            {
                if (crossFloor)
                {
                    // Cross-floor: use combined mask to allow pathfinding through elevators/stairs
                    seeker.graphMask = arena.GetCombinedGraphMask();
                    floorInfo = $"CROSS-FLOOR, using combined graph mask";
                }
                else
                {
                    // Single floor: use current floor's graph mask only
                    var currentFloor = arena.GetFloorForUnit(unitController);
                    if (currentFloor != null && currentFloor.Graph != null)
                    {
                        seeker.graphMask = currentFloor.GetGraphMaskStruct();
                        floorInfo = $"floor={currentFloor.FloorId}, graphIdx={currentFloor.GraphIndex}, mask={currentFloor.GetGraphMask()}";
                    }
                    else
                    {
                        // Fallback to all arena graphs
                        seeker.graphMask = arena.GetCombinedGraphMask();
                        floorInfo = $"no floor found, using combined mask";
                    }
                }
            }
            else
            {
                // Use all graphs
                seeker.graphMask = GraphMask.everything;
                floorInfo = "no arena, using everything";
            }

            Debug.Log($"[{gameObject.name}] RequestPath: {floorInfo}, from {transform.position} to {destination}");
            seeker.StartPath(transform.position, destination, OnPathComplete);
            return true;
        }

        private void OnPathComplete(Path p)
        {
            if (p.error)
            {
                Debug.LogError($"[{gameObject.name}] Path error: {p.errorLog}");
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

            // Set target to LAST node in path (final destination)
            if (p.path != null && p.path.Count > 0)
            {
                targetPosition = (Vector3)p.path[p.path.Count - 1].position;

                // Debug: Show graph info for cross-floor paths
                if (isCrossFloorPath)
                {
                    var graphSet = new System.Collections.Generic.HashSet<uint>();
                    foreach (var node in p.path)
                    {
                        graphSet.Add(node.GraphIndex);
                    }
                    Debug.Log($"[{gameObject.name}] CROSS-FLOOR PATH: {p.path.Count} nodes across {graphSet.Count} graph(s): [{string.Join(", ", graphSet)}]");

                    // If only 1 graph, the elevator isn't being used!
                    if (graphSet.Count == 1)
                    {
                        Debug.LogWarning($"[{gameObject.name}] WARNING: Cross-floor path uses only 1 graph! NodeLink2 not working?");
                    }
                }

                // Validate path - only log if there's a problem
                ValidatePath(p);
            }
        }

        /// <summary>
        /// Validate path and log errors if any nodes are unwalkable
        /// </summary>
        private void ValidatePath(Path p)
        {
            int unwalkableCount = 0;
            foreach (var node in p.path)
            {
                if (!node.Walkable) unwalkableCount++;
            }

            if (unwalkableCount > 0)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[{gameObject.name}] PATH HAS {unwalkableCount} UNWALKABLE NODES:");

                for (int i = 0; i < p.path.Count; i++)
                {
                    var node = p.path[i];
                    if (!node.Walkable)
                    {
                        Vector3 pos = (Vector3)node.position;
                        sb.AppendLine($"  [{i}] ({pos.x:F2}, {pos.y:F2}) - UNWALKABLE");
                    }
                }

                Debug.LogError(sb.ToString());
            }
        }

        private void FollowPath()
        {
            // === STRICT PATH FOLLOWING - NO MOVEMENT WITHOUT VALID PATH ===

            // No path = no movement, period.
            if (currentPath == null || currentPath.path == null || currentPath.path.Count == 0)
            {
                isMoving = false;
                if (rb != null) rb.linearVelocity = Vector2.zero;
                return;
            }

            // All waypoints exhausted = arrived
            if (currentWaypoint >= currentPath.path.Count)
            {
                OnReachedDestination();
                return;
            }

            // Get current waypoint from path nodes ONLY
            Vector3 currentTarget = (Vector3)currentPath.path[currentWaypoint].position;
            float distanceToWaypoint = Vector3.Distance(transform.position, currentTarget);

            // Close enough to current waypoint - snap and advance
            if (distanceToWaypoint <= ARRIVAL_THRESHOLD)
            {
                // Snap to exact node position
                transform.position = currentTarget;

                // Check if next waypoint is on a different floor (cross-floor transition)
                if (isCrossFloorPath && currentWaypoint + 1 < currentPath.path.Count)
                {
                    var currentNode = currentPath.path[currentWaypoint];
                    var nextNode = currentPath.path[currentWaypoint + 1];

                    // Different graph = different floor = need elevator transition
                    if (currentNode.Graph != nextNode.Graph)
                    {
                        Debug.Log($"[{gameObject.name}] ARRIVED AT ELEVATOR: {currentTarget.x:F2}, {currentTarget.y:F2}");
                        HandleFloorTransition(nextNode);
                        // Don't advance waypoint here - HandleFloorTransition will position us correctly
                        return;
                    }
                }

                currentWaypoint++;

                // Check if that was the last waypoint
                if (currentWaypoint >= currentPath.path.Count)
                {
                    OnReachedDestination();
                }
                return;
            }

            // Move toward current waypoint
            Vector3 direction = (currentTarget - transform.position).normalized;

            // Update facing direction
            if (direction.sqrMagnitude > 0.01f)
            {
                facingDirection = new Vector2(direction.x, direction.y).normalized;
            }

            // Apply acceleration based on Reflexes stat
            UpdateSpeed();
            float moveDistance = currentSpeed * Time.deltaTime;

            // Don't overshoot the waypoint
            if (moveDistance > distanceToWaypoint)
            {
                moveDistance = distanceToWaypoint;
            }

            // Move using Rigidbody2D or direct transform
            Vector3 newPosition = transform.position + direction * moveDistance;

            if (rb != null && rb.bodyType == RigidbodyType2D.Kinematic)
            {
                rb.MovePosition(newPosition);
            }
            else if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                rb.linearVelocity = direction * currentSpeed;
            }
            else
            {
                transform.position = newPosition;
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
            if (currentPath == null || currentPath.path == null || !isMoving) return;

            // Draw raw path nodes (not smoothed vectorPath)
            Gizmos.color = Color.cyan;
            for (int i = currentWaypoint; i < currentPath.path.Count - 1; i++)
            {
                Vector3 from = (Vector3)currentPath.path[i].position;
                Vector3 to = (Vector3)currentPath.path[i + 1].position;
                Gizmos.DrawLine(from, to);
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(targetPosition, 0.3f);
        }
#endif
    }
}
