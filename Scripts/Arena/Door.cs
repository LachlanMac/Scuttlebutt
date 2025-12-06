using UnityEngine;
using System.Collections.Generic;
using Pathfinding;

namespace Starbelter.Arena
{
    /// <summary>
    /// Represents a door/threshold between rooms.
    /// Blocks room flood fill and can block pathfinding when locked.
    /// </summary>
    public class Door : MonoBehaviour
    {
        
        [Header("Door Identity")]
        [SerializeField] private string doorId;

        [Header("Tiles")]
        [Tooltip("If true, automatically detect occupied tiles from collider bounds")]
        [SerializeField] private bool autoDetectTiles = true;

        [Tooltip("Additional tile offsets this door occupies (used if autoDetectTiles is false)")]
        [SerializeField] private List<Vector3Int> additionalTileOffsets = new List<Vector3Int>();

        [Header("State")]
        [SerializeField] private bool isLocked = false;
        [SerializeField] private bool startsLocked = false;

        [Header("Pathfinding")]
        [Tooltip("If true, locked doors block pathfinding")]
        [SerializeField] private bool blockPathfindingWhenLocked = true;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer doorSprite;

        // Auto-detected colliders
        private Collider2D blockingCollider;   // isTrigger = false
        private Collider2D detectionTrigger;   // isTrigger = true

        // Auto-door state
        private int unitsInTrigger = 0;
        private bool isOpen = false;

        // Runtime
        private ArenaFloor parentFloor;
        private List<Vector3Int> occupiedTiles = new List<Vector3Int>();
        private bool isInitialized;

        // Properties
        public string DoorId => doorId;
        public bool IsLocked => isLocked;
        public bool IsOpen => isOpen && !isLocked;
        public bool IsClosed => !isOpen || isLocked;
        public ArenaFloor ParentFloor => parentFloor;
        public IReadOnlyList<Vector3Int> OccupiedTiles => occupiedTiles;

        private void Awake()
        {
            if (string.IsNullOrEmpty(doorId))
            {
                doorId = $"Door_{System.Guid.NewGuid().ToString().Substring(0, 8)}";
            }

            EnsureCollidersDetected();
        }

        /// <summary>
        /// Detect colliders and sprite renderer if not already found.
        /// Called from both Awake and Initialize to handle execution order issues.
        /// </summary>
        private void EnsureCollidersDetected()
        {
            // Auto-detect colliders (check self and children)
            if (blockingCollider == null || detectionTrigger == null)
            {
                var colliders = GetComponentsInChildren<Collider2D>();
                foreach (var col in colliders)
                {
                    if (col.isTrigger && detectionTrigger == null)
                        detectionTrigger = col;
                    else if (!col.isTrigger && blockingCollider == null)
                        blockingCollider = col;
                }
            }

            // Auto-detect sprite renderer
            if (doorSprite == null)
            {
                doorSprite = GetComponentInChildren<SpriteRenderer>();
            }
        }

        /// <summary>
        /// Initialize the door. Called by ArenaFloor.
        /// </summary>
        public void Initialize(ArenaFloor floor)
        {
            if (isInitialized) return;

            parentFloor = floor;

            // Ensure colliders are detected (Awake may not have run yet)
            EnsureCollidersDetected();

            // Calculate occupied tiles
            CalculateOccupiedTiles();

            // Set initial state
            if (startsLocked)
            {
                SetLocked(true, updatePathfinding: false); // Don't update yet, graph not ready
            }

            isInitialized = true;
            Debug.Log($"[Door] Initialized '{doorId}' occupying {occupiedTiles.Count} tiles");
        }

        /// <summary>
        /// Called after pathfinding is baked to set initial lock state.
        /// </summary>
        public void FinalizeInitialization()
        {
            if (isLocked && blockPathfindingWhenLocked)
            {
                UpdatePathfinding();
            }
        }

        private void CalculateOccupiedTiles()
        {
            occupiedTiles.Clear();

            // Debug: show why auto-detect might not work
            if (!autoDetectTiles)
            {
                Debug.Log($"[Door] '{doorId}' autoDetectTiles is OFF - using manual offsets");
            }
            else if (blockingCollider == null)
            {
                Debug.LogWarning($"[Door] '{doorId}' has no blocking collider for auto-detect");
            }

            if (autoDetectTiles && blockingCollider != null)
            {
                // Auto-detect from collider bounds
                Bounds bounds = blockingCollider.bounds;

                Vector3Int minTile = parentFloor.WorldToTile(bounds.min);
                Vector3Int maxTile = parentFloor.WorldToTile(bounds.max);

                for (int x = minTile.x; x <= maxTile.x; x++)
                {
                    for (int y = minTile.y; y <= maxTile.y; y++)
                    {
                        Vector3Int tile = new Vector3Int(x, y, 0);
                        Vector3 tileCenter = parentFloor.TileToWorld(tile);

                        // Check if tile center is inside collider bounds
                        if (bounds.Contains(tileCenter))
                        {
                            occupiedTiles.Add(tile);
                        }
                    }
                }

                Debug.Log($"[Door] '{doorId}' auto-detected {occupiedTiles.Count} tiles from collider bounds");
            }
            else
            {
                // Manual: main tile + offsets
                Vector3Int mainTile = parentFloor.WorldToTile(transform.position);
                occupiedTiles.Add(mainTile);

                foreach (var offset in additionalTileOffsets)
                {
                    occupiedTiles.Add(mainTile + offset);
                }
            }
        }

        #region Lock/Unlock

