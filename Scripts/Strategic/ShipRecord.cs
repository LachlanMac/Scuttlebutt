using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Abstract representation of a ship in the strategic layer.
    /// Used for Level 2-3 simulation (not fully loaded ships).
    /// </summary>
    [System.Serializable]
    public class ShipRecord
    {
        [Header("Identity")]
        public string id;
        public string shipName;             // "TFS Chimera"
        public ShipClass shipClass;
        public FactionId factionId;

        [Header("Command")]
        public string captainName;          // Just the name for display
        public string fleetId;              // Which fleet this belongs to

        [Header("Location")]
        public Vector2 position;            // Position in current sector
        public float rotation;              // Facing direction
        [System.NonSerialized]
        public Sector currentSector;

        [Header("Status")]
        public ShipStatus status;
        public float hullIntegrity = 100f;  // 0-100%
        public float fuelPercent = 100f;
        public float ammoPercent = 100f;
        public float suppliesPercent = 100f;

        [Header("Crew (Abstract)")]
        public int crewCount;               // Total crew aboard
        public int crewCapacity;            // Max crew

        [Header("Combat (Abstract)")]
        public int fighterCount;            // Fighters aboard
        public int fighterCapacity;
        public float combatRating;          // Overall combat effectiveness 0-100

        [Header("Simulation Level")]
        public SimulationLevel simLevel = SimulationLevel.Abstract;

        // Runtime - only set when Level 1+
        [System.NonSerialized] public GameObject spawnedObject;
        [System.NonSerialized] public ShipController shipController;

        public ShipRecord(string id, string name, ShipClass shipClass, FactionId factionId)
        {
            this.id = id;
            this.shipName = name;
            this.shipClass = shipClass;
            this.factionId = factionId;
            this.crewCapacity = GetCrewCapacityForClass(shipClass);
            this.crewCount = crewCapacity;
            this.fighterCapacity = GetFighterCapacityForClass(shipClass);
            this.fighterCount = fighterCapacity;
            this.combatRating = CalculateBaseCombatRating();
        }

        #region Class-Based Defaults

        public static int GetCrewCapacityForClass(ShipClass shipClass)
        {
            return shipClass switch
            {
                ShipClass.Corvette => 18,
                ShipClass.Frigate => 35,
                ShipClass.Destroyer => 70,
                ShipClass.Cruiser => 120,
                ShipClass.Battleship => 175,
                _ => 50
            };
        }

        public static int GetFighterCapacityForClass(ShipClass shipClass)
        {
            return shipClass switch
            {
                ShipClass.Corvette => 0,
                ShipClass.Frigate => 0,
                ShipClass.Destroyer => 4,
                ShipClass.Cruiser => 8,
                ShipClass.Battleship => 12,
                _ => 0
            };
        }

        public static float GetBaseSpeedForClass(ShipClass shipClass)
        {
            return shipClass switch
            {
                ShipClass.Corvette => 15f,
                ShipClass.Frigate => 12f,
                ShipClass.Destroyer => 10f,
                ShipClass.Cruiser => 8f,
                ShipClass.Battleship => 5f,
                _ => 10f
            };
        }

        #endregion

        #region Status Checks

        public bool IsOperational => status == ShipStatus.Operational;
        public bool CanFight => IsOperational && hullIntegrity > 20f;
        public bool NeedsResupply => fuelPercent < 30f || ammoPercent < 30f || suppliesPercent < 30f;
        public bool NeedsRepairs => hullIntegrity < 80f;

        public float CalculateBaseCombatRating()
        {
            // Base rating from ship class
            float classRating = shipClass switch
            {
                ShipClass.Corvette => 20f,
                ShipClass.Frigate => 35f,
                ShipClass.Destroyer => 50f,
                ShipClass.Cruiser => 75f,
                ShipClass.Battleship => 100f,
                _ => 30f
            };

            // Modify by hull integrity
            classRating *= (hullIntegrity / 100f);

            // Modify by crew (undermanned = less effective)
            float crewRatio = crewCapacity > 0 ? (float)crewCount / crewCapacity : 1f;
            classRating *= Mathf.Lerp(0.5f, 1f, crewRatio);

            // Add fighter contribution
            classRating += fighterCount * 3f;

            return classRating;
        }

        #endregion

        #region Simulation Level Management

        /// <summary>
        /// Promote to higher simulation level (more detail).
        /// </summary>
        public void Promote(SimulationLevel newLevel)
        {
            if (newLevel <= simLevel) return;

            simLevel = newLevel;
            Debug.Log($"[ShipRecord] {shipName} promoted to {newLevel}");

            // If promoted to Detailed, need to generate crew roster
            // This would be handled by SectorManager or a ship spawner
        }

        /// <summary>
        /// Demote to lower simulation level (less detail).
        /// </summary>
        public void Demote(SimulationLevel newLevel)
        {
            if (newLevel >= simLevel) return;

            simLevel = newLevel;
            Debug.Log($"[ShipRecord] {shipName} demoted to {newLevel}");

            // If demoting from Detailed, we might want to preserve some state
            // or just let it go back to abstract
        }

        #endregion

        #region Movement

        /// <summary>
        /// Move toward a destination (for abstract simulation).
        /// Returns true if arrived.
        /// </summary>
        public bool MoveToward(Vector2 destination, float deltaTime)
        {
            float speed = GetBaseSpeedForClass(shipClass);
            Vector2 direction = (destination - position).normalized;
            float distance = Vector2.Distance(position, destination);

            if (distance < speed * deltaTime)
            {
                position = destination;
                return true;
            }

            position += direction * speed * deltaTime;
            rotation = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            return false;
        }

        #endregion

        #region Resource Consumption

        /// <summary>
        /// Consume resources over time (abstract simulation).
        /// </summary>
        public void ConsumeResources(float gameHours)
        {
            // Fuel consumption based on movement (handled elsewhere)

            // Supplies consumed by crew over time
            float supplyConsumption = crewCount * 0.01f * gameHours; // 1% per 100 crew per hour
            suppliesPercent = Mathf.Max(0, suppliesPercent - supplyConsumption);

            // Update status if critical
            if (suppliesPercent <= 0 || fuelPercent <= 0)
            {
                status = ShipStatus.Stranded;
            }
        }

        /// <summary>
        /// Resupply at a station.
        /// </summary>
        public void Resupply()
        {
            fuelPercent = 100f;
            ammoPercent = 100f;
            suppliesPercent = 100f;

            if (status == ShipStatus.Stranded)
                status = ShipStatus.Operational;
        }

        #endregion
    }

    public enum ShipStatus
    {
        Operational,    // Ready for action
        Damaged,        // Needs repairs but functional
        Disabled,       // Cannot move/fight
        Stranded,       // Out of fuel/supplies
        Repairing,      // At station being repaired
        Destroyed       // Gone
    }

    public enum SimulationLevel
    {
        Strategic,      // Level 3: Just a record
        Abstract,       // Level 2: In sector, visible, abstract combat
        Detailed,       // Level 1: Full crew roster, can load arena
        Full            // Level 0: Player's ship, always detailed
    }
}
