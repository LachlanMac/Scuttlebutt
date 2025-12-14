using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Ship
{
    /// <summary>
    /// Pure data class representing a ship's runtime state.
    /// Transferred between ParkedShip (arena) and SpaceVessel (space) representations.
    /// References ShipData for base stats via shipTypeId.
    /// </summary>
    [System.Serializable]
    public class ShipState
    {
        [Header("Identity")]
        public string ShipId;           // Unique instance ID
        public string ShipTypeId;       // References ShipData (e.g., "starfighter_a")
        public string ShipName;

        [Header("Pilot")]
        public Character Pilot;

        [Header("Hull & Shields (current values)")]
        public float CurrentHull = 100f;
        public float CurrentShields = 50f;

        [Header("Appearance")]
        public int ColorVariant = 0;
        public int MarkingsVariant = 0;

        [Header("Status")]
        public bool IsDisabled = false;
        public bool ShieldsOffline = false;

        // Cached ShipData reference (not serialized)
        [System.NonSerialized] private ShipData cachedShipData;

        /// <summary>
        /// Get the ShipData for this ship type. Cached after first lookup.
        /// </summary>
        public ShipData ShipData
        {
            get
            {
                if (cachedShipData == null && !string.IsNullOrEmpty(ShipTypeId))
                {
                    cachedShipData = DataLoader.GetShipReadOnly(ShipTypeId);
                }
                return cachedShipData;
            }
        }

        // Stats from ShipData (or fallback defaults)
        public float MaxHull => ShipData?.maxHull ?? 100f;
        public float MaxShields => ShipData?.maxShields ?? 50f;
        public float ShieldRegenRate => ShipData?.shieldRegenRate ?? 5f;

        // Computed properties
        public float HullPercent => MaxHull > 0 ? CurrentHull / MaxHull : 0f;
        public float ShieldsPercent => MaxShields > 0 ? CurrentShields / MaxShields : 0f;
        public bool IsDestroyed => CurrentHull <= 0;

        /// <summary>
        /// Create a default ship state.
        /// </summary>
        public ShipState()
        {
            ShipId = System.Guid.NewGuid().ToString().Substring(0, 8);
            ShipName = "Unknown Vessel";
        }

        /// <summary>
        /// Create a ship state from a ship type ID.
        /// </summary>
        public ShipState(string shipTypeId, Character pilot = null, string shipName = null)
        {
            ShipId = System.Guid.NewGuid().ToString().Substring(0, 8);
            ShipTypeId = shipTypeId;
            Pilot = pilot;

            // Load ship data and initialize stats
            var shipData = ShipData;
            if (shipData != null)
            {
                CurrentHull = shipData.maxHull;
                CurrentShields = shipData.maxShields;
                ShipName = shipName ?? shipData.displayName;
            }
            else
            {
                ShipName = shipName ?? "Unknown Vessel";
                Debug.LogWarning($"[ShipState] Unknown ship type: {shipTypeId}");
            }

            // Override name with pilot callsign if available
            if (pilot != null && string.IsNullOrEmpty(shipName))
            {
                ShipName = $"{pilot.Callsign ?? pilot.LastName ?? "Unknown"}'s Ship";
            }
        }

        /// <summary>
        /// Create a ship state with a pilot (legacy support, defaults to starfighter).
        /// </summary>
        public ShipState(Character pilot, string shipName = null)
            : this("starfighter_a", pilot, shipName)
        {
        }

        /// <summary>
        /// Apply damage to this ship. Shields absorb first, then hull.
        /// </summary>
        public void TakeDamage(float damage)
        {
            if (IsDestroyed) return;

            // Shields first
            if (CurrentShields > 0 && !ShieldsOffline)
            {
                float shieldDamage = Mathf.Min(CurrentShields, damage);
                CurrentShields -= shieldDamage;
                damage -= shieldDamage;
            }

            // Remaining to hull
            if (damage > 0)
            {
                CurrentHull -= damage;
                CurrentHull = Mathf.Max(0, CurrentHull);
            }
        }

        /// <summary>
        /// Repair hull damage.
        /// </summary>
        public void RepairHull(float amount)
        {
            CurrentHull = Mathf.Min(CurrentHull + amount, MaxHull);
        }

        /// <summary>
        /// Recharge shields.
        /// </summary>
        public void RechargeShields(float amount)
        {
            if (!ShieldsOffline)
            {
                CurrentShields = Mathf.Min(CurrentShields + amount, MaxShields);
            }
        }

        /// <summary>
        /// Fully restore hull and shields.
        /// </summary>
        public void FullRestore()
        {
            CurrentHull = MaxHull;
            CurrentShields = MaxShields;
            IsDisabled = false;
            ShieldsOffline = false;
        }

        /// <summary>
        /// Create a copy of this state.
        /// </summary>
        public ShipState Clone()
        {
            return new ShipState
            {
                ShipId = this.ShipId,
                ShipTypeId = this.ShipTypeId,
                ShipName = this.ShipName,
                Pilot = this.Pilot, // Reference, not deep copy
                CurrentHull = this.CurrentHull,
                CurrentShields = this.CurrentShields,
                ColorVariant = this.ColorVariant,
                MarkingsVariant = this.MarkingsVariant,
                IsDisabled = this.IsDisabled,
                ShieldsOffline = this.ShieldsOffline
            };
        }
    }
}