        /// <summary>
        /// Lock or unlock the door.
        /// </summary>
        public void SetLocked(bool locked, bool updatePathfinding = true)
        {
            if (isLocked == locked) return;

            isLocked = locked;

            // Update visuals
            UpdateVisuals();

            // Update pathfinding
            if (updatePathfinding && blockPathfindingWhenLocked)
            {
                UpdatePathfinding();
            }

            Debug.Log($"[Door] '{doorId}' is now {(locked ? "LOCKED" : "UNLOCKED")}");
        }

        public void Lock() => SetLocked(true);
        public void Unlock() => SetLocked(false);
        public void Toggle() => SetLocked(!isLocked);

        private void UpdateVisuals()
        {
            // Door is visually open if: unlocked AND someone is in trigger
            bool visuallyOpen = !isLocked && isOpen;

            // Update blocking collider (blocks when closed or locked)
            if (blockingCollider != null)
            {
                blockingCollider.enabled = !visuallyOpen;
            }

            // Update sprite (hide when open)
            if (doorSprite != null)
            {
                doorSprite.enabled = !visuallyOpen;
            }
        }

        #region Auto Door Trigger

        /// <summary>
        /// Call this from a child trigger zone's OnTriggerEnter2D.
        /// Or place a trigger collider on the Door GameObject itself.
        /// </summary>
        public void OnUnitEnteredZone(Collider2D other)
        {
            // Only react to units
            if (other.GetComponentInParent<Starbelter.AI.UnitController>() == null)
                return;

            unitsInTrigger++;

            if (!isLocked && unitsInTrigger > 0)
            {
                OpenDoor();
            }
        }

        /// <summary>
        /// Call this from a child trigger zone's OnTriggerExit2D.
        /// </summary>
        public void OnUnitExitedZone(Collider2D other)
        {
            if (other.GetComponentInParent<Starbelter.AI.UnitController>() == null)
                return;

            unitsInTrigger = Mathf.Max(0, unitsInTrigger - 1);

            if (unitsInTrigger == 0)
            {
                CloseDoor();
            }
        }

        // Also support triggers directly on this GameObject
        private void OnTriggerEnter2D(Collider2D other) => OnUnitEnteredZone(other);
        private void OnTriggerExit2D(Collider2D other) => OnUnitExitedZone(other);

        private void OpenDoor()
        {
            if (isOpen || isLocked) return;

            isOpen = true;
            UpdateVisuals();
            UpdatePathfinding();

            Debug.Log($"[Door] '{doorId}' opened");
        }

        private void CloseDoor()
        {
            if (!isOpen) return;

            isOpen = false;
            UpdateVisuals();
            UpdatePathfinding();

            Debug.Log($"[Door] '{doorId}' closed");
        }

        #endregion

        private void UpdatePathfinding()
        {
            if (AstarPath.active == null) return;
            if (!blockPathfindingWhenLocked) return;

            // Calculate bounds for the door tiles
            Bounds bounds = CalculateDoorBounds();

            // Walkable when: unlocked AND open (or just unlocked for pathfinding planning)
            // For pathfinding, we treat unlocked doors as walkable so units can path through them
            // The door will open automatically when they approach
            bool walkable = !isLocked;

            // Create graph update
            var guo = new GraphUpdateObject(bounds);
            guo.modifyWalkability = true;
            guo.setWalkability = walkable;

            AstarPath.active.UpdateGraphs(guo);
        }

        private Bounds CalculateDoorBounds()
        {
            if (occupiedTiles.Count == 0)
            {
                return new Bounds(transform.position, Vector3.one);
            }

            Vector3 min = parentFloor.TileToWorld(occupiedTiles[0]);
            Vector3 max = min;

            foreach (var tile in occupiedTiles)
            {
                Vector3 worldPos = parentFloor.TileToWorld(tile);
                min = Vector3.Min(min, worldPos);
                max = Vector3.Max(max, worldPos);
            }

            // Expand slightly to ensure we catch the nodes
            Vector3 center = (min + max) / 2f;
            Vector3 size = (max - min) + Vector3.one * 1.1f;

            return new Bounds(center, size);
        }

        #endregion

        #region Queries

        /// <summary>
        /// Check if this door occupies a specific tile.
        /// </summary>
        public bool OccupiesTile(Vector3Int tile)
        {
            return occupiedTiles.Contains(tile);
        }

        /// <summary>
        /// Check if a world position is at this door.
        /// </summary>
        public bool IsAtPosition(Vector3 worldPosition)
        {
            Vector3Int tile = parentFloor.WorldToTile(worldPosition);
            return occupiedTiles.Contains(tile);
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Color based on state: red=locked, yellow=closed, green=open
            if (isLocked)
                Gizmos.color = Color.red;
            else if (isOpen)
                Gizmos.color = Color.green;
            else
                Gizmos.color = Color.yellow;

            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);

            // Draw additional tiles
            if (Application.isPlaying && parentFloor != null)
            {
                foreach (var tile in occupiedTiles)
                {
                    Vector3 worldPos = parentFloor.TileToWorld(tile);
                    Gizmos.DrawWireCube(worldPos, Vector3.one * 0.7f);
                }
            }
            else
            {
                // In editor, show relative offsets
                foreach (var offset in additionalTileOffsets)
                {
                    Vector3 offsetPos = transform.position + new Vector3(offset.x, offset.y, 0);
                    Gizmos.DrawWireCube(offsetPos, Vector3.one * 0.7f);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            string state = isLocked ? "LOCKED" : (isOpen ? "OPEN" : "CLOSED");
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f,
                $"{doorId}\n{state}");
        }
#endif
    }
}
