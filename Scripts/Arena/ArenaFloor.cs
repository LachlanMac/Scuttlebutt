using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using Pathfinding;
using Starbelter.AI;

namespace Starbelter.Arena
{
    /// <summary>
    /// Represents a single floor/level within an Arena.
    /// Each floor has its own tilemap, pathfinding graph, cover, and occupancy.
    /// </summary>
    public class ArenaFloor : MonoBehaviour
    {
        [Header("Floor Identity")]
        [SerializeField] private string floorId;
        [SerializeField] private int floorIndex = 0;

        [Header("Tilemaps")]
        [Tooltip("Floor tilemap - defines walkable area")]
        [SerializeField] private Tilemap floorTilemap;

        [Tooltip("Walls tilemap - structural obstacles")]
        [SerializeField] private Tilemap wallsTilemap;

        [Tooltip("Cover data tilemap (invisible, data-only)")]
        [SerializeField] private Tilemap coverTilemap;

        [Header("Pathfinding")]
        [Tooltip("Template graph index to copy settings from")]
        [SerializeField] private int templateGraphIndex = 0;

        [Tooltip("If true, aligns grid to floor tilemap")]
        [SerializeField] private bool alignToTilemap = true;

        [Tooltip("Node size (used if not aligning to tilemap)")]
        [SerializeField] private float pathfindingNodeSize = 1f;

        [Tooltip("Collision mask for obstacles")]
        [SerializeField] private LayerMask obstacleLayer = ~0;

        [Tooltip("Collision diameter multiplier")]
        [SerializeField] private float collisionDiameterMultiplier = 0.5f;

        [Header("Bounds")]
        [Tooltip("Override bounds. If zero, calculated from floor tilemap.")]
        [SerializeField] private Bounds overrideBounds;

        [Header("Rendering")]
        [Tooltip("Layer for this floor (set automatically based on floorIndex if not specified)")]
        [SerializeField] private string floorLayerName;

        [Tooltip("If true, sets layer on all children when initialized")]
        [SerializeField] private bool applyLayerToChildren = true;

        // Runtime state
        private Arena parentArena;
        private bool isInitialized;
        private Bounds calculatedBounds;
        private GridGraph floorGraph;
        private int graphIndex = -1;

        // Cover data
        private Dictionary<Vector3Int, List<CoverSource>> coverData = new Dictionary<Vector3Int, List<CoverSource>>();

        // Tile occupancy
        private Dictionary<Vector3Int, GameObject> occupiedTiles = new Dictionary<Vector3Int, GameObject>();
        private Dictionary<GameObject, Vector3Int> unitPositions = new Dictionary<GameObject, Vector3Int>();
        private Dictionary<Vector3Int, GameObject> reservedTiles = new Dictionary<Vector3Int, GameObject>();

        // Units on this floor
        private List<UnitController> unitsOnFloor = new List<UnitController>();

        // Doors and Rooms
        private List<Door> doors = new List<Door>();
        private List<Room> rooms = new List<Room>();
        private Dictionary<Vector3Int, Room> tileToRoom = new Dictionary<Vector3Int, Room>();

        // Runtime
        private int floorLayer = -1;

        // Properties
        public string FloorId => floorId;
        public int FloorIndex => floorIndex;
        public Arena ParentArena => parentArena;
        public bool IsInitialized => isInitialized;
        public Bounds Bounds => overrideBounds.size != Vector3.zero ? overrideBounds : calculatedBounds;
        public int GraphIndex => graphIndex;
        public GridGraph Graph => floorGraph;
        public Tilemap FloorTilemap => floorTilemap;
        public Tilemap WallsTilemap => wallsTilemap;
        public IReadOnlyList<UnitController> Units => unitsOnFloor;
        public IReadOnlyList<Door> Doors => doors;
        public IReadOnlyList<Room> Rooms => rooms;

        /// <summary>
        /// The Unity layer for this floor. Used for camera culling and physics.
        /// </summary>
        public int Layer => floorLayer;

        /// <summary>
        /// Layer mask for just this floor.
        /// </summary>
        public int LayerMask => floorLayer >= 0 ? (1 << floorLayer) : ~0;

