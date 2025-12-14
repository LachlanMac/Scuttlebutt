using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Manages all faction fleets. Creates the faction hierarchy and spawns ships.
    /// Attach to a "Factions" parent GameObject.
    /// </summary>
    public class FactionFleetManager : MonoBehaviour
    {
        public static FactionFleetManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private bool autoSpawnFleets = true;

        [Header("Runtime (Read Only)")]
        [SerializeField] private List<FactionController> factionControllers = new List<FactionController>();

        private Dictionary<FactionId, FactionController> controllerLookup = new Dictionary<FactionId, FactionController>();

        // Properties
        public IReadOnlyList<FactionController> AllFactions => factionControllers;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Initialize faction data
            Factions.Initialize();

            // Find existing faction controllers
            FindExistingControllers();
        }

        private void Start()
        {
            if (autoSpawnFleets)
            {
                InitializeAllFactions();
            }
        }

        /// <summary>
        /// Initialize all factions and spawn their fleets.
        /// </summary>
        public void InitializeAllFactions()
        {
            SetupAllFactions();

            foreach (var controller in factionControllers)
            {
                SpawnFactionShips(controller.FactionId);
            }

            Debug.Log($"[FactionFleetManager] Initialized {factionControllers.Count} factions with {TotalShipCount} total ships");
        }

        private void FindExistingControllers()
        {
            factionControllers.Clear();
            controllerLookup.Clear();

            foreach (Transform child in transform)
            {
                var controller = child.GetComponent<FactionController>();
                if (controller != null)
                {
                    factionControllers.Add(controller);
                    controllerLookup[controller.FactionId] = controller;
                }
            }
        }

        /// <summary>
        /// Get a faction controller by ID.
        /// </summary>
        public FactionController GetFaction(FactionId id)
        {
            return controllerLookup.TryGetValue(id, out var controller) ? controller : null;
        }

        /// <summary>
        /// Set up all factions with their hierarchies.
        /// </summary>
        [ContextMenu("Setup All Factions")]
        public void SetupAllFactions()
        {
            foreach (var factionData in Factions.GetAll())
            {
                if (!controllerLookup.ContainsKey(factionData.id))
                {
                    CreateFactionController(factionData.id);
                }
            }

            Debug.Log($"[FactionFleetManager] Setup complete: {factionControllers.Count} factions");
        }

        /// <summary>
        /// Create a faction controller if it doesn't exist.
        /// </summary>
        public FactionController CreateFactionController(FactionId factionId)
        {
            if (controllerLookup.TryGetValue(factionId, out var existing))
            {
                return existing;
            }

            var faction = Factions.Get(factionId);
            if (faction == null)
            {
                Debug.LogWarning($"[FactionFleetManager] Unknown faction: {factionId}");
                return null;
            }

            // Create faction GameObject
            var factionGO = new GameObject(faction.displayName);
            factionGO.transform.SetParent(transform);

            // Add controller
            var controller = factionGO.AddComponent<FactionController>();

            // Set faction ID via serialized field (need reflection or make it settable)
            // For now, we'll use a setup method
            SetupController(controller, factionId);

            factionControllers.Add(controller);
            controllerLookup[factionId] = controller;

            Debug.Log($"[FactionFleetManager] Created faction: {faction.displayName}");

            return controller;
        }

        private void SetupController(FactionController controller, FactionId factionId)
        {
            // Use reflection to set the private factionId field
            var field = typeof(FactionController).GetField("factionId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(controller, factionId);
        }

        /// <summary>
        /// Spawn ships for a faction based on its config.
        /// </summary>
        public void SpawnFactionShips(FactionId factionId)
        {
            var controller = GetFaction(factionId);
            if (controller == null)
            {
                controller = CreateFactionController(factionId);
            }

            var config = FactionConfigLoader.GetConfig(factionId);
            if (config?.ships == null)
            {
                Debug.LogWarning($"[FactionFleetManager] No ship config for {factionId}");
                return;
            }

            // Spawn ships based on config
            foreach (var (shipClass, count) in config.GetAllShipCounts())
            {
                for (int i = 0; i < count; i++)
                {
                    SpawnShip(controller, shipClass, i);
                }
            }

            controller.RefreshShipList();
            Debug.Log($"[FactionFleetManager] Spawned {config.ships.TotalShips} ships for {factionId}");
        }

        private void SpawnShip(FactionController faction, ShipClass shipClass, int index)
        {
            string shipTypeId = ShipClassToTypeId(shipClass);
            string shipName = $"{faction.DisplayName} {shipClass} {index + 1}";

            var shipGO = new GameObject(shipName);
            var missionController = shipGO.AddComponent<ShipMissionController>();

            // Set ship type via reflection
            var field = typeof(ShipMissionController).GetField("shipTypeId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(missionController, shipTypeId);

            // Load and assign prefabs from Resources using category/variant structure
            string categoryName = shipClass.ToString();  // "Destroyer", "Frigate", etc.
            string variant = GetVariantFromTypeId(shipTypeId);  // "default" from "destroyer_default"
            string prefabPath = $"Prefabs/Ships/{categoryName}/{variant}";

            var spacePrefab = Resources.Load<GameObject>($"{prefabPath}/Space");
            var arenaPrefab = Resources.Load<GameObject>($"{prefabPath}/Arena");
            missionController.SetPrefabs(spacePrefab, arenaPrefab);

            if (spacePrefab == null)
            {
                Debug.LogWarning($"[FactionFleetManager] No space prefab found at {prefabPath}/Space");
            }

            faction.RegisterShip(missionController);
        }

        private string ShipClassToTypeId(ShipClass shipClass)
        {
            return shipClass switch
            {
                ShipClass.Battleship => "carrier",
                ShipClass.Cruiser => "cruiser",
                ShipClass.Destroyer => "destroyer_default",
                ShipClass.Frigate => "frigate",
                ShipClass.Corvette => "corvette",
                _ => "frigate"
            };
        }

        private string GetVariantFromTypeId(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return "default";

            int underscoreIndex = typeId.LastIndexOf('_');
            if (underscoreIndex >= 0 && underscoreIndex < typeId.Length - 1)
            {
                return typeId.Substring(underscoreIndex + 1);
            }

            return typeId;
        }

        /// <summary>
        /// Get all ships across all factions in a sector.
        /// </summary>
        public IEnumerable<ShipMissionController> GetAllShipsInSector(Vector2Int sector)
        {
            foreach (var faction in factionControllers)
            {
                foreach (var ship in faction.GetShipsInSector(sector))
                {
                    yield return ship;
                }
            }
        }

        /// <summary>
        /// Get total ship count across all factions.
        /// </summary>
        public int TotalShipCount
        {
            get
            {
                int total = 0;
                foreach (var faction in factionControllers)
                {
                    total += faction.ShipCount;
                }
                return total;
            }
        }

        #region Debug

        [ContextMenu("Log Fleet Status")]
        private void LogFleetStatus()
        {
            Debug.Log($"[FactionFleetManager] {factionControllers.Count} factions, {TotalShipCount} total ships");
            foreach (var controller in factionControllers)
            {
                Debug.Log($"  - {controller.DisplayName}: {controller.ShipCount} ships");
            }
        }

        #endregion
    }
}
