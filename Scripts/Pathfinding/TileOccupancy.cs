using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

namespace Starbelter.Pathfinding
{
    /// <summary>
    /// Tracks which tiles are occupied by units.
    /// Ensures only one unit per tile for tactical positioning.
    /// Works alongside A* RVO for real-time movement avoidance.
    /// </summary>
    public class TileOccupancy : MonoBehaviour
    {
        public static TileOccupancy Instance { get; private set; }

        [Header("References")]
        [Tooltip("Reference tilemap for coordinate conversion (can be the cover tilemap)")]
        [SerializeField] private Tilemap referenceTilemap;

        // Maps tile positions to occupying units
        private Dictionary<Vector3Int, GameObject> occupiedTiles = new Dictionary<Vector3Int, GameObject>();

        // Maps units to their occupied tile (reverse lookup)
        private Dictionary<GameObject, Vector3Int> unitPositions = new Dictionary<GameObject, Vector3Int>();

        // Tiles that are reserved (unit is moving toward them)
        private Dictionary<Vector3Int, GameObject> reservedTiles = new Dictionary<Vector3Int, GameObject>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Registers a unit at a tile position. Call when unit spawns or finishes moving.
        /// </summary>
        /// <returns>True if successful, false if tile is already occupied</returns>
        public bool OccupyTile(GameObject unit, Vector3Int tilePosition)
        {
            // Check if tile is occupied by another unit
            if (occupiedTiles.TryGetValue(tilePosition, out var occupant))
            {
                if (occupant != unit && occupant != null)
                {
                    return false; // Tile occupied by someone else
                }
            }

            // Clear old position if unit was elsewhere
            if (unitPositions.TryGetValue(unit, out var oldPosition))
            {
                if (oldPosition != tilePosition)
                {
                    occupiedTiles.Remove(oldPosition);
                }
            }

            // Clear any reservation this unit had
            ClearReservation(unit);

            // Occupy new position
            occupiedTiles[tilePosition] = unit;
            unitPositions[unit] = tilePosition;

            return true;
        }

        /// <summary>
        /// Registers a unit at a world position. Converts to tile position internally.
        /// </summary>
        public bool OccupyTile(GameObject unit, Vector3 worldPosition)
        {
            var tilePos = WorldToTile(worldPosition);
            return OccupyTile(unit, tilePos);
        }

        /// <summary>
        /// Releases a tile when unit leaves or is destroyed.
        /// </summary>
        public void ReleaseTile(GameObject unit)
        {
            if (unitPositions.TryGetValue(unit, out var tilePosition))
            {
                occupiedTiles.Remove(tilePosition);
                unitPositions.Remove(unit);
            }
            ClearReservation(unit);
        }

        /// <summary>
        /// Reserves a tile that a unit is moving toward.
        /// Other units should avoid pathfinding to reserved tiles.
        /// </summary>
        public bool ReserveTile(GameObject unit, Vector3Int tilePosition)
        {
            // Can't reserve if occupied by another unit
            if (occupiedTiles.TryGetValue(tilePosition, out var occupant))
            {
                if (occupant != unit && occupant != null)
                {
                    return false;
                }
            }

            // Can't reserve if reserved by another unit
            if (reservedTiles.TryGetValue(tilePosition, out var reserver))
            {
                if (reserver != unit && reserver != null)
                {
                    return false;
                }
            }

            // Clear old reservation
            ClearReservation(unit);

            reservedTiles[tilePosition] = unit;
            return true;
        }

        /// <summary>
        /// Reserves a tile at a world position.
        /// </summary>
        public bool ReserveTile(GameObject unit, Vector3 worldPosition)
        {
            var tilePos = WorldToTile(worldPosition);
            return ReserveTile(unit, tilePos);
        }

        /// <summary>
        /// Clears a unit's tile reservation.
        /// </summary>
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

