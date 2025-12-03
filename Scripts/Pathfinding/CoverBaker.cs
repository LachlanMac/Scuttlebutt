using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using Pathfinding;

namespace Starbelter.Pathfinding
{
    /// <summary>
    /// Scans all colliders tagged as "HalfCover" or "FullCover" and marks adjacent tiles
    /// in the cover tilemap. Call Bake() on scene load or when cover objects are destroyed.
    /// </summary>
    public class CoverBaker : MonoBehaviour
    {
        public static CoverBaker Instance { get; private set; }

        [Header("References")]
        [Tooltip("The tilemap used to store cover data (invisible, data-only)")]
        [SerializeField] private Tilemap coverTilemap;

        [Tooltip("Tile asset to paint for cover positions")]
        [SerializeField] private TileBase coverTile;

        [Header("Settings")]
        [Tooltip("Layer mask for cover objects")]
        [SerializeField] private LayerMask coverLayerMask = ~0;

        [Tooltip("Tags that identify cover objects")]
        [SerializeField] private string halfCoverTag = "HalfCover";
        [SerializeField] private string fullCoverTag = "FullCover";

        // Stores cover data: key = tile position, value = list of cover sources at that position
        private Dictionary<Vector3Int, List<CoverSource>> coverData = new Dictionary<Vector3Int, List<CoverSource>>();