        /// <summary>
        /// Initialize the floor. Called by parent Arena.
        /// </summary>
        public void Initialize(Arena arena)
        {
            if (isInitialized) return;

            parentArena = arena;

            if (string.IsNullOrEmpty(floorId))
            {
                floorId = $"Floor_{floorIndex}";
            }

            Debug.Log($"[ArenaFloor] Initializing floor '{floorId}' in arena '{arena.ArenaId}'...");

            // Set up layer
            SetupLayer();

            CalculateBounds();
            BakePathfinding();
            BakeCover();

            // Initialize doors first (rooms need door positions)
            InitializeDoors();

            // Initialize rooms (flood fill using door positions as boundaries)
            InitializeRooms();

            // Finalize door pathfinding (after graph is ready)
            FinalizeDoors();

            isInitialized = true;
            Debug.Log($"[ArenaFloor] Floor '{floorId}' initialized. Bounds: {Bounds}, Doors: {doors.Count}, Rooms: {rooms.Count}");
        }

        private void CalculateBounds()
        {
            bool hasBounds = false;

            // Start with floor tilemap bounds
            if (floorTilemap != null)
            {
                floorTilemap.CompressBounds();
                calculatedBounds = floorTilemap.localBounds;
                calculatedBounds.center = floorTilemap.transform.TransformPoint(calculatedBounds.center);
                hasBounds = true;
            }

            // Expand to include walls tilemap
            if (wallsTilemap != null)
            {
                wallsTilemap.CompressBounds();
                Bounds wallBounds = wallsTilemap.localBounds;
                wallBounds.center = wallsTilemap.transform.TransformPoint(wallBounds.center);

                if (hasBounds)
                {
                    calculatedBounds.Encapsulate(wallBounds);
                }
                else
                {
                    calculatedBounds = wallBounds;
                    hasBounds = true;
                }
            }

            if (!hasBounds)
            {
                calculatedBounds = new Bounds(transform.position, Vector3.one * 10f);
            }
        }

        #region Doors and Rooms

        private void InitializeDoors()
        {
            doors.Clear();
            doors.AddRange(GetComponentsInChildren<Door>());

            foreach (var door in doors)
            {
                door.Initialize(this);
            }

            Debug.Log($"[ArenaFloor] Initialized {doors.Count} doors");
        }

        private void InitializeRooms()
        {
            rooms.Clear();
            tileToRoom.Clear();

            // Collect all door tiles as blocked positions
            HashSet<Vector3Int> doorTiles = new HashSet<Vector3Int>();
            foreach (var door in doors)
            {
                foreach (var tile in door.OccupiedTiles)
                {
                    doorTiles.Add(tile);
                }
            }

            // Find and initialize all rooms
            rooms.AddRange(GetComponentsInChildren<Room>());

            foreach (var room in rooms)
            {
                room.Initialize(this, doorTiles);

                // Build tile → room lookup
                foreach (var tile in room.Tiles)
                {
                    if (!tileToRoom.ContainsKey(tile))
                    {
                        tileToRoom[tile] = room;
                    }
                    else
                    {
                        Debug.LogWarning($"[ArenaFloor] Tile {tile} claimed by both '{tileToRoom[tile].name}' and '{room.name}'!");
                    }
                }
            }

            Debug.Log($"[ArenaFloor] Initialized {rooms.Count} rooms covering {tileToRoom.Count} tiles");
        }

        private void FinalizeDoors()
        {
            foreach (var door in doors)
            {
                door.FinalizeInitialization();
            }
        }

        /// <summary>
        /// Get the room at a specific tile position.
        /// </summary>
        public Room GetRoomAtTile(Vector3Int tile)
        {
            tileToRoom.TryGetValue(tile, out var room);
            return room;
        }

        /// <summary>
        /// Get the room at a world position.
        /// </summary>
        public Room GetRoomAtPosition(Vector3 worldPosition)
        {
            return GetRoomAtTile(WorldToTile(worldPosition));
        }

        /// <summary>
        /// Get a room by type. Returns first match.
        /// </summary>
        public Room GetRoom(RoomType type)
        {
            foreach (var room in rooms)
            {
                if (room.Type == type)
                    return room;
            }
            return null;
        }

        /// <summary>
        /// Get all rooms of a specific type.
        /// </summary>
        public List<Room> GetRooms(RoomType type)
        {
            List<Room> result = new List<Room>();
            foreach (var room in rooms)
            {
                if (room.Type == type)
                    result.Add(room);
            }
            return result;
        }

