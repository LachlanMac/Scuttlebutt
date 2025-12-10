using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Pathfinding;

namespace Starbelter.Arena
{
    /// <summary>
    /// Represents a room within an ArenaFloor.
    /// Place this GameObject anywhere inside the room - it will flood fill to find all tiles.
    /// </summary>
    public class Room : MonoBehaviour
    {
        [Header("Room Identity")]
        [SerializeField] private string roomId;
        [SerializeField] private RoomType roomType = RoomType.Generic;
        [SerializeField] private RoomAccess accessRestriction = RoomAccess.All;

        [Header("Display")]
        [SerializeField] private string displayName;

        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0f, 0.2f);

        // Runtime state
        private ArenaFloor parentFloor;
        private HashSet<Vector3Int> roomTiles = new HashSet<Vector3Int>();
        private List<Door> connectedDoors = new List<Door>();
        private bool isInitialized;

        // Properties
        public string RoomId => roomId;
        public RoomType Type => roomType;
        public RoomAccess Access => accessRestriction;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? roomType.ToString() : displayName;

        /// <summary>
        /// Check if a specific access level is allowed in this room.
        /// </summary>
        public bool AllowsAccess(RoomAccess access)
        {
            return (accessRestriction & access) != 0;
        }
        public ArenaFloor ParentFloor => parentFloor;
        public bool IsInitialized => isInitialized;
        public int TileCount => roomTiles.Count;
        public IReadOnlyCollection<Vector3Int> Tiles => roomTiles;
        public IReadOnlyList<Door> Doors => connectedDoors;

        /// <summary>
        /// Initialize the room. Called by ArenaFloor.
        /// </summary>
        public void Initialize(ArenaFloor floor, IReadOnlyCollection<Vector3Int> doorTiles)
        {
            if (isInitialized) return;

            parentFloor = floor;

            if (string.IsNullOrEmpty(roomId))
            {
                roomId = $"{roomType}_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
            }

            // Flood fill to find all tiles in this room
            FloodFillRoom(doorTiles);

            // Find connected doors
            FindConnectedDoors();

            isInitialized = true;

            Debug.Log($"[Room] Initialized '{DisplayName}' ({roomId}) with {roomTiles.Count} tiles");
        }

        /// <summary>
        /// Flood fill from this GameObject's position to find all room tiles.
        /// </summary>
        private void FloodFillRoom(IReadOnlyCollection<Vector3Int> blockedTiles)
        {
            roomTiles.Clear();

            // Get starting tile from this GameObject's position
            Vector3Int startTile = parentFloor.WorldToTile(transform.position);

            // Validate start position
            if (!IsValidFloorTile(startTile))
            {
                Debug.LogWarning($"[Room] Room '{roomId}' starting position is not on a valid floor tile!");
                return;
            }

            // Flood fill using BFS
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            HashSet<Vector3Int> visited = new HashSet<Vector3Int>();

            queue.Enqueue(startTile);
            visited.Add(startTile);

            while (queue.Count > 0)
            {
                Vector3Int current = queue.Dequeue();

                // Check if this tile is valid for the room
                if (!IsValidFloorTile(current))
                    continue;

                if (IsWallTile(current))
                    continue;

                if (blockedTiles.Contains(current))
                    continue; // Door threshold - stop here

                // Valid room tile!
                roomTiles.Add(current);

                // Check neighbors (4-directional)
                CheckNeighbor(current + Vector3Int.right, visited, queue);
                CheckNeighbor(current + Vector3Int.left, visited, queue);
                CheckNeighbor(current + Vector3Int.up, visited, queue);
                CheckNeighbor(current + Vector3Int.down, visited, queue);
            }
        }

        private void CheckNeighbor(Vector3Int tile, HashSet<Vector3Int> visited, Queue<Vector3Int> queue)
        {
            if (!visited.Contains(tile))
            {
                visited.Add(tile);
                queue.Enqueue(tile);
            }
        }

        private bool IsValidFloorTile(Vector3Int tile)
        {
            if (parentFloor.FloorTilemap == null) return false;
            return parentFloor.FloorTilemap.HasTile(tile);
        }

        private bool IsWallTile(Vector3Int tile)
        {
            if (parentFloor.WallsTilemap == null) return false;
            return parentFloor.WallsTilemap.HasTile(tile);
        }

        /// <summary>
        /// Find doors that are adjacent to this room.
        /// </summary>
        private void FindConnectedDoors()
        {
            connectedDoors.Clear();

            // Get all doors from the floor
            var allDoors = parentFloor.GetComponentsInChildren<Door>();

            foreach (var door in allDoors)
            {
                // Check if any of the door's tiles are adjacent to our room tiles
                foreach (var doorTile in door.OccupiedTiles)
                {
                    if (IsTileAdjacentToRoom(doorTile))
                    {
                        if (!connectedDoors.Contains(door))
                        {
                            connectedDoors.Add(door);
                        }
                        break;
                    }
                }
            }
        }

