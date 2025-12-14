using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;
using Starbelter.Arena;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Manages the current sector (the active scene).
    /// - Loads galaxy data into memory on startup
    /// - Spawns/despawns POIs and ships for the current sector
    /// - Handles entity spawning (arena + space combinations)
    /// - Manages simulation levels based on player position
    /// </summary>
    public class SectorManager : MonoBehaviour
    {
        [Header("Galaxy Loading")]
        [SerializeField] private bool autoLoadOnStart = true;
        [SerializeField] private Vector2Int startingSector = new Vector2Int(5, 5);

        [Header("Scene Roots")]
        [SerializeField] private Transform poiParent;
        [SerializeField] private Transform shipParent;
        [SerializeField] private Transform arenasRoot;
        [SerializeField] private string spaceLayerName = "Space";

        [Header("Simulation")]
        [SerializeField] private float detailedSimRadius = 10000f;
        [SerializeField] private float tickInterval = 1f;

        [Header("Testing")]
        [Tooltip("Entities to spawn on Start for testing (arena/space prefab combos)")]
        [SerializeField] private List<SpawnEntry> testSpawns = new List<SpawnEntry>();

        [Header("Debug")]
        [SerializeField] private bool showChunkGrid = false;
        [SerializeField] private bool showGravityWells = false;

        // Galaxy data (loaded once, stays in memory)
        public GalaxyData Galaxy { get; private set; }
        public bool IsGalaxyLoaded => Galaxy != null;

        // Current sector state
        public Sector CurrentSector { get; private set; }
        public ShipRecord PlayerShip { get; private set; }

        // Entity tracking
        private Dictionary<string, WorldEntity> entities = new Dictionary<string, WorldEntity>();
        private int spaceLayer;

        // Tick tracking
        private float tickTimer;

        // Events
        public event System.Action<WorldEntity> OnEntitySpawned;
        public event System.Action<WorldEntity> OnEntityDestroyed;

        // Properties
        public Transform SpaceRoot => poiParent;
        public Transform ArenasRoot => arenasRoot;
        public int SpaceLayer => spaceLayer;
        public IReadOnlyDictionary<string, WorldEntity> Entities => entities;

        private static SectorManager instance;
        public static SectorManager Instance => instance;

        #region Lifecycle

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            // Initialize factions
            Factions.Initialize();

            // Setup space layer
            spaceLayer = LayerMask.NameToLayer(spaceLayerName);
            if (spaceLayer == -1)
            {
                Debug.LogWarning($"[SectorManager] Layer '{spaceLayerName}' not found");
            }

            EnsureRootsExist();
        }

        private void Start()
        {
            // Load galaxy/sector first
            if (autoLoadOnStart)
            {
                LoadGalaxyData();
                LoadSector(startingSector.x, startingSector.y);
            }

            // Then spawn test entities (so we can position relative to sector content)
            foreach (var entry in testSpawns)
            {
                // If no position specified, try to spawn near a station
                Vector3 spawnPos = entry.spacePosition;
                if (spawnPos == Vector3.zero && CurrentSector != null)
                {
                    var station = FindRandomStation();
                    if (station != null)
                    {
                        // Spawn near the station with some offset
                        spawnPos = (Vector3)(Vector2)station.position + new Vector3(500f, 300f, 0f);
                    }
                }

                SpawnEntity(entry.arenaPrefab, entry.spacePrefab, spawnPos, entry.entityName);
            }
        }

        private Station FindRandomStation()
        {
            if (CurrentSector == null) return null;

            var stations = new List<Station>();
            foreach (var poi in CurrentSector.AllPOIs)
            {
                if (poi is Station s)
                    stations.Add(s);
            }

            if (stations.Count == 0) return null;
            return stations[UnityEngine.Random.Range(0, stations.Count)];
        }

        private void Update()
        {
            if (CurrentSector == null) return;

            UpdateSimulationLevels();

            tickTimer += Time.deltaTime;
            if (tickTimer >= tickInterval)
            {
                tickTimer = 0;
                TickAbstractShips();
            }
        }

        private void EnsureRootsExist()
        {
            if (poiParent == null)
            {
                var go = GameObject.Find("Space") ?? new GameObject("Space");
                poiParent = go.transform;
            }

            if (arenasRoot == null)
            {
                var go = GameObject.Find("Arenas") ?? new GameObject("Arenas");
                arenasRoot = go.transform;
            }

            if (shipParent == null)
            {
                shipParent = poiParent;
            }
        }

        #endregion

        #region Galaxy Loading

        public void LoadGalaxyData()
        {
            if (IsGalaxyLoaded)
            {
                Debug.LogWarning("[SectorManager] Galaxy already loaded");
                return;
            }

            Debug.Log("[SectorManager] Loading galaxy data...");
            GalaxyLoader.LoadGalaxy();
            Galaxy = GalaxyLoader.Galaxy;

            if (Galaxy == null)
            {
                Debug.LogError("[SectorManager] Failed to load galaxy data!");
                return;
            }

            Debug.Log($"[SectorManager] Galaxy '{Galaxy.galaxyName}' loaded (seed: {Galaxy.seed})");

            // Future: Load additional data layers here
            // LoadShipData();
            // LoadFleetData();
        }

        private void LoadShipData()
        {
            // TODO: Load ship records from JSON
            Debug.Log("[SectorManager] Ship data loading not yet implemented");
        }

        #endregion

        #region Sector Loading

        public void LoadSector(int x, int y)
        {
            if (!IsGalaxyLoaded)
            {
                Debug.LogError("[SectorManager] Cannot load sector - galaxy not loaded");
                return;
            }

            var sector = Galaxy.GetSector(x, y);
            if (sector == null)
            {
                Debug.LogError($"[SectorManager] Sector [{x},{y}] not found in galaxy");
                return;
            }

            LoadSectorInternal(sector);
        }

        public void LoadSector(Vector2Int coord)
        {
            LoadSector(coord.x, coord.y);
        }

        public void LoadSector(string sectorId)
        {
            if (!IsGalaxyLoaded)
            {
                Debug.LogError("[SectorManager] Cannot load sector - galaxy not loaded");
                return;
            }

            if (sectorId.StartsWith("sector_"))
            {
                var parts = sectorId.Split('_');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[1], out int x) &&
                    int.TryParse(parts[2], out int y))
                {
                    LoadSector(x, y);
                    return;
                }
            }

            Debug.LogError($"[SectorManager] Invalid sector ID format: {sectorId}");
        }

        private void LoadSectorInternal(Sector sector)
        {
            if (CurrentSector != null)
            {
                UnloadCurrentSector();
            }

            CurrentSector = sector;
            Debug.Log($"[SectorManager] Loading sector: {sector.displayName} [{sector.galaxyCoord.x},{sector.galaxyCoord.y}]");

            foreach (var poi in sector.AllPOIs)
            {
                SpawnPOI(poi);
            }

            foreach (var ship in sector.ShipsPresent)
            {
                SpawnShip(ship);
            }

            Debug.Log($"[SectorManager] Loaded {sector.AllPOIs.Count} POIs, {sector.ShipsPresent.Count} ships");
        }

        public void UnloadCurrentSector()
        {
            if (CurrentSector == null) return;

            Debug.Log($"[SectorManager] Unloading sector: {CurrentSector.displayName}");

            foreach (var poi in CurrentSector.AllPOIs)
            {
                DespawnPOI(poi);
            }

            foreach (var ship in CurrentSector.ShipsPresent)
            {
                DespawnShip(ship);
            }

            CurrentSector = null;
        }

        #endregion

        #region Entity Spawning (Arena + Space)

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
            if (arenaPrefab == null && spacePrefab == null)
            {
                Debug.LogError("[SectorManager] Cannot spawn entity with no prefabs");
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
                // Parent to shipParent for ships, poiParent for other space objects
                var parent = spacePrefab.GetComponent<ShipController>() != null ? shipParent : poiParent;
                var spaceInstance = Instantiate(spacePrefab, spacePosition, Quaternion.identity, parent);
                spaceInstance.name = $"{entity.Name}_Space";
                SetLayerRecursive(spaceInstance, spaceLayer);

                entity.SpaceObject = spaceInstance;
                entity.ShipController = spaceInstance.GetComponent<ShipController>();

                // Link ShipController to Arena if both exist
                if (entity.ShipController != null && entity.Arena != null)
                {
                    entity.ShipController.Initialize(entity.Arena);
                }

                // Link Arena to SpaceVessel (for hangar operations)
                var spaceVessel = spaceInstance.GetComponent<Space.SpaceVessel>();
                if (spaceVessel != null && entity.Arena != null)
                {
                    entity.Arena.SetParentVessel(spaceVessel);
                    spaceVessel.SetInteriorArena(entity.Arena);
                }
            }

            entities[entity.Name] = entity;

            Debug.Log($"[SectorManager] Spawned entity '{entity.Name}' " +
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

        #region Entity Destruction

        public void DestroyEntity(WorldEntity entity)
        {
            if (entity == null) return;

            Debug.Log($"[SectorManager] Destroying entity '{entity.Name}'");

            if (entity.ArenaObject != null)
                Destroy(entity.ArenaObject);

            if (entity.SpaceObject != null)
                Destroy(entity.SpaceObject);

            entities.Remove(entity.Name);
            OnEntityDestroyed?.Invoke(entity);
        }

        public void DestroyEntity(string entityName)
        {
            if (entities.TryGetValue(entityName, out var entity))
            {
                DestroyEntity(entity);
            }
        }

        #endregion

        #region Entity Queries

        public WorldEntity GetEntity(string name)
        {
            entities.TryGetValue(name, out var entity);
            return entity;
        }

        public WorldEntity GetEntityByArena(Arena.Arena arena)
        {
            foreach (var kvp in entities)
            {
                if (kvp.Value.Arena == arena)
                    return kvp.Value;
            }
            return null;
        }

        public WorldEntity GetEntityByShipController(ShipController controller)
        {
            foreach (var kvp in entities)
            {
                if (kvp.Value.ShipController == controller)
                    return kvp.Value;
            }
            return null;
        }

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

        #region POI Management

        private void SpawnPOI(PointOfInterest poi)
        {
            if (poi.spawnedObject != null)
            {
                Debug.LogWarning($"[SectorManager] POI {poi.displayName} already spawned - skipping");
                return;
            }

            GameObject obj = CreatePOIGameObject(poi);
            obj.transform.position = (Vector3)(Vector2)poi.position;

            if (poiParent != null)
            {
                obj.transform.SetParent(poiParent);
            }

            poi.OnSpawned(obj);
        }

        private void DespawnPOI(PointOfInterest poi)
        {
            if (poi.spawnedObject != null)
            {
                Destroy(poi.spawnedObject);
                poi.OnDespawned();
            }
        }

        private GameObject CreatePOIGameObject(PointOfInterest poi)
        {
            if (poi is AsteroidBelt belt)
            {
                return CreateAsteroidBelt(belt, PlanetSprites.Instance);
            }

            GameObject obj = new GameObject(poi.displayName);
            obj.layer = LayerMask.NameToLayer("Space");
            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -1;

            Sprite sprite = null;
            float size = 5f;
            Color fallbackColor = Color.white;

            switch (poi)
            {
                case Planet planet:
                    fallbackColor = Color.green;
                    size = planet.size > 0 ? planet.size : 60f;
                    sprite = LoadPlanetSprite(planet.spriteName, planet.planetType);
                    break;

                case Moon moon:
                    fallbackColor = Color.gray;
                    size = moon.size > 0 ? moon.size : 20f;
                    sprite = LoadMoonSprite(moon.spriteName);
                    break;

                case Nebula nebula:
                    fallbackColor = new Color(0.5f, 0.2f, 0.8f, 0.5f);
                    size = nebula.radius * 0.5f;
                    break;

                case Anomaly:
                    fallbackColor = Color.yellow;
                    size = 10f;
                    break;

                case Station station:
                    var stationPrefab = StationPrefabLoader.GetPrefab(station.stationType);
                    if (stationPrefab != null)
                    {
                        var stationObj = Object.Instantiate(stationPrefab);
                        stationObj.name = poi.displayName;
                        stationObj.layer = LayerMask.NameToLayer("Space");
                        float scale = StationPrefabLoader.GetScaleMultiplier(station.stationType);
                        stationObj.transform.localScale *= scale;
                        return stationObj;
                    }
                    fallbackColor = Factions.Get(station.controlledBy)?.factionColor ?? Color.cyan;
                    size = StationPrefabLoader.GetSizeForType(station.stationType) switch
                    {
                        "Large" => 20f,
                        "Medium" => 12f,
                        _ => 6f
                    };
                    break;

                case DebrisField debris:
                    fallbackColor = new Color(0.4f, 0.3f, 0.3f);
                    size = debris.radius * 0.3f;
                    break;

                default:
                    fallbackColor = Color.white;
                    size = 5f;
                    break;
            }

            if (sprite != null)
            {
                sr.sprite = sprite;
            }
            else
            {
                sr.color = fallbackColor;
            }

            obj.transform.localScale = Vector3.one * size;
            return obj;
        }

        private Sprite LoadPlanetSprite(string spriteName, PlanetType planetType)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            // Determine folder based on planet type
            string folder = planetType switch
            {
                PlanetType.Gas => "Gas",
                PlanetType.Terran => "Habitable",
                PlanetType.Ocean => "Habitable",
                _ => "Uninhabitable"
            };

            string path = $"Planets/{folder}/{spriteName}";
            return Resources.Load<Sprite>(path);
        }

        private Sprite LoadMoonSprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            // Moons can use sprites from either Habitable or Uninhabitable
            // Try Habitable first, then Uninhabitable
            var sprite = Resources.Load<Sprite>($"Planets/Habitable/{spriteName}");
            if (sprite == null)
            {
                sprite = Resources.Load<Sprite>($"Planets/Uninhabitable/{spriteName}");
            }
            return sprite;
        }

        private GameObject CreateAsteroidBelt(AsteroidBelt belt, PlanetSprites sprites)
        {
            GameObject beltObj = new GameObject(belt.displayName);
            beltObj.layer = LayerMask.NameToLayer("Space");

            int asteroidCount = belt.density switch
            {
                AsteroidDensity.Sparse => Random.Range(80, 120),
                AsteroidDensity.Medium => Random.Range(150, 250),
                AsteroidDensity.Dense => Random.Range(300, 450),
                AsteroidDensity.Extreme => Random.Range(500, 700),
                _ => 150
            };

            for (int i = 0; i < asteroidCount; i++)
            {
                GameObject asteroid = new GameObject($"Asteroid_{i}");
                asteroid.layer = LayerMask.NameToLayer("Space");
                asteroid.transform.SetParent(beltObj.transform);

                var sr = asteroid.AddComponent<SpriteRenderer>();
                sr.sortingOrder = -1;

                float angle = Random.Range(0f, Mathf.PI * 2f);
                float midpoint = (belt.innerRadius + belt.outerRadius) / 2f;
                float spread = (belt.outerRadius - belt.innerRadius) / 2f * 0.5f;
                float distance = Random.Range(midpoint - spread, midpoint + spread);
                asteroid.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * distance,
                    Mathf.Sin(angle) * distance,
                    0
                );

                asteroid.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

                float size = Random.Range(1f, 10f);
                asteroid.transform.localScale = Vector3.one * size;

                if (sprites != null)
                {
                    Sprite sprite = sprites.GetRandomAsteroidSprite();
                    if (sprite != null)
                    {
                        sr.sprite = sprite;
                    }
                    else
                    {
                        sr.color = new Color(0.5f, 0.4f, 0.3f);
                    }
                }
                else
                {
                    sr.color = new Color(
                        Random.Range(0.3f, 0.6f),
                        Random.Range(0.3f, 0.5f),
                        Random.Range(0.2f, 0.4f)
                    );
                }
            }

            return beltObj;
        }

        #endregion

        #region Ship Management

        public void SetPlayerShip(ShipRecord ship)
        {
            PlayerShip = ship;
            ship.simLevel = SimulationLevel.Full;
        }

        public void AddShipToSector(ShipRecord ship)
        {
            if (CurrentSector == null) return;

            CurrentSector.AddShip(ship);
            SpawnShip(ship);
        }

        public void RemoveShipFromSector(ShipRecord ship)
        {
            if (CurrentSector == null) return;

            DespawnShip(ship);
            CurrentSector.RemoveShip(ship);
        }

        private void SpawnShip(ShipRecord ship)
        {
            var faction = Factions.Get(ship.factionId);
            string prefabPath = $"Prefabs/Ships/{faction?.shortName ?? "ZE"}/{ship.shipClass}_Space";
            GameObject prefab = Resources.Load<GameObject>(prefabPath);

            if (prefab == null)
            {
                prefab = CreateShipPlaceholder(ship);
            }

            GameObject obj = Instantiate(prefab, (Vector3)(Vector2)ship.position, Quaternion.identity, shipParent);
            obj.name = ship.shipName;
            ship.spawnedObject = obj;
            ship.shipController = obj.GetComponent<ShipController>();
        }

        private void DespawnShip(ShipRecord ship)
        {
            if (ship.spawnedObject != null)
            {
                Destroy(ship.spawnedObject);
                ship.spawnedObject = null;
                ship.shipController = null;
            }
        }

        private GameObject CreateShipPlaceholder(ShipRecord ship)
        {
            GameObject placeholder = new GameObject("ShipPlaceholder");
            var sr = placeholder.AddComponent<SpriteRenderer>();

            var faction = Factions.Get(ship.factionId);
            sr.color = faction?.factionColor ?? Color.white;

            float scale = ship.shipClass switch
            {
                ShipClass.Corvette => 3f,
                ShipClass.Frigate => 4f,
                ShipClass.Destroyer => 6f,
                ShipClass.Cruiser => 8f,
                ShipClass.Battleship => 10f,
                _ => 5f
            };
            placeholder.transform.localScale = Vector3.one * scale;

            return placeholder;
        }

        #endregion

        #region Simulation

        private void UpdateSimulationLevels()
        {
            if (PlayerShip == null) return;

            foreach (var ship in CurrentSector.ShipsPresent)
            {
                if (ship == PlayerShip) continue;

                float distance = Vector2.Distance(ship.position, PlayerShip.position);
                SimulationLevel targetLevel = distance <= detailedSimRadius
                    ? SimulationLevel.Detailed
                    : SimulationLevel.Abstract;

                if (ship.simLevel != targetLevel)
                {
                    if (targetLevel > ship.simLevel)
                        ship.Promote(targetLevel);
                    else
                        ship.Demote(targetLevel);
                }
            }
        }

        private void TickAbstractShips()
        {
            foreach (var ship in CurrentSector.ShipsPresent)
            {
                if (ship.simLevel == SimulationLevel.Full) continue;
                ship.ConsumeResources(tickInterval / 60f);
            }
        }

        #endregion

        #region Jumping

        public bool CanJump(ShipRecord ship)
        {
            if (CurrentSector == null) return false;
            return !CurrentSector.IsInGravityWell(ship.position);
        }

        public bool JumpToSector(ShipRecord ship, int targetX, int targetY, Vector2? arrivalPosition = null)
        {
            if (!IsGalaxyLoaded || CurrentSector == null) return false;

            if (CurrentSector.IsInGravityWell(ship.position))
            {
                Debug.LogWarning($"[SectorManager] {ship.shipName} cannot jump - inside gravity well");
                return false;
            }

            var targetSector = Galaxy.GetSector(targetX, targetY);
            if (targetSector == null)
            {
                Debug.LogError($"[SectorManager] Target sector [{targetX},{targetY}] not found");
                return false;
            }

            RemoveShipFromSector(ship);
            ship.position = arrivalPosition ?? Vector2.zero;
            targetSector.AddShip(ship);

            if (ship == PlayerShip)
            {
                LoadSector(targetX, targetY);
            }

            Debug.Log($"[SectorManager] {ship.shipName} jumped to {targetSector.displayName}");
            return true;
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

        #region Debug

        private void OnDrawGizmos()
        {
            if (CurrentSector == null) return;

            if (showChunkGrid) DrawChunkGrid();
            if (showGravityWells) DrawGravityWells();
        }

        private void DrawChunkGrid()
        {
            Gizmos.color = new Color(0.3f, 0.3f, 0.3f, 0.3f);

            for (int x = 0; x <= Sector.CHUNKS_PER_AXIS; x++)
            {
                float xPos = -Sector.HALF_SECTOR + (x * Sector.CHUNK_SIZE);
                Gizmos.DrawLine(
                    new Vector3(xPos, -Sector.HALF_SECTOR, 0),
                    new Vector3(xPos, Sector.HALF_SECTOR, 0)
                );
            }

            for (int y = 0; y <= Sector.CHUNKS_PER_AXIS; y++)
            {
                float yPos = -Sector.HALF_SECTOR + (y * Sector.CHUNK_SIZE);
                Gizmos.DrawLine(
                    new Vector3(-Sector.HALF_SECTOR, yPos, 0),
                    new Vector3(Sector.HALF_SECTOR, yPos, 0)
                );
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(Sector.SECTOR_SIZE, Sector.SECTOR_SIZE, 0));
        }

        private void DrawGravityWells()
        {
            if (CurrentSector?.AllPOIs == null) return;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);

            foreach (var poi in CurrentSector.AllPOIs)
            {
                if (poi is Planet planet)
                {
                    Gizmos.DrawWireSphere((Vector3)(Vector2)planet.position, planet.gravityWellRadius);
                }
            }
        }

        #endregion

        #region Editor Helpers

        [ContextMenu("Reload Current Sector")]
        public void ReloadCurrentSector()
        {
            if (CurrentSector != null)
            {
                var coord = CurrentSector.galaxyCoord;
                UnloadCurrentSector();

                GalaxyLoader.Reload();
                Galaxy = GalaxyLoader.Galaxy;

                LoadSector(coord.x, coord.y);
            }
        }

        [ContextMenu("Load Starting Sector")]
        public void LoadStartingSector()
        {
            if (!IsGalaxyLoaded)
            {
                LoadGalaxyData();
            }
            LoadSector(startingSector.x, startingSector.y);
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

        public Vector3 SpacePosition
        {
            get => SpaceObject != null ? SpaceObject.transform.position : Vector3.zero;
            set { if (SpaceObject != null) SpaceObject.transform.position = value; }
        }
    }

    /// <summary>
    /// Data for spawning an entity (used in Testing inspector).
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