        /// <summary>
        /// Get a door by ID.
        /// </summary>
        public Door GetDoor(string doorId)
        {
            foreach (var door in doors)
            {
                if (door.DoorId == doorId)
                    return door;
            }
            return null;
        }

        #endregion

        #region Layer Management

        private void SetupLayer()
        {
            // Determine layer name
            string layerName = floorLayerName;
            if (string.IsNullOrEmpty(layerName))
            {
                layerName = $"Floor{floorIndex}";
            }

            // Try to get the layer
            floorLayer = UnityEngine.LayerMask.NameToLayer(layerName);

            if (floorLayer < 0)
            {
                Debug.LogWarning($"[ArenaFloor] Layer '{layerName}' not found. Create it in Edit > Project Settings > Tags and Layers");
                return;
            }

            // Apply layer to this floor and optionally children
            if (applyLayerToChildren)
            {
                SetLayerRecursive(gameObject, floorLayer);
            }
            else
            {
                gameObject.layer = floorLayer;
            }

            Debug.Log($"[ArenaFloor] Floor '{floorId}' using layer '{layerName}' ({floorLayer})");
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
        /// Set a unit to this floor's layer.
        /// Call this when a unit moves to this floor.
        /// </summary>
        public void SetUnitLayer(GameObject unit)
        {
            if (floorLayer >= 0)
            {
                SetLayerRecursive(unit, floorLayer);
            }
        }

        #endregion

        #region Pathfinding

        private void BakePathfinding()
        {
            if (AstarPath.active == null)
            {
                Debug.LogWarning($"[ArenaFloor] No AstarPath found for floor '{floorId}'");
                return;
            }

            // Create a new graph for this floor
            floorGraph = AstarPath.active.data.AddGraph(typeof(GridGraph)) as GridGraph;
            floorGraph.name = $"{parentArena.ArenaId}_{floorId}";
            graphIndex = System.Array.IndexOf(AstarPath.active.data.graphs, floorGraph);

            Debug.Log($"[ArenaFloor] Created graph '{floorGraph.name}' at index {graphIndex}");

            ConfigureGraph(floorGraph);
            AstarPath.active.Scan(floorGraph);

            // Apply tilemap-based walkability
            ApplyTilemapWalkability();
        }

        /// <summary>
        /// Mark nodes as unwalkable based on tilemap data.
        /// Walkable = on floor tilemap AND not on walls tilemap.
        /// Walls ALWAYS override floor (if wall exists at position, it's unwalkable).
        /// </summary>
        private void ApplyTilemapWalkability()
        {
            if (floorGraph == null) return;
            if (floorTilemap == null && wallsTilemap == null) return;

            int noFloorCount = 0;
            int wallBlockedCount = 0;

            floorGraph.GetNodes(node =>
            {
                Vector3 worldPos = (Vector3)node.position;

                bool isWalkable = true;
                bool blockedByWall = false;
                bool noFloorTile = false;

                // Check walls FIRST - walls always block, regardless of floor
                if (wallsTilemap != null)
                {
                    // Use walls tilemap's own coordinate conversion
                    Vector3Int wallTilePos = wallsTilemap.WorldToCell(worldPos);
                    if (wallsTilemap.HasTile(wallTilePos))
                    {
                        isWalkable = false;
                        blockedByWall = true;
                    }
                }

                // Only check floor if not already blocked by wall
                if (isWalkable && floorTilemap != null)
                {
                    Vector3Int floorTilePos = floorTilemap.WorldToCell(worldPos);
                    if (!floorTilemap.HasTile(floorTilePos))
                    {
                        isWalkable = false;
                        noFloorTile = true;
                    }
                }

                if (!isWalkable && node.Walkable)
                {
                    node.Walkable = false;
                    if (blockedByWall) wallBlockedCount++;
                    else if (noFloorTile) noFloorCount++;
                }
            });

            Debug.Log($"[ArenaFloor] Applied tilemap walkability: {wallBlockedCount} blocked by walls, {noFloorCount} no floor tile");

            // Debug: Print walkability grid (uncomment for debugging)
            // DebugPrintWalkabilityGrid();
        }

        /// <summary>
        /// Debug: Print walkability grid to log
        /// </summary>
        private void DebugPrintWalkabilityGrid()
        {
            if (floorGraph == null) return;

            var bounds = Bounds;
            int minX = Mathf.FloorToInt(bounds.min.x);
            int maxX = Mathf.CeilToInt(bounds.max.x);
            int minY = Mathf.FloorToInt(bounds.min.y);
            int maxY = Mathf.CeilToInt(bounds.max.y);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[ArenaFloor] === WALKABILITY GRID for {floorId} ===");
            sb.AppendLine($"Bounds: X[{minX} to {maxX}], Y[{minY} to {maxY}]");

            // Print from top to bottom (high Y to low Y)
            for (int y = maxY; y >= minY; y--)
            {
                sb.Append($"Y{y,3}: ");
                for (int x = minX; x <= maxX; x++)
                {
                    Vector3 worldPos = new Vector3(x + 0.5f, y + 0.5f, 0);
                    var node = floorGraph.GetNearest(worldPos, NearestNodeConstraint.None).node;

                    if (node == null)
                        sb.Append("?");
                    else if (node.Walkable)
                        sb.Append(".");
                    else
                        sb.Append("#");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"[ArenaFloor] === END GRID (. = walkable, # = blocked, ? = no node) ===");
            Debug.Log(sb.ToString());
        }

        private void ConfigureGraph(GridGraph graph)
        {
            // Copy settings from template
            GridGraph templateGraph = null;
            if (templateGraphIndex >= 0 && templateGraphIndex < AstarPath.active.data.graphs.Length)
            {
                templateGraph = AstarPath.active.data.graphs[templateGraphIndex] as GridGraph;
            }

            if (templateGraph != null && templateGraph != graph)
            {
                graph.rotation = templateGraph.rotation;
                graph.collision.use2D = templateGraph.collision.use2D;
                graph.collision.type = templateGraph.collision.type;
                graph.collision.height = templateGraph.collision.height;
                graph.collision.collisionOffset = templateGraph.collision.collisionOffset;
                graph.neighbours = templateGraph.neighbours;
                graph.cutCorners = templateGraph.cutCorners;
                graph.maxSlope = templateGraph.maxSlope;
                graph.maxStepHeight = templateGraph.maxStepHeight;
                graph.erosionFirstTag = templateGraph.erosionFirstTag;
                graph.erodeIterations = templateGraph.erodeIterations;
            }
            else
            {
                graph.rotation = new Vector3(-90, 0, 0);
                graph.collision.use2D = true;
            }

            // Determine node size
            float nodeSize = pathfindingNodeSize;
            if (alignToTilemap && floorTilemap != null)
            {
                nodeSize = floorTilemap.cellSize.x;
            }
            if (nodeSize <= 0f) nodeSize = 1f;

            // Calculate dimensions
            var bounds = Bounds;
            int width = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / nodeSize));
            int depth = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / nodeSize));

