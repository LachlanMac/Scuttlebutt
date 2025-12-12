using UnityEngine;
using System.Collections.Generic;
using Starbelter.Arena;

namespace Starbelter.Core
{
    /// <summary>
    /// Unified manager for the game world - handles both space and arena layers.
    /// Responsible for loading/spawning entities and managing transitions between layers.
    /// </summary>
    public class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance { get; private set; }

        [Header("Scene Roots")]
        [SerializeField] private Transform spaceRoot;
        [SerializeField] private Transform arenasRoot;

        [Header("Layer Settings")]
        [SerializeField] private string spaceLayerName = "Space";

        [Header("Test Spawns")]
        [Tooltip("Entities to spawn on Start for testing")]
        [SerializeField] private List<SpawnEntry> testSpawns = new List<SpawnEntry>();

        // Runtime state
        private Dictionary<string, WorldEntity> entities = new Dictionary<string, WorldEntity>();
        private int spaceLayer;

        // Events
        public event System.Action<WorldEntity> OnEntitySpawned;
        public event System.Action<WorldEntity> OnEntityDestroyed;

        // Properties
        public Transform SpaceRoot => spaceRoot;
        public Transform ArenasRoot => arenasRoot;
        public int SpaceLayer => spaceLayer;
        public IReadOnlyDictionary<string, WorldEntity> Entities => entities;

        #region Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            spaceLayer = LayerMask.NameToLayer(spaceLayerName);
            if (spaceLayer == -1)
            {
                Debug.LogWarning($"[WorldManager] Layer '{spaceLayerName}' not found");
            }

