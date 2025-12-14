using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Runtime controller for a faction. Manages ships and provides fleet operations.
    /// Hierarchy: Factions -> [FactionController] -> Ships -> [ShipMissionController]s
    /// </summary>
    public class FactionController : MonoBehaviour
    {
        [Header("Faction")]
        [SerializeField] private FactionId factionId;

        [Header("References (Auto-populated)")]
        [SerializeField] private Transform shipsContainer;

        // Cached data
        private Faction faction;
        private FactionConfig config;
        private List<ShipMissionController> ships = new List<ShipMissionController>();

        // Properties
        public FactionId FactionId => factionId;
        public Faction Faction => faction;
        public FactionConfig Config => config;
        public IReadOnlyList<ShipMissionController> Ships => ships;
        public int ShipCount => ships.Count;
        public Color FactionColor => faction?.factionColor ?? Color.white;
        public string DisplayName => faction?.displayName ?? factionId.ToString();

        private void Awake()
        {
            // Load faction data
            Factions.Initialize();
            faction = Factions.Get(factionId);
            config = FactionConfigLoader.GetConfig(factionId);

            // Find or create Ships container
            shipsContainer = transform.Find("Ships");
            if (shipsContainer == null)
            {
                var shipsGO = new GameObject("Ships");
                shipsGO.transform.SetParent(transform);
                shipsContainer = shipsGO.transform;
            }

            // Gather existing ships
            RefreshShipList();
        }

        /// <summary>
        /// Refresh the list of ships from the container.
        /// </summary>
        public void RefreshShipList()
        {
            ships.Clear();
            if (shipsContainer == null) return;

            foreach (Transform child in shipsContainer)
            {
                var shipController = child.GetComponent<ShipMissionController>();
                if (shipController != null)
                {
                    ships.Add(shipController);
                }
            }
        }

        /// <summary>
        /// Register a ship with this faction.
        /// </summary>
        public void RegisterShip(ShipMissionController ship)
        {
            if (ship == null) return;

            // Parent under Ships container
            ship.transform.SetParent(shipsContainer);

            if (!ships.Contains(ship))
            {
                ships.Add(ship);
            }
        }

        /// <summary>
        /// Remove a ship from this faction.
        /// </summary>
        public void UnregisterShip(ShipMissionController ship)
        {
            ships.Remove(ship);
        }

        /// <summary>
        /// Get all ships in a specific state.
        /// </summary>
        public IEnumerable<ShipMissionController> GetShipsInState(MissionState state)
        {
            foreach (var ship in ships)
            {
                if (ship.State == state)
                    yield return ship;
            }
        }

        /// <summary>
        /// Get all ships in a specific sector.
        /// </summary>
        public IEnumerable<ShipMissionController> GetShipsInSector(Vector2Int sector)
        {
            foreach (var ship in ships)
            {
                if (!ship.IsJumping && ship.Sector == sector)
                    yield return ship;
            }
        }

        /// <summary>
        /// Get all ships currently jumping (in hyperspace).
        /// </summary>
        public IEnumerable<ShipMissionController> GetJumpingShips()
        {
            foreach (var ship in ships)
            {
                if (ship.IsJumping)
                    yield return ship;
            }
        }

        /// <summary>
        /// Get all docked ships.
        /// </summary>
        public IEnumerable<ShipMissionController> GetDockedShips()
        {
            return GetShipsInState(MissionState.Docked);
        }

        /// <summary>
        /// Check if this faction is hostile to another.
        /// </summary>
        public bool IsHostileTo(FactionId other)
        {
            return faction?.atWarWith.Contains(other) ?? false;
        }

        /// <summary>
        /// Check if this faction is hostile to another controller.
        /// </summary>
        public bool IsHostileTo(FactionController other)
        {
            return other != null && IsHostileTo(other.FactionId);
        }

        #region Debug

        [ContextMenu("Log Fleet Status")]
        private void LogFleetStatus()
        {
            RefreshShipList();

            int docked = 0, traveling = 0, jumping = 0, other = 0;
            foreach (var ship in ships)
            {
                switch (ship.State)
                {
                    case MissionState.Docked: docked++; break;
                    case MissionState.Traveling: traveling++; break;
                    case MissionState.Jumping: jumping++; break;
                    default: other++; break;
                }
            }

            Debug.Log($"[{DisplayName}] Fleet: {ships.Count} ships - Docked:{docked} Traveling:{traveling} Jumping:{jumping} Other:{other}");
        }

        #endregion
    }
}
