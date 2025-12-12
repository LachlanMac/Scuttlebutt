using UnityEngine;
using Starbelter.Combat;

namespace Starbelter.Core
{
    /// <summary>
    /// Service branch - determines rank naming conventions.
    /// </summary>
    public enum ServiceBranch
    {
        Marine,     // Uses Marine Corps ranks
        Navy,       // Uses Navy ranks
        Civilian    // No military rank
    }

    /// <summary>
    /// Marine specializations (combat roles)
    /// </summary>
    public enum Specialization
    {
        Rifleman,       // Standard infantry
        Shocktrooper,   // Aggressive close-quarters
        Marksman        // Long-range precision
    }

    /// <summary>
    /// Character gender for appearance selection.
    /// </summary>
    public enum Gender
    {
        Male,
        Female
    }

    /// <summary>
    /// Broad profession categories that determine training and skill penalties.
    /// Characters acting outside their profession take penalties.
    /// </summary>
    public enum ProfessionCategory
    {
        Combat,         // Marines, Security - firefights, weapons handling
        Pilot,          // Fighter/shuttle pilots - flying spacecraft
        Engineering,    // Engineers, reactor techs - power systems, repairs
        Medical,        // Doctor, corpsmen - healing, triage, surgery
        Operations,     // Helm, sensors, comms, weapons console - ship ops
        Command,        // Officers in leadership roles - coordination, decisions
        Administration, // Supply, logistics, yeomen - paperwork, inventory
        Maintenance     // Machinists, general repairs - fabrication, fixing
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
        public Gender Gender;
        public int SkinTone;
        public int HairStyle;
        public int HairColor;

        [Header("Service Record")]
        public ServiceBranch Branch;
        public bool IsOfficer;
        [Tooltip("E-1 to E-9 for enlisted, O-1 to O-10 for officers")]
        [Range(1, 10)] public int Rank = 1;
        public int YearsOfService;
        public Specialization Specialization;
        public ProfessionCategory Profession;

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
        /// Get the abbreviated rank string based on Branch, IsOfficer, and Rank.
        /// </summary>
        public string RankAbbreviation => GetRankAbbreviation(Branch, IsOfficer, Rank);

        /// <summary>
        /// Get the full rank name based on Branch, IsOfficer, and Rank.
        /// </summary>
        public string RankName => GetRankName(Branch, IsOfficer, Rank);

        /// <summary>
        /// Get pay grade string (e.g., "E-5" or "O-3").
        /// </summary>
        public string PayGradeString => IsOfficer ? $"O-{Rank}" : $"E-{Rank}";

        /// <summary>
        /// Get pay grade as number (E-1 through E-9, O-1 through O-10).
        /// </summary>
        public int PayGrade => Rank;