        private bool IsTileAdjacentToRoom(Vector3Int tile)
        {
            return roomTiles.Contains(tile + Vector3Int.right) ||
                   roomTiles.Contains(tile + Vector3Int.left) ||
                   roomTiles.Contains(tile + Vector3Int.up) ||
                   roomTiles.Contains(tile + Vector3Int.down);
        }

        #region Queries

        /// <summary>
        /// Check if a tile position is in this room.
        /// </summary>
        public bool ContainsTile(Vector3Int tile)
        {
            return roomTiles.Contains(tile);
        }

        /// <summary>
        /// Check if a world position is in this room.
        /// </summary>
        public bool ContainsPosition(Vector3 worldPosition)
        {
            Vector3Int tile = parentFloor.WorldToTile(worldPosition);
            return roomTiles.Contains(tile);
        }

        /// <summary>
        /// Get a random WALKABLE tile in this room.
        /// Checks pathfinding graph to ensure tile is actually walkable.
        /// </summary>
        public Vector3Int GetRandomTile()
        {
            if (roomTiles.Count == 0)
                return parentFloor.WorldToTile(transform.position);

            var graph = parentFloor?.Graph;

            // Try up to 10 times to find a walkable tile
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int index = Random.Range(0, roomTiles.Count);
                int i = 0;
                Vector3Int selectedTile = default;

                foreach (var tile in roomTiles)
                {
                    if (i == index)
                    {
                        selectedTile = tile;
                        break;
                    }
                    i++;
                }

                // If no graph, just return the tile
                if (graph == null)
                    return selectedTile;

                // Check if tile is walkable
                Vector3 worldPos = parentFloor.TileToWorld(selectedTile);
                var node = graph.GetNearest(worldPos, NearestNodeConstraint.None).node;

                if (node != null && node.Walkable)
                    return selectedTile;
            }

            // Fallback: return any tile (shouldn't happen often)
            Debug.LogWarning($"[Room] '{displayName}' couldn't find walkable tile after 10 attempts!");
            return roomTiles.First();
        }

        /// <summary>
        /// Get a random world position in this room.
        /// </summary>
        public Vector3 GetRandomPosition()
        {
            return parentFloor.TileToWorld(GetRandomTile());
        }

        /// <summary>
        /// Get the center tile of this room (approximate).
        /// </summary>
        public Vector3Int GetCenterTile()
        {
            if (roomTiles.Count == 0)
                return parentFloor.WorldToTile(transform.position);

            Vector3 sum = Vector3.zero;
            foreach (var tile in roomTiles)
            {
                sum += new Vector3(tile.x, tile.y, 0);
            }
            sum /= roomTiles.Count;

            return new Vector3Int(Mathf.RoundToInt(sum.x), Mathf.RoundToInt(sum.y), 0);
        }

        /// <summary>
        /// Get the center world position of this room.
        /// </summary>
        public Vector3 GetCenterPosition()
        {
            return parentFloor.TileToWorld(GetCenterTile());
        }

        #endregion

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Snap to 0.5 intervals
            var pos = transform.localPosition;
            pos.x = Mathf.Round(pos.x * 2f) / 2f;
            pos.y = Mathf.Round(pos.y * 2f) / 2f;
            transform.localPosition = pos;
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos) return;

            // Draw room origin
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position, 0.3f);

            // Draw room tiles if initialized
            if (isInitialized && parentFloor != null)
            {
                foreach (var tile in roomTiles)
                {
                    Vector3 worldPos = parentFloor.TileToWorld(tile);
                    Gizmos.DrawCube(worldPos, Vector3.one * 0.9f);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw room label
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, DisplayName);
        }
#endif
    }

    public enum RoomType
    {
        Generic,

        // Command
        Bridge,
        CIC,                    // Combat Information Center
        BriefingRoom,
        ReadyRoom,              // Captain's office adjacent to bridge

        // Quarters
        CaptainsQuarters,
        FirstOfficerQuarters,
        OfficerQuarters,
        EnlistedQuarters,
        Barracks,

        // Operations
        Engineering,
        ReactorRoom,
        MachineShop,
        LifeSupport,
        ScienceLab,
        Communications,

        // Admin / Logistics
        AdministrationOffice,
        SupplyOffice,

        // Combat/Security
        Armory,
        Brig,
        SecurityStation,
        SquadBay,               // Marine planning/ready room

        // Medical
        MedBay,
        Morgue,

        // Crew Welfare
        MessHall,
        Gym,
        Lounge,

        // Logistics
        CargoHold,
        StorageRoom,

        // Access
        Hallway,
        Airlock,
        Hangar,
        MaintenanceShaft
    }

    /// <summary>
    /// Flags for who can access a room. Use for AI behavior and room restrictions.
    /// </summary>
    [System.Flags]
    public enum RoomAccess
    {
        None = 0,
        Officers = 1 << 0,      // 1
        Enlisted = 1 << 1,      // 2
        Pilots = 1 << 2,        // 4
        Guests = 1 << 3,        // 8
        Security = 1 << 4,      // 16
        Medical = 1 << 5,       // 32
        Engineering = 1 << 6,   // 64
        All = ~0
    }
}
