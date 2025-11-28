using UnityEngine;
using Pathfinding;
using Starbelter.Pathfinding;

namespace Starbelter.Core
{
    /// <summary>
    /// Main game manager. Initializes systems in correct order.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Initialization")]
        [Tooltip("Automatically bake cover on start")]
        [SerializeField] private bool bakeCoverOnStart = true;

        [Tooltip("Automatically scan A* graph on start")]
        [SerializeField] private bool scanGraphOnStart = true;

        [Header("References")]
        [SerializeField] private AstarPath astarPath;
        [SerializeField] private CoverBaker coverBaker;
        [SerializeField] private CoverQuery coverQuery;
        [SerializeField] private TileOccupancy tileOccupancy;

        public bool IsInitialized { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Find references if not assigned
            if (astarPath == null) astarPath = FindFirstObjectByType<AstarPath>();
            if (coverBaker == null) coverBaker = FindFirstObjectByType<CoverBaker>();
            if (coverQuery == null) coverQuery = FindFirstObjectByType<CoverQuery>();
            if (tileOccupancy == null) tileOccupancy = FindFirstObjectByType<TileOccupancy>();
        }

        private void Start()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes all game systems in the correct order.
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized) return;

            Debug.Log("[GameManager] Initializing systems...");

            // 1. Scan A* graph first (determines walkable areas)
            if (scanGraphOnStart && astarPath != null)
            {
                Debug.Log("[GameManager] Scanning A* graphs...");
                astarPath.Scan();
            }

            // 2. Bake cover data (depends on obstacles being defined)
            if (bakeCoverOnStart && coverBaker != null)
            {
                Debug.Log("[GameManager] Baking cover data...");
                coverBaker.BakeAll();
            }

            IsInitialized = true;
            Debug.Log("[GameManager] Initialization complete");
        }

        /// <summary>
        /// Rebakes the pathfinding graph and cover data.
        /// Call after significant map changes (buildings destroyed, etc.)
        /// </summary>
        public void RebakeAll()
        {
            Debug.Log("[GameManager] Rebaking all...");

            if (astarPath != null)
            {
                astarPath.Scan();
            }

            if (coverBaker != null)
            {
                coverBaker.BakeAll();
            }
        }

        /// <summary>
        /// Updates a local area after cover is destroyed.
        /// More performant than full rebake for small changes.
        /// </summary>
        public void UpdateLocalArea(Vector3 center, float radius)
        {
            if (astarPath != null)
            {
                // Update only the affected graph nodes
                var bounds = new Bounds(center, Vector3.one * radius * 2);
                astarPath.UpdateGraphs(bounds);
            }

            // Full cover rebake for now - could optimize later
            if (coverBaker != null)
            {
                coverBaker.BakeAll();
            }
        }

        /// <summary>
        /// Called when a cover object is destroyed.
        /// </summary>
        public void OnCoverDestroyed(GameObject coverObject, Vector3 position, float radius = 3f)
        {
            // Remove cover data for this object
            if (coverBaker != null)
            {
                coverBaker.RemoveCoverObject(coverObject);
            }

            // Update pathfinding in local area
            if (astarPath != null)
            {
                var bounds = new Bounds(position, Vector3.one * radius * 2);
                astarPath.UpdateGraphs(bounds);
            }
        }
    }
}