        /// <summary>
        /// Checks if a tile is available (not occupied or reserved by another unit).
        /// </summary>
        public bool IsTileAvailable(Vector3Int tilePosition, GameObject excludeUnit = null)
        {
            if (occupiedTiles.TryGetValue(tilePosition, out var occupant))
            {
                if (occupant != null && occupant != excludeUnit)
                {
                    return false;
                }
            }

            if (reservedTiles.TryGetValue(tilePosition, out var reserver))
            {
                if (reserver != null && reserver != excludeUnit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if a world position's tile is available.
        /// </summary>
        public bool IsTileAvailable(Vector3 worldPosition, GameObject excludeUnit = null)
        {
            var tilePos = WorldToTile(worldPosition);
            return IsTileAvailable(tilePos, excludeUnit);
        }

        /// <summary>
        /// Gets the unit occupying a tile, if any.
        /// </summary>
        public GameObject GetOccupant(Vector3Int tilePosition)
        {
            occupiedTiles.TryGetValue(tilePosition, out var occupant);
            return occupant;
        }

        /// <summary>
        /// Gets the unit occupying a world position, if any.
        /// </summary>
        public GameObject GetOccupant(Vector3 worldPosition)
        {
            var tilePos = WorldToTile(worldPosition);
            return GetOccupant(tilePos);
        }

        /// <summary>
        /// Gets the tile position a unit currently occupies.
        /// </summary>
        public Vector3Int? GetUnitTile(GameObject unit)
        {
            if (unitPositions.TryGetValue(unit, out var pos))
            {
                return pos;
            }
            return null;
        }

        /// <summary>
        /// Finds the nearest available tile to a target position.
        /// Useful for pathfinding when target tile is occupied.
        /// </summary>
        public Vector3Int? FindNearestAvailableTile(Vector3Int targetTile, GameObject excludeUnit = null, int maxRadius = 5)
        {
            if (IsTileAvailable(targetTile, excludeUnit))
            {
                return targetTile;
            }

            // Spiral outward to find nearest available
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        // Only check tiles at this radius (not inner ones already checked)
                        if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius)
                            continue;

                        var checkTile = new Vector3Int(targetTile.x + dx, targetTile.y + dy, 0);

                        if (IsTileAvailable(checkTile, excludeUnit))
                        {
                            return checkTile;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all currently occupied tiles.
        /// </summary>
        public IReadOnlyDictionary<Vector3Int, GameObject> GetAllOccupiedTiles()
        {
            return occupiedTiles;
        }

        /// <summary>
        /// Converts world position to tile position.
        /// </summary>
        public Vector3Int WorldToTile(Vector3 worldPosition)
        {
            if (referenceTilemap != null)
            {
                return referenceTilemap.WorldToCell(worldPosition);
            }

            // Fallback: assume 1x1 tiles centered at integer positions
            return new Vector3Int(
                Mathf.FloorToInt(worldPosition.x + 0.5f),
                Mathf.FloorToInt(worldPosition.y + 0.5f),
                0
            );
        }

        /// <summary>
        /// Converts tile position to world position (center of tile).
        /// </summary>
        public Vector3 TileToWorld(Vector3Int tilePosition)
        {
            if (referenceTilemap != null)
            {
                return referenceTilemap.GetCellCenterWorld(tilePosition);
            }

            // Fallback: assume 1x1 tiles centered at 0.5 offset
            return new Vector3(tilePosition.x + 0.5f, tilePosition.y + 0.5f, 0);
        }

        /// <summary>
        /// Cleans up null references (destroyed units).
        /// Call periodically if units are destroyed without calling ReleaseTile.
        /// </summary>
        public void CleanupNullReferences()
        {
            var tilesToRemove = new List<Vector3Int>();
            var unitsToRemove = new List<GameObject>();

            foreach (var kvp in occupiedTiles)
            {
                if (kvp.Value == null)
                {
                    tilesToRemove.Add(kvp.Key);
                }
            }

            foreach (var kvp in unitPositions)
            {
                if (kvp.Key == null)
                {
                    unitsToRemove.Add(kvp.Key);
                }
            }

            foreach (var tile in tilesToRemove)
            {
                occupiedTiles.Remove(tile);
            }

            foreach (var unit in unitsToRemove)
            {
                unitPositions.Remove(unit);
            }

            // Same for reservations
            tilesToRemove.Clear();
            foreach (var kvp in reservedTiles)
            {
                if (kvp.Value == null)
                {
                    tilesToRemove.Add(kvp.Key);
                }
            }

            foreach (var tile in tilesToRemove)
            {
                reservedTiles.Remove(tile);
            }
        }

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos) return;

            // Draw occupied tiles in red
            Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
            foreach (var kvp in occupiedTiles)
            {
                if (kvp.Value != null)
                {
                    var worldPos = TileToWorld(kvp.Key);
                    Gizmos.DrawCube(worldPos, Vector3.one * 0.9f);
                }
            }

            // Draw reserved tiles in yellow
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            foreach (var kvp in reservedTiles)
            {
                if (kvp.Value != null)
                {
                    var worldPos = TileToWorld(kvp.Key);
                    Gizmos.DrawWireCube(worldPos, Vector3.one * 0.95f);
                }
            }
        }
#endif
    }
}
