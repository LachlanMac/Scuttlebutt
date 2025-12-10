using UnityEngine;
using System.Collections.Generic;
using Pathfinding;
using Starbelter.AI;

namespace Starbelter.Arena
{
    /// <summary>
    /// Represents a complete playable area that may contain multiple floors.
    /// Examples: Ship (with multiple decks), building, space station.
    /// </summary>
    public class Arena : MonoBehaviour
    {
        [Header("Arena Identity")]
        [SerializeField] private string arenaId;
        [SerializeField] private ArenaType arenaType = ArenaType.Ship;

        [Header("Initialization")]
        [SerializeField] private bool initializeOnAwake = true;

        // Runtime state
        private bool isInitialized;
        private List<ArenaFloor> floors = new List<ArenaFloor>();
        private List<FloorConnection> floorConnections = new List<FloorConnection>();
        private List<Elevator> elevators = new List<Elevator>();
        private List<Portal> portals = new List<Portal>();
        private List<UnitController> registeredUnits = new List<UnitController>();
        private Dictionary<UnitController, ArenaFloor> unitFloorMap = new Dictionary<UnitController, ArenaFloor>();

        // Events
        public event System.Action<Arena> OnArenaInitialized;
        public event System.Action<UnitController> OnUnitEntered;
        public event System.Action<UnitController> OnUnitExited;
        public event System.Action<UnitController, ArenaFloor, ArenaFloor> OnUnitChangedFloor;

        // Properties
        public string ArenaId => arenaId;
        public ArenaType Type => arenaType;
        public bool IsInitialized => isInitialized;
        public IReadOnlyList<ArenaFloor> Floors => floors;
        public IReadOnlyList<Portal> Portals => portals;
        public IReadOnlyList<Elevator> Elevators => elevators;
        public IReadOnlyList<UnitController> Units => registeredUnits;
        public int FloorCount => floors.Count;

        /// <summary>
        /// Get the combined bounds of all floors.
        /// </summary>
        public Bounds Bounds
        {
            get
            {
                if (floors.Count == 0)
                    return new Bounds(transform.position, Vector3.one * 10f);

                var bounds = floors[0].Bounds;
                for (int i = 1; i < floors.Count; i++)
                {
                    bounds.Encapsulate(floors[i].Bounds);
                }
                return bounds;
            }
        }

        private void Awake()
        {
            if (string.IsNullOrEmpty(arenaId))
            {
                arenaId = $"{arenaType}_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
            }

            if (initializeOnAwake)
            {
                Initialize();
            }
        }

        private void Start()
        {
            // Register with ArenaManager
            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.RegisterArena(this);
            }

            // Find portals in children (external connections to other arenas)
            portals.AddRange(GetComponentsInChildren<Portal>());
            foreach (var portal in portals)
            {
                portal.SetArena(this);
            }
        }

        private void OnDestroy()
        {
            if (ArenaManager.Instance != null)
            {
                ArenaManager.Instance.UnregisterArena(this);
            }
        }

        /// <summary>
        /// Initialize the arena and all its floors.
        /// </summary>
        public void Initialize()
        {
            if (isInitialized) return;

            Debug.Log($"[Arena] Initializing arena '{arenaId}'...");

            // Collapse floors to X=0 (allows horizontal spread in editor for easier editing)
            CollapseFloors();

            // Find all floors in children
            floors.AddRange(GetComponentsInChildren<ArenaFloor>());
            floors.Sort((a, b) => a.FloorIndex.CompareTo(b.FloorIndex));

            Debug.Log($"[Arena] Found {floors.Count} floors");

            // Initialize each floor
            foreach (var floor in floors)
            {
                floor.Initialize(this);
            }

            // Find floor connections (stairs, ladders)
            floorConnections.AddRange(GetComponentsInChildren<FloorConnection>());
            foreach (var connection in floorConnections)
            {
                connection.Initialize(this);
            }

            Debug.Log($"[Arena] Found {floorConnections.Count} floor connections");

            // Find and initialize elevators (multi-floor connections)
            elevators.AddRange(GetComponentsInChildren<Elevator>());
            foreach (var elevator in elevators)
            {
                elevator.Initialize(this);
            }

            Debug.Log($"[Arena] Found {elevators.Count} elevators");

            isInitialized = true;
            OnArenaInitialized?.Invoke(this);

            Debug.Log($"[Arena] Arena '{arenaId}' initialized with {floors.Count} floors");
        }

        /// <summary>
        /// Collapse all ArenaFloor children to X=0.
        /// Allows floors to be spread horizontally in editor for easier editing.
        /// </summary>
        private void CollapseFloors()
        {
            var floorComponents = GetComponentsInChildren<ArenaFloor>();
            foreach (var floor in floorComponents)
            {
                var pos = floor.transform.localPosition;
                if (pos.x != 0f)
                {
                    floor.transform.localPosition = new Vector3(0f, pos.y, pos.z);
                }
            }
        }

        #region Floor Access

        /// <summary>
        /// Get a floor by index.
        /// </summary>
        public ArenaFloor GetFloor(int index)
        {
            if (index >= 0 && index < floors.Count)
            {
                return floors[index];
            }
            return null;
        }

        /// <summary>
        /// Get a floor by ID.
        /// </summary>
        public ArenaFloor GetFloor(string floorId)
        {
            foreach (var floor in floors)
            {
                if (floor.FloorId == floorId)
                    return floor;
            }
            return null;
        }

