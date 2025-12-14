using UnityEngine;

namespace Starbelter.Ship
{
    /// <summary>
    /// Ship type category - determines if ship has interior, can dock, etc.
    /// Matches folder structure in Resources/Data/Ships/
    /// </summary>
    public enum ShipCategory
    {
        Fighter,        // Small single-seat craft (Starfighter)
        Bomber,         // Small attack craft, 1-2 crew
        Shuttle,        // Small crewed vessel (Personnel Shuttle, Dropship)
        Freighter,      // Cargo vessel, small crew
        Corvette,       // Small capital ship (Light Frigate)
        Destroyer,      // Medium capital ship
        Cruiser,        // Large capital ship
        Battleship,     // Largest combat vessels (Carrier, Dreadnought)
        Station         // Stationary (Stations, Platforms)
    }

    /// <summary>
    /// Pure data class defining a ship type's stats.
    /// Loaded from JSON via DataLoader.
    /// </summary>
    [System.Serializable]
    public class ShipData
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        public ShipCategory category;

        [Header("Movement")]
        public float maxSpeed = 20f;
        public float acceleration = 10f;
        public float turnRate = 180f;
        public float warpSpeed = 100f;

        [Header("Combat")]
        public float maxHull = 100f;
        public float maxShields = 50f;
        public float shieldRegenRate = 5f;

        [Header("Hangar/Docking")]
        public float approachSpeed = 3f;
        public float dockingSpeed = 5f;
        public float landingDuration = 4f;
        public bool canDock = true;         // Can this ship dock in hangars?
        public bool hasHangar = false;      // Does this ship have a hangar bay?

        [Header("Jump Drive")]
        public float jumpSpeed = 0f;           // Units per game-hour (0 = no jump drive)
        public float jumpFuelCapacity = 0f;    // Max fuel for jumping
        public float jumpFuelPerUnit = 0.1f;   // Fuel consumed per unit distance

        [Header("Prefabs (loaded by convention)")]
        [System.NonSerialized] public GameObject spacePrefab;
        [System.NonSerialized] public GameObject parkedPrefab;
        [System.NonSerialized] public GameObject arenaPrefab;  // Interior, if any

        /// <summary>
        /// Whether this ship type has a walkable interior.
        /// Shuttles and larger have interiors. Fighters/Bombers do not.
        /// </summary>
        public bool HasInterior => category >= ShipCategory.Shuttle;

        /// <summary>
        /// Whether this ship has a jump drive.
        /// </summary>
        public bool HasJumpDrive => jumpSpeed > 0f;

        /// <summary>
        /// Load prefabs from Resources based on category and variant.
        /// Call after loading from JSON.
        /// Prefabs expected at: Resources/Prefabs/Ships/{Category}/{variant}/Space.prefab, Parked.prefab, Arena.prefab
        /// </summary>
        public void LoadPrefabs()
        {
            // Extract variant from id (e.g., "destroyer_default" -> "default")
            string variant = GetVariantFromId(id);
            string basePath = $"Prefabs/Ships/{category}/{variant}";

            spacePrefab = Resources.Load<GameObject>($"{basePath}/Space");
            parkedPrefab = Resources.Load<GameObject>($"{basePath}/Parked");

            if (HasInterior)
            {
                arenaPrefab = Resources.Load<GameObject>($"{basePath}/Arena");
            }

            if (spacePrefab == null)
            {
                Debug.LogWarning($"[ShipData] No Space prefab found for '{id}' at {basePath}/Space");
            }
            if (canDock && parkedPrefab == null)
            {
                Debug.LogWarning($"[ShipData] No Parked prefab found for '{id}' at {basePath}/Parked");
            }
        }

        /// <summary>
        /// Extract variant name from ship ID.
        /// e.g., "destroyer_default" -> "default", "starfighter_a" -> "a"
        /// </summary>
        private string GetVariantFromId(string shipId)
        {
            if (string.IsNullOrEmpty(shipId)) return "default";

            int underscoreIndex = shipId.LastIndexOf('_');
            if (underscoreIndex >= 0 && underscoreIndex < shipId.Length - 1)
            {
                return shipId.Substring(underscoreIndex + 1);
            }

            // No underscore - use the whole id as variant
            return shipId;
        }

        /// <summary>
        /// Create a default ship data.
        /// </summary>
        public ShipData()
        {
            id = "unknown";
            displayName = "Unknown Ship";
            category = ShipCategory.Fighter;
        }

        /// <summary>
        /// Clone this ship data.
        /// </summary>
        public ShipData Clone()
        {
            var clone = new ShipData
            {
                id = this.id,
                displayName = this.displayName,
                category = this.category,
                maxSpeed = this.maxSpeed,
                acceleration = this.acceleration,
                turnRate = this.turnRate,
                warpSpeed = this.warpSpeed,
                maxHull = this.maxHull,
                maxShields = this.maxShields,
                shieldRegenRate = this.shieldRegenRate,
                approachSpeed = this.approachSpeed,
                dockingSpeed = this.dockingSpeed,
                landingDuration = this.landingDuration,
                canDock = this.canDock,
                hasHangar = this.hasHangar,
                jumpSpeed = this.jumpSpeed,
                jumpFuelCapacity = this.jumpFuelCapacity,
                jumpFuelPerUnit = this.jumpFuelPerUnit,
                // Prefabs are shared references, not cloned
                spacePrefab = this.spacePrefab,
                parkedPrefab = this.parkedPrefab,
                arenaPrefab = this.arenaPrefab
            };
            return clone;
        }
    }
}