            if (bounds.size.x <= 0 || bounds.size.y <= 0)
            {
                Debug.LogWarning($"[ArenaFloor] Invalid bounds for floor '{floorId}': {bounds}");
                width = 10;
                depth = 10;
            }

            graph.center = bounds.center;
            graph.SetDimensions(width, depth, nodeSize);

            // Build collision mask for THIS floor only (plus FloorShared)
            // This prevents doors/obstacles on other floors from affecting this floor's graph
            LayerMask floorCollisionMask = BuildFloorCollisionMask();
            graph.collision.mask = floorCollisionMask;
            graph.collision.diameter = nodeSize * collisionDiameterMultiplier;

            Debug.Log($"[ArenaFloor] Graph configured: {width}x{depth} nodes, center={bounds.center}, collisionMask={floorCollisionMask.value}");
        }

        /// <summary>
        /// Build a collision mask that only includes this floor's layer and FloorShared.
        /// This prevents obstacles on other floors from affecting this floor's pathfinding.
        /// </summary>
        private LayerMask BuildFloorCollisionMask()
        {
            int mask = 0;

            // Include this floor's layer
            if (floorLayer >= 0)
            {
                mask |= (1 << floorLayer);
            }

            // Include FloorShared layer
            int sharedLayer = UnityEngine.LayerMask.NameToLayer("FloorShared");
            if (sharedLayer >= 0)
            {
                mask |= (1 << sharedLayer);
            }

            // If we couldn't find floor layers, fall back to obstacleLayer (but warn)
            if (mask == 0)
            {
                Debug.LogWarning($"[ArenaFloor] Could not build floor-specific collision mask for '{floorId}', using obstacleLayer fallback");
                return obstacleLayer;
            }

            // Also include Default layer for general obstacles
            int defaultLayer = UnityEngine.LayerMask.NameToLayer("Default");
            if (defaultLayer >= 0)
            {
                mask |= (1 << defaultLayer);
            }

            return mask;
        }

        public void CleanupGraph()
        {
            if (floorGraph != null && AstarPath.active != null)
            {
                AstarPath.active.data.RemoveGraph(floorGraph);
                Debug.Log($"[ArenaFloor] Removed graph for floor '{floorId}'");
                floorGraph = null;
            }
        }

        public int GetGraphMask()
        {
            if (graphIndex >= 0)
            {
                return 1 << graphIndex;
            }
            return -1;
        }

        public GraphMask GetGraphMaskStruct()
        {
            return new GraphMask((uint)GetGraphMask());
        }

        #endregion

        #region Cover System

        private void BakeCover()
        {
            coverData.Clear();

            var halfCoverObjects = FindCoverObjectsWithTag("HalfCover");
            var fullCoverObjects = FindCoverObjectsWithTag("FullCover");

            foreach (var obj in halfCoverObjects)
            {
                ProcessCoverObject(obj, CoverType.Half);
            }
            foreach (var obj in fullCoverObjects)
            {
                ProcessCoverObject(obj, CoverType.Full);
            }

            Debug.Log($"[ArenaFloor] Cover baked for floor '{floorId}': {coverData.Count} positions");
        }

        private List<GameObject> FindCoverObjectsWithTag(string tag)
        {
            var result = new List<GameObject>();
            var allObjects = GameObject.FindGameObjectsWithTag(tag);
            foreach (var obj in allObjects)
            {
                if (Bounds.Contains(obj.transform.position))
                {
                    result.Add(obj);
                }
            }
            return result;
        }

        private static readonly Vector3Int[] AdjacentOffsets = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0)
        };

        private void ProcessCoverObject(GameObject obj, CoverType type)
        {
            var collider = obj.GetComponent<Collider2D>();
            if (collider == null) return;

            var bounds = collider.bounds;
            var occupiedTilesList = GetOccupiedTilesFromBounds(bounds);

            foreach (var occupiedTile in occupiedTilesList)
            {
                foreach (var offset in AdjacentOffsets)
                {
                    var adjacentTile = occupiedTile + offset;

                    if (occupiedTilesList.Contains(adjacentTile))
                        continue;

                    var worldPos = TileToWorld(adjacentTile);
                    if (Physics2D.OverlapPoint(worldPos) != null)
                        continue;

                    Vector2 directionToCover = new Vector2(
                        occupiedTile.x - adjacentTile.x,
                        occupiedTile.y - adjacentTile.y
                    ).normalized;

                    AddCoverSource(adjacentTile, new CoverSource
                    {
                        SourceObject = obj,
                        Type = type,
                        DirectionToCover = directionToCover,
                        CoverTilePosition = occupiedTile
                    });
                }
            }
        }

        private HashSet<Vector3Int> GetOccupiedTilesFromBounds(Bounds bounds)
        {
            var tiles = new HashSet<Vector3Int>();
            var minTile = WorldToTile(bounds.min);
            var maxTile = WorldToTile(bounds.max);

            for (int x = minTile.x; x <= maxTile.x; x++)
            {
                for (int y = minTile.y; y <= maxTile.y; y++)
                {
                    var tilePos = new Vector3Int(x, y, 0);
                    var worldCenter = TileToWorld(tilePos);
                    if (bounds.Contains(worldCenter))
                    {
                        tiles.Add(tilePos);
                    }
                }
            }
            return tiles;
        }

        private void AddCoverSource(Vector3Int tilePos, CoverSource source)
        {
            if (!coverData.ContainsKey(tilePos))
            {
                coverData[tilePos] = new List<CoverSource>();
            }
            coverData[tilePos].Add(source);
        }

        public bool HasCover(Vector3Int tilePosition)
        {
            return coverData.ContainsKey(tilePosition) && coverData[tilePosition].Count > 0;
        }

        public List<CoverSource> GetCoverAt(Vector3Int tilePosition)
        {
            if (coverData.TryGetValue(tilePosition, out var sources))
            {
                return sources;
            }
            return new List<CoverSource>();
        }

        #endregion

        #region Tile Occupancy

        public bool OccupyTile(GameObject unit, Vector3Int tilePosition)
        {
            if (occupiedTiles.TryGetValue(tilePosition, out var occupant))
            {
                if (occupant != unit && occupant != null)
                    return false;
            }

            if (unitPositions.TryGetValue(unit, out var oldPosition))
            {
                if (oldPosition != tilePosition)
                    occupiedTiles.Remove(oldPosition);
            }

            ClearReservation(unit);
            occupiedTiles[tilePosition] = unit;
            unitPositions[unit] = tilePosition;
            return true;
        }

        public bool OccupyTile(GameObject unit, Vector3 worldPosition)
        {
            return OccupyTile(unit, WorldToTile(worldPosition));
        }

        public void ReleaseTile(GameObject unit)
        {
            if (unitPositions.TryGetValue(unit, out var tilePosition))
            {
                occupiedTiles.Remove(tilePosition);
                unitPositions.Remove(unit);
            }
            ClearReservation(unit);
        }

        public bool ReserveTile(GameObject unit, Vector3Int tilePosition)
        {
            if (occupiedTiles.TryGetValue(tilePosition, out var occupant))
            {
                if (occupant != unit && occupant != null)
                    return false;
            }

            if (reservedTiles.TryGetValue(tilePosition, out var reserver))
            {
                if (reserver != unit && reserver != null)
                    return false;
            }

            ClearReservation(unit);
            reservedTiles[tilePosition] = unit;
            return true;
        }

        public void ClearReservation(GameObject unit)
        {
            Vector3Int? toRemove = null;
            foreach (var kvp in reservedTiles)
            {
                if (kvp.Value == unit)
                {
                    toRemove = kvp.Key;
                    break;
                }
            }
            if (toRemove.HasValue)
            {
                reservedTiles.Remove(toRemove.Value);
            }
        }

        public bool IsTileAvailable(Vector3Int tilePosition, GameObject excludeUnit = null)
        {
            if (occupiedTiles.TryGetValue(tilePosition, out var occupant))
            {
                if (occupant != null && occupant != excludeUnit)
                    return false;
            }
            if (reservedTiles.TryGetValue(tilePosition, out var reserver))
            {
                if (reserver != null && reserver != excludeUnit)
                    return false;
            }
            return true;
        }

        public GameObject GetOccupant(Vector3Int tilePosition)
        {
            occupiedTiles.TryGetValue(tilePosition, out var occupant);
            return occupant;
        }

        #endregion

        #region Unit Registration

        public void RegisterUnit(UnitController unit)
        {
            if (!unitsOnFloor.Contains(unit))
            {
                unitsOnFloor.Add(unit);
            }
        }

        public void UnregisterUnit(UnitController unit)
        {
            if (unitsOnFloor.Remove(unit))
            {
                ReleaseTile(unit.gameObject);
            }
        }

        public bool ContainsUnit(UnitController unit)
        {
            return unitsOnFloor.Contains(unit);
        }

        #endregion

        #region Coordinate Conversion

        public Vector3Int WorldToTile(Vector3 worldPosition)
        {
            if (floorTilemap != null)
            {
                return floorTilemap.WorldToCell(worldPosition);
            }
            return new Vector3Int(
                Mathf.FloorToInt(worldPosition.x + 0.5f),
                Mathf.FloorToInt(worldPosition.y + 0.5f),
                0
            );
        }

        public Vector3 TileToWorld(Vector3Int tilePosition)
        {
            if (floorTilemap != null)
            {
                return floorTilemap.GetCellCenterWorld(tilePosition);
            }
            return new Vector3(tilePosition.x, tilePosition.y, 0);
        }

        #endregion

        private void OnDestroy()
        {
            CleanupGraph();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.DrawWireCube(Bounds.center, Bounds.size);

            Gizmos.color = new Color(0f, 0f, 1f, 0.3f);
            foreach (var kvp in coverData)
            {
                var worldPos = TileToWorld(kvp.Key);
                Gizmos.DrawCube(worldPos, Vector3.one * 0.5f);
            }
        }
#endif
    }
}