            EnsureRootsExist();
        }

        private void Start()
        {
            // Spawn test entities
            foreach (var entry in testSpawns)
            {
                SpawnEntity(entry);
            }
        }

        private void EnsureRootsExist()
        {
            if (spaceRoot == null)
            {
                var go = GameObject.Find("Space") ?? new GameObject("Space");
                spaceRoot = go.transform;
            }

            if (arenasRoot == null)
            {
                var go = GameObject.Find("Arenas") ?? new GameObject("Arenas");
                arenasRoot = go.transform;
            }
        }

        #endregion

        #region Spawning

        /// <summary>
        /// Spawn an entity from a SpawnEntry.
        /// Handles all combinations: arena+space, arena-only, space-only.
        /// </summary>
        public WorldEntity SpawnEntity(SpawnEntry entry)
        {
            return SpawnEntity(entry.arenaPrefab, entry.spacePrefab, entry.spacePosition, entry.entityName);
        }

        /// <summary>
        /// Spawn an entity with optional arena and space prefab.
        /// </summary>
        public WorldEntity SpawnEntity(
            GameObject arenaPrefab,
            GameObject spacePrefab,
            Vector3 spacePosition,
            string entityName = null)
        {
            // Validate - need at least one prefab
            if (arenaPrefab == null && spacePrefab == null)
            {
                Debug.LogError("[WorldManager] Cannot spawn entity with no prefabs");
                return null;
            }

            var entity = new WorldEntity();
            entity.Name = entityName ?? GenerateEntityName(arenaPrefab, spacePrefab);

            // Spawn arena if provided
            if (arenaPrefab != null)
            {
                var arenaInstance = Instantiate(arenaPrefab, arenasRoot);
                arenaInstance.name = $"{entity.Name}_Arena";

                entity.ArenaObject = arenaInstance;
                entity.Arena = arenaInstance.GetComponent<Arena.Arena>();

                // Check if arena has a linked space prefab
                if (spacePrefab == null)
                {
                    var linker = arenaInstance.GetComponent<ArenaSpaceLinker>();
                    if (linker != null && linker.SpaceViewPrefab != null)
                    {
                        spacePrefab = linker.SpaceViewPrefab;
                    }
                }
            }

            // Spawn space object if provided
            if (spacePrefab != null)
            {
                var spaceInstance = Instantiate(spacePrefab, spacePosition, Quaternion.identity, spaceRoot);
                spaceInstance.name = $"{entity.Name}_Space";
                SetLayerRecursive(spaceInstance, spaceLayer);

                entity.SpaceObject = spaceInstance;
                entity.ShipController = spaceInstance.GetComponent<ShipController>();

                // Link ShipController to Arena if both exist
                if (entity.ShipController != null && entity.Arena != null)
                {
                    entity.ShipController.Initialize(entity.Arena);
                }
            }

            // Register entity
            entities[entity.Name] = entity;

            Debug.Log($"[WorldManager] Spawned entity '{entity.Name}' " +
                $"(Arena: {entity.HasArena}, Space: {entity.HasSpaceObject})");

            OnEntitySpawned?.Invoke(entity);
            return entity;
        }

        /// <summary>
        /// Spawn a space-only entity (starfighter, asteroid, etc.)
        /// </summary>
        public WorldEntity SpawnSpaceOnly(GameObject spacePrefab, Vector3 position, string name = null)
        {
            return SpawnEntity(null, spacePrefab, position, name);
        }

        /// <summary>
        /// Spawn an arena-only entity (station interior, building, etc.)
        /// </summary>
        public WorldEntity SpawnArenaOnly(GameObject arenaPrefab, string name = null)
        {
            return SpawnEntity(arenaPrefab, null, Vector3.zero, name);
        }

        private string GenerateEntityName(GameObject arenaPrefab, GameObject spacePrefab)
        {
            var baseName = arenaPrefab?.name ?? spacePrefab?.name ?? "Entity";
            baseName = baseName.Replace("_Arena", "").Replace("_Space", "").Replace("Prefab", "");
            return $"{baseName}_{System.Guid.NewGuid().ToString().Substring(0, 4)}";
        }

        #endregion

        #region Destruction

        /// <summary>
        /// Destroy an entity completely.
        /// </summary>
        public void DestroyEntity(WorldEntity entity)
        {
            if (entity == null) return;

            Debug.Log($"[WorldManager] Destroying entity '{entity.Name}'");

            if (entity.ArenaObject != null)
                Destroy(entity.ArenaObject);

            if (entity.SpaceObject != null)
                Destroy(entity.SpaceObject);

            entities.Remove(entity.Name);
            OnEntityDestroyed?.Invoke(entity);
        }

        /// <summary>
        /// Destroy entity by name.
        /// </summary>
        public void DestroyEntity(string entityName)
        {
            if (entities.TryGetValue(entityName, out var entity))
            {
                DestroyEntity(entity);
            }
        }

        #endregion

        #region Queries

        /// <summary>
        /// Get an entity by name.
        /// </summary>
        public WorldEntity GetEntity(string name)
        {
            entities.TryGetValue(name, out var entity);
            return entity;
        }

        /// <summary>
        /// Find entity by its arena.
        /// </summary>
        public WorldEntity GetEntityByArena(Arena.Arena arena)
        {
            foreach (var kvp in entities)
            {
                if (kvp.Value.Arena == arena)
                    return kvp.Value;
            }
            return null;
        }

        /// <summary>
        /// Find entity by its ShipController.
        /// </summary>
        public WorldEntity GetEntityByShipController(ShipController controller)
        {
            foreach (var kvp in entities)
            {
                if (kvp.Value.ShipController == controller)
                    return kvp.Value;
            }
            return null;
        }

        /// <summary>
        /// Get all entities with space presence.
        /// </summary>
        public List<WorldEntity> GetSpaceEntities()
        {
            var result = new List<WorldEntity>();
            foreach (var kvp in entities)
            {
                if (kvp.Value.HasSpaceObject)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// Get all entities with arena interiors.
        /// </summary>
        public List<WorldEntity> GetArenaEntities()
        {
            var result = new List<WorldEntity>();
            foreach (var kvp in entities)
            {
                if (kvp.Value.HasArena)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// Get all ships (entities with ShipController).
        /// </summary>
        public List<WorldEntity> GetShips()
        {
            var result = new List<WorldEntity>();
            foreach (var kvp in entities)
            {
                if (kvp.Value.ShipController != null)
                    result.Add(kvp.Value);
            }
            return result;
        }

        #endregion

        #region Utility

        private void SetLayerRecursive(GameObject obj, int layer)
        {
            if (layer < 0) return;

            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents a complete entity in the world (may have arena, space object, or both).
    /// </summary>
    public class WorldEntity
    {
        public string Name;

        // Arena layer (interior)
        public GameObject ArenaObject;
        public Arena.Arena Arena;

        // Space layer (exterior)
        public GameObject SpaceObject;
        public ShipController ShipController;

        // Convenience
        public bool HasArena => ArenaObject != null;
        public bool HasSpaceObject => SpaceObject != null;
        public bool IsShip => ShipController != null;
        public bool IsValid => HasArena || HasSpaceObject;

        /// <summary>
        /// Get the space position (from SpaceObject or Vector3.zero).
        /// </summary>
        public Vector3 SpacePosition
        {
            get => SpaceObject != null ? SpaceObject.transform.position : Vector3.zero;
            set { if (SpaceObject != null) SpaceObject.transform.position = value; }
        }
    }

    /// <summary>
    /// Data for spawning an entity.
    /// </summary>
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("Interior prefab (null for space-only entities like starfighters)")]
        public GameObject arenaPrefab;

        [Tooltip("Space prefab (null for interior-only entities, or leave null to use ArenaSpaceLinker)")]
        public GameObject spacePrefab;

        [Tooltip("Position in space")]
        public Vector3 spacePosition;

        [Tooltip("Entity name (auto-generated if empty)")]
        public string entityName;
    }
}
