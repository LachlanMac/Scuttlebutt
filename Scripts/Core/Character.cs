using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Pure data class representing a character's stats and identity.
    /// Assigned to a UnitController at spawn time.
    /// </summary>
    [System.Serializable]
    public class Character
    {
        [Header("Identity")]
        public string Name;

        [Header("Stats (1-20 scale, 10 = average)")]
        [Range(1, 20)] public int Health = 10;
        [Range(1, 20)] public int Accuracy = 10;
        [Range(1, 20)] public int Reflex = 10;
        [Range(1, 20)] public int Bravery = 10;

        [Header("Damage Mitigation (0-100%, from armor/gear)")]
        [Range(0f, 100f)] public float PhysicalMitigation = 0f;
        [Range(0f, 100f)] public float HeatMitigation = 0f;
        [Range(0f, 100f)] public float EnergyMitigation = 0f;
        [Range(0f, 100f)] public float IonMitigation = 0f;

        /// <summary>
        /// Create a default character with average stats.
        /// </summary>
        public Character()
        {
            Name = "Soldier";
            Health = 10;
            Accuracy = 10;
            Reflex = 10;
            Bravery = 10;
        }

        /// <summary>
        /// Create a character with custom stats.
        /// </summary>
        public Character(string name, int health, int accuracy, int reflex, int bravery)
        {
            Name = name;
            Health = Mathf.Clamp(health, 1, 20);
            Accuracy = Mathf.Clamp(accuracy, 1, 20);
            Reflex = Mathf.Clamp(reflex, 1, 20);
            Bravery = Mathf.Clamp(bravery, 1, 20);
        }

        /// <summary>
        /// Returns a stat as a 0-1 multiplier (10 = 0.5, 20 = 1.0, 1 = 0.05)
        /// </summary>
        public static float StatToMultiplier(int stat)
        {
            return stat / 20f;
        }

        /// <summary>
        /// Returns a stat as a modifier (-0.45 to +0.5 range, 10 = 0)
        /// </summary>
        public static float StatToModifier(int stat)
        {
            return (stat - 10) / 20f;
        }

        /// <summary>
        /// Get the mitigation percentage for a specific damage type.
        /// </summary>
        public float GetMitigation(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Physical => PhysicalMitigation,
                DamageType.Heat => HeatMitigation,
                DamageType.Energy => EnergyMitigation,
                DamageType.Ion => IonMitigation,
                _ => 0f
            };
        }
    }
}