        // 4-directional offsets for adjacent tiles (cardinal directions only, no diagonals)
        private static readonly Vector3Int[] AdjacentOffsets = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0),   // Right
            new Vector3Int(-1, 0, 0),  // Left
            new Vector3Int(0, 1, 0),   // Up
            new Vector3Int(0, -1, 0)   // Down
        };

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
        /// Performs a full bake of all cover in the scene.
        /// Call this on scene load or after significant map changes.
        /// </summary>
        public void BakeAll()
        {
            ClearCoverData();

            // Find all cover objects by tag
            var halfCoverObjects = GameObject.FindGameObjectsWithTag(halfCoverTag);
            var fullCoverObjects = GameObject.FindGameObjectsWithTag(fullCoverTag);

            foreach (var obj in halfCoverObjects)
            {
                ProcessCoverObject(obj, CoverType.Half);
            }

            foreach (var obj in fullCoverObjects)
            {
                ProcessCoverObject(obj, CoverType.Full);
            }

            UpdateTilemap();

            Debug.Log($"[CoverBaker] Baked {coverData.Count} cover positions from {halfCoverObjects.Length + fullCoverObjects.Length} objects");
        }

        /// <summary>
        /// Bakes cover for a single object. Use when spawning new cover at runtime.
        /// </summary>
        public void BakeObject(GameObject coverObject)
        {
            CoverType type = CoverType.None;

            if (coverObject.CompareTag(halfCoverTag))
                type = CoverType.Half;
            else if (coverObject.CompareTag(fullCoverTag))
                type = CoverType.Full;

            if (type != CoverType.None)
            {
                ProcessCoverObject(coverObject, type);
                UpdateTilemap();
            }
        }

        /// <summary>
        /// Removes cover data for a destroyed object. Call before destroying cover.
        /// </summary>
        public void RemoveCoverObject(GameObject coverObject)
        {
            var collider = coverObject.GetComponent<Collider2D>();
            if (collider == null) return;

            var bounds = collider.bounds;
            var occupiedTiles = GetOccupiedTiles(bounds);

            foreach (var tile in occupiedTiles)
            {
                RemoveCoverAroundTile(tile, coverObject);
            }

            UpdateTilemap();
        }

        /// <summary>
        /// Gets all cover positions (tiles where units can take cover).
        /// </summary>
        public IReadOnlyDictionary<Vector3Int, List<CoverSource>> GetAllCoverData()
        {
            return coverData;
        }

        /// <summary>
        /// Checks if a tile position has cover available.
        /// </summary>
        public bool HasCover(Vector3Int tilePosition)
        {
            return coverData.ContainsKey(tilePosition) && coverData[tilePosition].Count > 0;
        }

        /// <summary>
        /// Gets cover sources at a specific tile position.
        /// </summary>
        public List<CoverSource> GetCoverAt(Vector3Int tilePosition)
        {
            if (coverData.TryGetValue(tilePosition, out var sources))
            {
                return sources;
            }
            return new List<CoverSource>();
        }

        /// <summary>
        /// Converts world position to tile position.
        /// </summary>
        public Vector3Int WorldToTile(Vector3 worldPosition)
        {
            return coverTilemap.WorldToCell(worldPosition);
        }

        /// <summary>
        /// Converts tile position to world position (center of tile).
        /// </summary>
        public Vector3 TileToWorld(Vector3Int tilePosition)
        {
            return coverTilemap.GetCellCenterWorld(tilePosition);
        }

        private void ProcessCoverObject(GameObject obj, CoverType type)
        {
            var collider = obj.GetComponent<Collider2D>();
            if (collider == null)
            {
                Debug.LogWarning($"[CoverBaker] Cover object '{obj.name}' has no Collider2D");
                return;
            }

            var bounds = collider.bounds;
            var occupiedTiles = GetOccupiedTiles(bounds);

            // For each tile the cover occupies, mark adjacent tiles as cover positions
            foreach (var occupiedTile in occupiedTiles)
            {
                foreach (var offset in AdjacentOffsets)
                {
                    var adjacentTile = occupiedTile + offset;

                    // Don't mark tiles that are inside the cover object itself
                    if (occupiedTiles.Contains(adjacentTile))
                        continue;

                    // Check if this adjacent tile is walkable (not blocked by collider)
                    var worldPos = TileToWorld(adjacentTile);
                    if (Physics2D.OverlapPoint(worldPos, coverLayerMask) != null)
                        continue;

                    // Calculate direction from adjacent tile to cover
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

        private HashSet<Vector3Int> GetOccupiedTiles(Bounds bounds)
        {
            var tiles = new HashSet<Vector3Int>();

            // Get tile bounds
            var minTile = coverTilemap.WorldToCell(bounds.min);
            var maxTile = coverTilemap.WorldToCell(bounds.max);

            for (int x = minTile.x; x <= maxTile.x; x++)
            {
                for (int y = minTile.y; y <= maxTile.y; y++)
                {
                    var tilePos = new Vector3Int(x, y, 0);
                    var worldCenter = TileToWorld(tilePos);

                    // Check if tile center is inside bounds
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

        private void RemoveCoverAroundTile(Vector3Int occupiedTile, GameObject coverObject)
        {
            foreach (var offset in AdjacentOffsets)
            {
                var adjacentTile = occupiedTile + offset;
                if (coverData.TryGetValue(adjacentTile, out var sources))
                {
                    sources.RemoveAll(s => s.SourceObject == coverObject);
                    if (sources.Count == 0)
                    {
                        coverData.Remove(adjacentTile);
                    }
                }
            }
        }

        private void ClearCoverData()
        {
            coverData.Clear();
            if (coverTilemap != null)
            {
                coverTilemap.ClearAllTiles();
            }
        }

        private void UpdateTilemap()
        {
            if (coverTilemap == null || coverTile == null) return;

            coverTilemap.ClearAllTiles();

            foreach (var kvp in coverData)
            {
                if (kvp.Value.Count > 0)
                {
                    coverTilemap.SetTile(kvp.Key, coverTile);
                }
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Bake Cover (Editor)")]
        private void EditorBake()
        {
            BakeAll();
        }
#endif
    }

    public enum CoverType
    {
        None,
        Half,
        Full
    }

    /// <summary>
    /// Represents a single source of cover at a tile position.
    /// A tile can have multiple cover sources (e.g., corner between two walls).
    /// </summary>
    [System.Serializable]
    public struct CoverSource
    {
        public GameObject SourceObject;
        public CoverType Type;
        public Vector2 DirectionToCover; // Normalized direction from cover position toward the cover object
        public Vector3Int CoverTilePosition; // The tile position of the actual cover object
    }
}
