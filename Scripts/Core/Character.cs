using UnityEngine;
using Starbelter.Combat;

namespace Starbelter.Core
{
    /// <summary>
    /// Marine Corps Enlisted Ranks (E-1 to E-9)
    /// </summary>
    public enum MarineEnlistedRank
    {
        Private,                // Pvt, E-1
        PrivateFirstClass,      // PFC, E-2
        LanceCorporal,          // LCpl, E-3
        Corporal,               // Cpl, E-4
        Sergeant,               // Sgt, E-5
        StaffSergeant,          // SSgt, E-6
        GunnerySergeant,        // GySgt, E-7
        MasterSergeant,         // MSgt, E-8
        FirstSergeant,          // 1stSgt, E-8
        MasterGunnerySergeant,  // MGySgt, E-9
        SergeantMajor           // SgtMaj, E-9
    }

    /// <summary>
    /// Marine Corps Officer Ranks (O-1 to O-10)
    /// </summary>
    public enum MarineOfficerRank
    {
        SecondLieutenant,       // 2ndLt, O-1
        FirstLieutenant,        // 1stLt, O-2
        Captain,                // Capt, O-3
        Major,                  // Maj, O-4
        LieutenantColonel,      // LtCol, O-5
        Colonel,                // Col, O-6
        BrigadierGeneral,       // BGen, O-7
        MajorGeneral,           // MajGen, O-8
        LieutenantGeneral,      // LtGen, O-9
        General                 // Gen, O-10
    }

    /// <summary>
    /// Navy Officer Ranks (O-1 to O-10)
    /// </summary>
    public enum NavyOfficerRank
    {
        Ensign,                 // ENS, O-1
        LieutenantJuniorGrade,  // LTJG, O-2
        Lieutenant,             // LT, O-3
        LieutenantCommander,    // LCDR, O-4
        Commander,              // CDR, O-5
        Captain,                // CAPT, O-6
        RearAdmiralLowerHalf,   // RDML, O-7
        RearAdmiralUpperHalf,   // RADM, O-8
        ViceAdmiral,            // VADM, O-9
        Admiral                 // ADM, O-10
    }

    /// <summary>
    /// Marine specializations
    /// </summary>
    public enum Specialization
    {
        Rifleman,       // Standard infantry
        Shocktrooper,   // Aggressive close-quarters
        Marksman        // Long-range precision
    }

    /// <summary>
    /// Pure data class representing a character's stats and identity.
    /// Assigned to a UnitController at spawn time.
    /// </summary>
    [System.Serializable]
    public class Character
    {
        [Header("Identity")]
        public string FirstName;
        public string LastName;
        public string Callsign;

        [Header("Service Record")]
        public bool IsOfficer;
        public MarineEnlistedRank EnlistedRank;
        public MarineOfficerRank OfficerRank;
        public int YearsOfService;
        public Specialization Specialization;

        [Header("Equipment")]
        public string MainWeaponId;
        [System.NonSerialized] public ProjectileWeapon MainWeapon;

        /// <summary>
        /// Full name for display.
        /// </summary>
        public string FullName => $"{FirstName} {LastName}";

        /// <summary>
        /// Rank and name for formal display (e.g., "Sgt. John Smith")
        /// </summary>
        public string RankAndName => $"{RankAbbreviation} {LastName}";

        /// <summary>
        /// Get the abbreviated rank string.
        /// </summary>
        public string RankAbbreviation
        {
            get
            {
                if (IsOfficer)
                {
                    return OfficerRank switch
                    {
                        MarineOfficerRank.SecondLieutenant => "2ndLt",
                        MarineOfficerRank.FirstLieutenant => "1stLt",
                        MarineOfficerRank.Captain => "Capt",
                        MarineOfficerRank.Major => "Maj",
                        MarineOfficerRank.LieutenantColonel => "LtCol",
                        MarineOfficerRank.Colonel => "Col",
                        MarineOfficerRank.BrigadierGeneral => "BGen",
                        MarineOfficerRank.MajorGeneral => "MajGen",
                        MarineOfficerRank.LieutenantGeneral => "LtGen",
                        MarineOfficerRank.General => "Gen",
                        _ => "Off"
                    };
                }
                else
                {
                    return EnlistedRank switch
                    {
                        MarineEnlistedRank.Private => "Pvt",
                        MarineEnlistedRank.PrivateFirstClass => "PFC",
                        MarineEnlistedRank.LanceCorporal => "LCpl",
                        MarineEnlistedRank.Corporal => "Cpl",
                        MarineEnlistedRank.Sergeant => "Sgt",
                        MarineEnlistedRank.StaffSergeant => "SSgt",
                        MarineEnlistedRank.GunnerySergeant => "GySgt",
                        MarineEnlistedRank.MasterSergeant => "MSgt",
                        MarineEnlistedRank.FirstSergeant => "1stSgt",
                        MarineEnlistedRank.MasterGunnerySergeant => "MGySgt",
                        MarineEnlistedRank.SergeantMajor => "SgtMaj",
                        _ => "Enl"
                    };
                }
            }
        }

        /// <summary>
        /// Get pay grade (E-1 through E-9, O-1 through O-10).
        /// </summary>
        public int PayGrade
        {
            get
            {
                if (IsOfficer)
                {
                    return (int)OfficerRank + 1; // O-1 to O-10
                }
                else
                {
                    // E-1 to E-9 (First Sergeant and Master Sergeant are both E-8, etc.)
                    return EnlistedRank switch
                    {
                        MarineEnlistedRank.Private => 1,
                        MarineEnlistedRank.PrivateFirstClass => 2,
                        MarineEnlistedRank.LanceCorporal => 3,
                        MarineEnlistedRank.Corporal => 4,
                        MarineEnlistedRank.Sergeant => 5,
                        MarineEnlistedRank.StaffSergeant => 6,
                        MarineEnlistedRank.GunnerySergeant => 7,
                        MarineEnlistedRank.MasterSergeant => 8,
                        MarineEnlistedRank.FirstSergeant => 8,
                        MarineEnlistedRank.MasterGunnerySergeant => 9,
                        MarineEnlistedRank.SergeantMajor => 9,
                        _ => 1
                    };
                }
            }
        }

        /// <summary>
        /// Legacy Name property for backwards compatibility.
        /// </summary>
        public string Name
        {
            get => FullName;
            set
            {
                var parts = value?.Split(' ') ?? new[] { "Unknown" };
                FirstName = parts[0];
                LastName = parts.Length > 1 ? parts[1] : "";
            }
        }

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
            FirstName = "John";
            LastName = "Doe";
            Callsign = "";
            IsOfficer = false;
            EnlistedRank = MarineEnlistedRank.Private;
            YearsOfService = 0;
            Specialization = Specialization.Rifleman;
            MainWeaponId = "assault_rifle";
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
        /// Create a character with custom stats (legacy constructor for backwards compatibility).
        /// </summary>
        public Character(string name, int vitality, int accuracy, int reflex, int bravery, int agility = 10, int perception = 10, int stealth = 10, int tactics = 10)
        {
            Name = name; // Uses the legacy setter to split into first/last
            IsOfficer = false;
            EnlistedRank = MarineEnlistedRank.Private;
            YearsOfService = 0;
            Specialization = Specialization.Rifleman;
            MainWeaponId = "assault_rifle";
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
        /// Load the weapon from DataLoader based on MainWeaponId.
        /// Call after loading from JSON.
        /// </summary>
        public void LoadWeapon()
        {
            if (!string.IsNullOrEmpty(MainWeaponId))
            {
                MainWeapon = DataLoader.GetWeapon(MainWeaponId);
            }
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