        /// <summary>
        /// Static helper to get rank abbreviation.
        /// </summary>
        public static string GetRankAbbreviation(ServiceBranch branch, bool isOfficer, int rank)
        {
            if (branch == ServiceBranch.Civilian)
                return "Civ";

            if (isOfficer)
            {
                return (branch, rank) switch
                {
                    // Marine Officers
                    (ServiceBranch.Marine, 1) => "2ndLt",
                    (ServiceBranch.Marine, 2) => "1stLt",
                    (ServiceBranch.Marine, 3) => "Capt",
                    (ServiceBranch.Marine, 4) => "Maj",
                    (ServiceBranch.Marine, 5) => "LtCol",
                    (ServiceBranch.Marine, 6) => "Col",
                    (ServiceBranch.Marine, 7) => "BGen",
                    (ServiceBranch.Marine, 8) => "MajGen",
                    (ServiceBranch.Marine, 9) => "LtGen",
                    (ServiceBranch.Marine, 10) => "Gen",
                    // Navy Officers
                    (ServiceBranch.Navy, 1) => "ENS",
                    (ServiceBranch.Navy, 2) => "LTJG",
                    (ServiceBranch.Navy, 3) => "LT",
                    (ServiceBranch.Navy, 4) => "LCDR",
                    (ServiceBranch.Navy, 5) => "CDR",
                    (ServiceBranch.Navy, 6) => "CAPT",
                    (ServiceBranch.Navy, 7) => "RDML",
                    (ServiceBranch.Navy, 8) => "RADM",
                    (ServiceBranch.Navy, 9) => "VADM",
                    (ServiceBranch.Navy, 10) => "ADM",
                    _ => "Off"
                };
            }
            else
            {
                return (branch, rank) switch
                {
                    // Marine Enlisted
                    (ServiceBranch.Marine, 1) => "Pvt",
                    (ServiceBranch.Marine, 2) => "PFC",
                    (ServiceBranch.Marine, 3) => "LCpl",
                    (ServiceBranch.Marine, 4) => "Cpl",
                    (ServiceBranch.Marine, 5) => "Sgt",
                    (ServiceBranch.Marine, 6) => "SSgt",
                    (ServiceBranch.Marine, 7) => "GySgt",
                    (ServiceBranch.Marine, 8) => "MSgt",
                    (ServiceBranch.Marine, 9) => "MGySgt",
                    // Navy Enlisted
                    (ServiceBranch.Navy, 1) => "CR",
                    (ServiceBranch.Navy, 2) => "CA",
                    (ServiceBranch.Navy, 3) => "CN",
                    (ServiceBranch.Navy, 4) => "PO3",
                    (ServiceBranch.Navy, 5) => "PO2",
                    (ServiceBranch.Navy, 6) => "PO1",
                    (ServiceBranch.Navy, 7) => "CPO",
                    (ServiceBranch.Navy, 8) => "SCPO",
                    (ServiceBranch.Navy, 9) => "MCPO",
                    _ => "Enl"
                };
            }
        }

