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
        [Tooltip("Determines max health pool (50-150 range)")]
        [Range(1, 20)] public int Vitality = 10;
        [Range(1, 20)] public int Accuracy = 10;
        [Range(1, 20)] public int Reflex = 10;
        [Range(1, 20)] public int Bravery = 10;
        [Range(1, 20)] public int Agility = 10;
        [Range(1, 20)] public int Perception = 10;
        [Range(1, 20)] public int Stealth = 10;
        [Tooltip("Affects squad coordination speed (suppression assignment, etc.)")]
        [Range(1, 20)] public int Tactics = 10;

        [Header("Runtime Health (set at spawn)")]
        [HideInInspector] public float MaxHealth;
        [HideInInspector] public float CurrentHealth;

        [Header("Damage Mitigation (0-100%, from armor/gear)")]
        [Range(0f, 100f)] public float PhysicalMitigation = 0f;
        [Range(0f, 100f)] public float HeatMitigation = 0f;
        [Range(0f, 100f)] public float EnergyMitigation = 0f;
        [Range(0f, 100f)] public float IonMitigation = 0f;

        // Computed properties
        public float HealthPercent => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0f;
        public bool IsDead => CurrentHealth <= 0;

        /// <summary>
        /// Create a default character with average stats.
        /// </summary>
        public Character()
        {
            Name = "Soldier";
            Vitality = 10;
            Accuracy = 10;
            Reflex = 10;
            Bravery = 10;
            Agility = 10;
            Perception = 10;
            Stealth = 10;
            Tactics = 10;
        }

        /// <summary>
        /// Create a character with custom stats.
        /// </summary>
        public Character(string name, int vitality, int accuracy, int reflex, int bravery, int agility = 10, int perception = 10, int stealth = 10, int tactics = 10)
        {
            Name = name;
            Vitality = Mathf.Clamp(vitality, 1, 20);
            Accuracy = Mathf.Clamp(accuracy, 1, 20);
            Reflex = Mathf.Clamp(reflex, 1, 20);
            Bravery = Mathf.Clamp(bravery, 1, 20);
            Agility = Mathf.Clamp(agility, 1, 20);
            Perception = Mathf.Clamp(perception, 1, 20);
            Stealth = Mathf.Clamp(stealth, 1, 20);
            Tactics = Mathf.Clamp(tactics, 1, 20);
        }

        /// <summary>
        /// Initialize health based on Vitality stat. Call once at spawn.
        /// </summary>
        public void InitializeHealth()
        {
            float vitalityMultiplier = StatToMultiplier(Vitality);
            MaxHealth = 50f + (vitalityMultiplier * 100f); // 50-150 range
            CurrentHealth = MaxHealth;
        }

        /// <summary>
        /// Apply damage to this character. Returns final damage after mitigation.
        /// </summary>
        public float TakeDamage(float damage, DamageType damageType)
        {
            if (IsDead) return 0f;

            float mitigation = GetMitigation(damageType) / 100f;
            float finalDamage = damage * (1f - mitigation);

            CurrentHealth -= finalDamage;
            CurrentHealth = Mathf.Max(0, CurrentHealth);

            return finalDamage;
        }

        /// <summary>
        /// Apply damage directly (no mitigation).
        /// </summary>
        public float TakeDamage(float damage)
        {
            if (IsDead) return 0f;

            CurrentHealth -= damage;
            CurrentHealth = Mathf.Max(0, CurrentHealth);

            return damage;
        }

        /// <summary>
        /// Heal this character.
        /// </summary>
        public void Heal(float amount)
        {
            if (IsDead) return;

            CurrentHealth += amount;
            CurrentHealth = Mathf.Min(CurrentHealth, MaxHealth);
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

        /// <summary>
        /// Contested roll: stat1 + d6 vs stat2 + d6.
        /// Returns true if stat1 wins.
        /// </summary>
        public static bool ContestedRoll(int stat1, int stat2)
        {
            int roll1 = stat1 + Random.Range(1, 7); // 1-6
            int roll2 = stat2 + Random.Range(1, 7);
            return roll1 > roll2; // Tie goes to defender (stat2)
        }

        /// <summary>
        /// Contested roll with modifier bonus/penalty applied to stat1.
        /// </summary>
        public static bool ContestedRoll(int stat1, int stat2, int modifier)
        {
            int roll1 = stat1 + modifier + Random.Range(1, 7);
            int roll2 = stat2 + Random.Range(1, 7);
            return roll1 > roll2;
        }
    }
}