        /// <summary>
        /// Find which floor contains a world position.
        /// </summary>
        public ArenaFloor GetFloorAtPosition(Vector3 worldPosition)
        {
            foreach (var floor in floors)
            {
                if (floor.Bounds.Contains(worldPosition))
                {
                    return floor;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the floor a unit is currently on.
        /// </summary>
        public ArenaFloor GetFloorForUnit(UnitController unit)
        {
            unitFloorMap.TryGetValue(unit, out var floor);
            return floor;
        }

        /// <summary>
        /// Get a combined graph mask for all floors in this arena.
        /// </summary>
        public GraphMask GetCombinedGraphMask()
        {
            uint mask = 0;
            foreach (var floor in floors)
            {
                if (floor.GraphIndex >= 0)
                {
                    mask |= (uint)(1 << floor.GraphIndex);
                }
            }
            return new GraphMask(mask);
        }

        /// <summary>
        /// Get camera culling mask for a specific floor (includes that floor's layer).
        /// </summary>
        public int GetFloorCullingMask(int floorIndex)
        {
            var floor = GetFloor(floorIndex);
            if (floor != null)
            {
                return floor.LayerMask;
            }
            return ~0; // All layers
        }

        /// <summary>
        /// Get camera culling mask for all floors combined.
        /// </summary>
        public int GetAllFloorsCullingMask()
        {
            int mask = 0;
            foreach (var floor in floors)
            {
                mask |= floor.LayerMask;
            }
            return mask;
        }

        #endregion

        #region Unit Registration

        /// <summary>
        /// Register a unit as being in this arena on a specific floor.
        /// </summary>
        public void RegisterUnit(UnitController unit, ArenaFloor floor = null)
        {
            if (!registeredUnits.Contains(unit))
            {
                registeredUnits.Add(unit);
                OnUnitEntered?.Invoke(unit);
            }

            // Determine floor from position if not specified
            if (floor == null)
            {
                floor = GetFloorAtPosition(unit.transform.position);
            }

            if (floor != null)
            {
                SetUnitFloor(unit, floor);
            }
        }

        /// <summary>
        /// Unregister a unit from this arena.
        /// </summary>
        public void UnregisterUnit(UnitController unit)
        {
            if (registeredUnits.Remove(unit))
            {
                // Remove from current floor
                if (unitFloorMap.TryGetValue(unit, out var floor))
                {
                    floor.UnregisterUnit(unit);
                    unitFloorMap.Remove(unit);
                }
                OnUnitExited?.Invoke(unit);
            }
        }

        /// <summary>
        /// Move a unit to a different floor.
        /// </summary>
        public void SetUnitFloor(UnitController unit, ArenaFloor newFloor)
        {
            ArenaFloor oldFloor = null;
            unitFloorMap.TryGetValue(unit, out oldFloor);

            if (oldFloor == newFloor) return;

            // Unregister from old floor
            if (oldFloor != null)
            {
                oldFloor.UnregisterUnit(unit);
            }

            // Register with new floor
            if (newFloor != null)
            {
                newFloor.RegisterUnit(unit);
                unitFloorMap[unit] = newFloor;

                // Update unit's layer to match the floor
                newFloor.SetUnitLayer(unit.gameObject);
            }
            else
            {
                unitFloorMap.Remove(unit);
            }

            OnUnitChangedFloor?.Invoke(unit, oldFloor, newFloor);
        }

        /// <summary>
        /// Check if a unit is in this arena.
        /// </summary>
        public bool ContainsUnit(UnitController unit)
        {
            return registeredUnits.Contains(unit);
        }

        #endregion

        #region Tile Operations (delegated to floors)

        /// <summary>
        /// Occupy a tile on the appropriate floor.
        /// </summary>
        public bool OccupyTile(GameObject unit, Vector3 worldPosition)
        {
            var floor = GetFloorAtPosition(worldPosition);
            if (floor != null)
            {
                return floor.OccupyTile(unit, worldPosition);
            }
            return false;
        }

        /// <summary>
        /// Check if a tile is available on any floor.
        /// </summary>
        public bool IsTileAvailable(Vector3Int tilePosition, GameObject excludeUnit = null)
        {
            foreach (var floor in floors)
            {
                if (!floor.IsTileAvailable(tilePosition, excludeUnit))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Convert world position to tile on the appropriate floor.
        /// </summary>
        public Vector3Int WorldToTile(Vector3 worldPosition)
        {
            var floor = GetFloorAtPosition(worldPosition);
            if (floor != null)
            {
                return floor.WorldToTile(worldPosition);
            }
            // Fallback
            return new Vector3Int(
                Mathf.FloorToInt(worldPosition.x + 0.5f),
                Mathf.FloorToInt(worldPosition.y + 0.5f),
                0
            );
        }

        /// <summary>
        /// Convert tile to world position on a specific floor.
        /// </summary>
        public Vector3 TileToWorld(Vector3Int tilePosition, int floorIndex = 0)
        {
            var floor = GetFloor(floorIndex);
            if (floor != null)
            {
                return floor.TileToWorld(tilePosition);
            }
            return new Vector3(tilePosition.x, tilePosition.y, 0);
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw arena bounds
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireCube(Bounds.center, Bounds.size);
        }
#endif
    }

    public enum ArenaType
    {
        Ship,
        Planet,
        Station,
        Dropship,
        Custom
    }

    public enum CoverType
    {
        None,
        Half,
        Full
    }

    [System.Serializable]
    public struct CoverSource
    {
        public GameObject SourceObject;
        public CoverType Type;
        public Vector2 DirectionToCover;
        public Vector3Int CoverTilePosition;
    }
}