        /// <summary>
        /// Static helper to get full rank name.
        /// </summary>
        public static string GetRankName(ServiceBranch branch, bool isOfficer, int rank)
        {
            if (branch == ServiceBranch.Civilian)
                return "Civilian";

            if (isOfficer)
            {
                return (branch, rank) switch
                {
                    // Marine Officers
                    (ServiceBranch.Marine, 1) => "Second Lieutenant",
                    (ServiceBranch.Marine, 2) => "First Lieutenant",
                    (ServiceBranch.Marine, 3) => "Captain",
                    (ServiceBranch.Marine, 4) => "Major",
                    (ServiceBranch.Marine, 5) => "Lieutenant Colonel",
                    (ServiceBranch.Marine, 6) => "Colonel",
                    (ServiceBranch.Marine, 7) => "Brigadier General",
                    (ServiceBranch.Marine, 8) => "Major General",
                    (ServiceBranch.Marine, 9) => "Lieutenant General",
                    (ServiceBranch.Marine, 10) => "General",
                    // Navy Officers
                    (ServiceBranch.Navy, 1) => "Ensign",
                    (ServiceBranch.Navy, 2) => "Lieutenant Junior Grade",
                    (ServiceBranch.Navy, 3) => "Lieutenant",
                    (ServiceBranch.Navy, 4) => "Lieutenant Commander",
                    (ServiceBranch.Navy, 5) => "Commander",
                    (ServiceBranch.Navy, 6) => "Captain",
                    (ServiceBranch.Navy, 7) => "Rear Admiral Lower Half",
                    (ServiceBranch.Navy, 8) => "Rear Admiral Upper Half",
                    (ServiceBranch.Navy, 9) => "Vice Admiral",
                    (ServiceBranch.Navy, 10) => "Admiral",
                    _ => "Officer"
                };
            }
            else
            {
                return (branch, rank) switch
                {
                    // Marine Enlisted
                    (ServiceBranch.Marine, 1) => "Private",
                    (ServiceBranch.Marine, 2) => "Private First Class",
                    (ServiceBranch.Marine, 3) => "Lance Corporal",
                    (ServiceBranch.Marine, 4) => "Corporal",
                    (ServiceBranch.Marine, 5) => "Sergeant",
                    (ServiceBranch.Marine, 6) => "Staff Sergeant",
                    (ServiceBranch.Marine, 7) => "Gunnery Sergeant",
                    (ServiceBranch.Marine, 8) => "Master Sergeant",
                    (ServiceBranch.Marine, 9) => "Master Gunnery Sergeant",
                    // Navy Enlisted
                    (ServiceBranch.Navy, 1) => "Crewman Recruit",
                    (ServiceBranch.Navy, 2) => "Crewman Apprentice",
                    (ServiceBranch.Navy, 3) => "Crewman",
                    (ServiceBranch.Navy, 4) => "Petty Officer Third Class",
                    (ServiceBranch.Navy, 5) => "Petty Officer Second Class",
                    (ServiceBranch.Navy, 6) => "Petty Officer First Class",
                    (ServiceBranch.Navy, 7) => "Chief Petty Officer",
                    (ServiceBranch.Navy, 8) => "Senior Chief Petty Officer",
                    (ServiceBranch.Navy, 9) => "Master Chief Petty Officer",
                    _ => "Enlisted"
                };
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
        [Range(1, 20)] public int Fitness = 10;
        [Range(1, 20)] public int Accuracy = 10;
        [Tooltip("Reaction time, dodge, piloting")]
        [Range(1, 20)] public int Reflexes = 10;
        [Range(1, 20)] public int Bravery = 10;
        [Range(1, 20)] public int Perception = 10;
        [Range(1, 20)] public int Stealth = 10;
        [Tooltip("Affects threat perception accuracy and tactical decision-making")]
        [Range(1, 20)] public int Tactics = 10;
        [Tooltip("Affects squad morale bonus when leading")]
        [Range(1, 20)] public int Leadership = 10;
        [Tooltip("Engineering, repairs, systems operation")]
        [Range(1, 20)] public int Technical = 10;
        [Tooltip("Calm under pressure, focus, steady hands")]
        [Range(1, 20)] public int Composure = 10;
        [Tooltip("Following orders, self-control, routine")]
        [Range(1, 20)] public int Discipline = 10;
        [Tooltip("Problem-solving, analysis, diagnostics")]
        [Range(1, 20)] public int Logic = 10;
        [Tooltip("Interpersonal skills, persuasion, negotiation")]
        [Range(1, 20)] public int Communication = 10;

        [Header("Runtime Health (set at spawn)")]
        [HideInInspector] public float MaxHealth;
        [HideInInspector] public float CurrentHealth;

        [Header("Runtime Morale")]
        [HideInInspector] public float MaxMorale = 100f;
        [HideInInspector] public float CurrentMorale = 100f;
        [HideInInspector] public bool HasAppliedLowHealthMoralePenalty = false;

        [Header("Damage Mitigation (0-100%, from armor/gear)")]
        [Range(0f, 100f)] public float PhysicalMitigation = 0f;
        [Range(0f, 100f)] public float HeatMitigation = 0f;
        [Range(0f, 100f)] public float EnergyMitigation = 0f;
        [Range(0f, 100f)] public float IonMitigation = 0f;

        // Computed properties
        public float HealthPercent => MaxHealth > 0 ? CurrentHealth / MaxHealth : 0f;
        public bool IsDead => CurrentHealth <= 0;
        public float MoralePercent => MaxMorale > 0 ? CurrentMorale / MaxMorale : 0f;

        /// <summary>
        /// Create a default character with average stats.
        /// </summary>
        public Character()
        {
            FirstName = "John";
            LastName = "Doe";
            Callsign = "";
            Branch = ServiceBranch.Marine;
            IsOfficer = false;
            Rank = 1;
            YearsOfService = 0;
            Specialization = Specialization.Rifleman;
            Profession = ProfessionCategory.Combat;
            MainWeaponId = "assault_rifle";
            Fitness = 10;
            Accuracy = 10;
            Reflexes = 10;
            Bravery = 10;
            Perception = 10;
            Stealth = 10;
            Tactics = 10;
            Leadership = 10;
            Technical = 10;
            Composure = 10;
            Discipline = 10;
            Logic = 10;
            Communication = 10;
        }

        /// <summary>
        /// Create a character with custom stats (legacy constructor for backwards compatibility).
        /// </summary>
        public Character(string name, int fitness, int accuracy, int reflexes, int bravery, int perception = 10, int stealth = 10, int tactics = 10)
        {
            Name = name; // Uses the legacy setter to split into first/last
            Branch = ServiceBranch.Marine;
            IsOfficer = false;
            Rank = 1;
            YearsOfService = 0;
            Specialization = Specialization.Rifleman;
            Profession = ProfessionCategory.Combat;
            MainWeaponId = "assault_rifle";
            Fitness = Mathf.Clamp(fitness, 1, 20);
            Accuracy = Mathf.Clamp(accuracy, 1, 20);
            Reflexes = Mathf.Clamp(reflexes, 1, 20);
            Bravery = Mathf.Clamp(bravery, 1, 20);
            Perception = Mathf.Clamp(perception, 1, 20);
            Stealth = Mathf.Clamp(stealth, 1, 20);
            Tactics = Mathf.Clamp(tactics, 1, 20);
            Leadership = 10;
            Technical = 10;
            Composure = 10;
            Discipline = 10;
            Logic = 10;
            Communication = 10;
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

                // Ensure weapon starts with full magazine
                if (MainWeapon != null)
                {
                    MainWeapon.Reload();
                }
            }
        }

        /// <summary>
        /// Initialize health based on Fitness stat. Call once at spawn.
        /// </summary>
        public void InitializeHealth()
        {
            float fitnessMultiplier = StatToMultiplier(Fitness);
            MaxHealth = 50f + (fitnessMultiplier * 100f); // 50-150 range
            CurrentHealth = MaxHealth;
            InitializeMorale();
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
        /// Apply morale damage (from ally death, taking damage, etc.)
        /// </summary>
        public void TakeMoraleDamage(float amount)
        {
            CurrentMorale -= amount;
            CurrentMorale = Mathf.Clamp(CurrentMorale, 0f, MaxMorale);
        }

        /// <summary>
        /// Initialize morale to max (call at spawn).
        /// </summary>
        public void InitializeMorale()
        {
            CurrentMorale = MaxMorale;
            HasAppliedLowHealthMoralePenalty = false;
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

        /// <summary>
        /// Get the skill penalty multiplier when performing a task outside your profession.
        /// Returns 1.0 for your own profession, reduced values for others.
        /// </summary>
        public float GetProfessionMultiplier(ProfessionCategory taskCategory)
        {
            if (Profession == taskCategory)
                return 1.0f;

            // Adjacent/related professions get smaller penalties
            return (Profession, taskCategory) switch
            {
                // Combat-adjacent
                (ProfessionCategory.Combat, ProfessionCategory.Operations) => 0.7f,
                (ProfessionCategory.Operations, ProfessionCategory.Combat) => 0.6f,
                (ProfessionCategory.Command, ProfessionCategory.Combat) => 0.6f,

                // Technical-adjacent
                (ProfessionCategory.Engineering, ProfessionCategory.Maintenance) => 0.9f,
                (ProfessionCategory.Maintenance, ProfessionCategory.Engineering) => 0.8f,
                (ProfessionCategory.Engineering, ProfessionCategory.Operations) => 0.7f,
                (ProfessionCategory.Operations, ProfessionCategory.Engineering) => 0.5f,

                // Command-adjacent
                (ProfessionCategory.Command, ProfessionCategory.Operations) => 0.8f,
                (ProfessionCategory.Command, ProfessionCategory.Administration) => 0.8f,
                (ProfessionCategory.Operations, ProfessionCategory.Command) => 0.5f,

                // Medical is specialized
                (ProfessionCategory.Medical, _) => 0.4f, // Medics are bad at other things
                (_, ProfessionCategory.Medical) => 0.3f, // Others are bad at medical

                // Pilot is specialized
                (ProfessionCategory.Pilot, ProfessionCategory.Operations) => 0.7f,
                (ProfessionCategory.Operations, ProfessionCategory.Pilot) => 0.5f,
                (_, ProfessionCategory.Pilot) => 0.3f, // Non-pilots are terrible at flying

                // Default penalty for unrelated professions
                _ => 0.5f
            };
        }

        /// <summary>
        /// Apply profession penalty to a stat value for a specific task type.
        /// </summary>
        public int GetEffectiveStat(int baseStat, ProfessionCategory taskCategory)
        {
            float multiplier = GetProfessionMultiplier(taskCategory);
            return Mathf.RoundToInt(baseStat * multiplier);
        }
    }
}
