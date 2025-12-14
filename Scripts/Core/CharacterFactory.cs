using UnityEngine;
using System;
using System.Collections.Generic;

namespace Starbelter.Core
{
    /// <summary>
    /// Factory for procedurally generating characters.
    /// Generates stats based on profession, experience, and natural talent.
    /// </summary>
    public static class CharacterFactory
    {
        // Name pools (loaded from Names.json via DataLoader)
        private static string[] mascNames;
        private static string[] femNames;
        private static string[] surnames;
        private static string[] pilotCallsigns;
        private static string[] soldierCallsigns;
        private static bool namesLoaded = false;

        // Generation constants
        private const int BASE_STAT = 8;
        private const int MIN_STAT = 1;
        private const int MAX_STAT = 20;
        private const float PRODIGY_CHANCE = 0.05f;
        private const float EXPERIENCE_BONUS_PER_YEAR = 0.25f;
        private const float MAX_EXPERIENCE_BONUS = 5f;

        #region Public API

        /// <summary>
        /// Generate a character with specified parameters.
        /// </summary>
        /// <param name="profession">Primary profession/job category</param>
        /// <param name="specialization">Combat specialization (for weapon assignment)</param>
        /// <param name="branch">Service branch (Marine/Navy/Civilian)</param>
        /// <param name="isOfficer">Officer or enlisted</param>
        /// <param name="rank">Pay grade (E-1 to E-9, O-1 to O-10)</param>
        /// <param name="seed">Optional seed for deterministic generation</param>
        /// <param name="forcedGender">If set, overrides random gender selection</param>
        public static Character Generate(
            ProfessionCategory profession,
            Specialization specialization,
            ServiceBranch branch,
            bool isOfficer,
            int rank,
            int? seed = null,
            Gender? forcedGender = null)
        {
            EnsureNamesLoaded();
            System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            // Calculate years of service based on rank
            int yearsOfService = CalculateYearsOfService(isOfficer, rank, rng);

            // Determine if this character is a prodigy
            bool isProdigy = rng.NextDouble() < PRODIGY_CHANCE;

            // Generate identity (65% male, 35% female) - unless forced
            bool isFemale = forcedGender.HasValue
                ? forcedGender.Value == Gender.Female
                : rng.NextDouble() < 0.35;
            string firstName = GetRandomName(isFemale ? femNames : mascNames, rng, isFemale ? "Jane" : "John");
            string lastName = GetRandomName(surnames, rng, "Doe");
            string callsign = GetCallsign(profession, rng);

            // Generate stats
            var stats = GenerateStats(profession, yearsOfService, isProdigy, isOfficer, rng);

            // Determine weapon
            string weaponId = GetWeaponForRole(specialization, isOfficer);

            int skinTone = rng.Next(0, 8);  // 8 skin tones
            int hairStyle = rng.Next(0, 2); // 2 hair styles per gender
            int hairColor = rng.Next(0, 8); // 8 hair colors

            var character = new Character
            {
                FirstName = firstName,
                LastName = lastName,
                Callsign = callsign,
                Gender = isFemale ? Gender.Female : Gender.Male,
                SkinTone = skinTone,
                HairStyle = hairStyle,
                HairColor = hairColor,
                Branch = branch,
                IsOfficer = isOfficer,
                Rank = rank,
                YearsOfService = yearsOfService,
                Specialization = specialization,
                Profession = profession,
                MainWeaponId = weaponId,
                Fitness = stats.Fitness,
                Accuracy = stats.Accuracy,
                Reflexes = stats.Reflexes,
                Bravery = stats.Bravery,
                Perception = stats.Perception,
                Stealth = stats.Stealth,
                Tactics = stats.Tactics,
                Leadership = stats.Leadership,
                Technical = stats.Technical,
                Composure = stats.Composure,
                Discipline = stats.Discipline,
                Logic = stats.Logic,
                Communication = stats.Communication
            };

            character.LoadWeapon();
            return character;
        }

        /// <summary>
        /// Generate a combat-focused character (Marine).
        /// </summary>
        public static Character GenerateSoldier(
            Specialization specialization,
            bool isOfficer,
            int rank,
            int? seed = null)
        {
            return Generate(
                ProfessionCategory.Combat,
                specialization,
                ServiceBranch.Marine,
                isOfficer,
                rank,
                seed);
        }

        /// <summary>
        /// Generate a ship crew member (Navy).
        /// </summary>
        public static Character GenerateCrewMember(
            ProfessionCategory profession,
            bool isOfficer,
            int rank,
            int? seed = null)
        {
            return Generate(
                profession,
                Specialization.Rifleman, // Default, not relevant for most crew
                ServiceBranch.Navy,
                isOfficer,
                rank,
                seed);
        }

        /// <summary>
        /// Generate a random enlisted soldier of appropriate rank.
        /// </summary>
        public static Character GenerateRandomEnlisted(ServiceBranch branch, int? seed = null)
        {
            System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            int rank = rng.Next(1, 8); // E-1 to E-7 (more common ranks)
            var specialization = (Specialization)rng.Next(0, 3);

            return Generate(
                ProfessionCategory.Combat,
                specialization,
                branch,
                false,
                rank,
                seed);
        }

        /// <summary>
        /// Generate a random officer.
        /// </summary>
        public static Character GenerateRandomOfficer(ServiceBranch branch, int? seed = null)
        {
            System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            int rank = rng.Next(1, 5); // O-1 to O-4 (field grade)

            return Generate(
                ProfessionCategory.Command,
                Specialization.Rifleman,
                branch,
                true,
                rank,
                seed);
        }

        #endregion

        #region Crew Member Generation (Job + Role System)

        /// <summary>
        /// Generate a CrewMember for a Position (from Positions.json).
        /// Uses role adjacency for realistic cross-training based on experience.
        /// </summary>
        /// <param name="forcedGender">If set, overrides random gender selection</param>
        public static CrewMember GenerateForPosition(Position position, Shift shift, int? seed = null, Gender? forcedGender = null)
        {
            if (position == null)
            {
                Debug.LogError("[CharacterFactory] Position is null");
                return null;
            }

            System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            // Pick a random rank within the position's range
            int rank = rng.Next(position.MinRank, position.MaxRank + 1);

            // Get the job definition for profession lookup
            var jobDef = JobDefinitions.Get(position.Job);
            ProfessionCategory profession = jobDef?.Profession ?? ProfessionCategory.Combat;

            // Pick a primary role from the position's required roles
            Role primaryRole = Role.None;
            if (position.RequiredRoles != null && position.RequiredRoles.Length > 0 && position.RequiredRoles[0] != Role.None)
            {
                primaryRole = position.RequiredRoles[rng.Next(position.RequiredRoles.Length)];
            }

            // Calculate years of service based on rank
            int yearsOfService = CalculateYearsOfService(position.IsOfficer, rank, rng);

            // Determine if prodigy
            bool isProdigy = rng.NextDouble() < PRODIGY_CHANCE;

            // Generate roles with experience-based cross-training
            Role[] allRoles;
            if (primaryRole != Role.None)
            {
                allRoles = RoleAdjacency.GenerateRolesWithExperience(primaryRole, yearsOfService, isProdigy, rng);
            }
            else
            {
                allRoles = new[] { Role.None };
            }

            // Map primary role to specialization for weapon assignment
            Specialization specialization = RoleToSpecialization(primaryRole);

            // Generate the base character
            Character character = Generate(
                profession,
                specialization,
                position.Branch,
                position.IsOfficer,
                rank,
                seed,
                forcedGender
            );

            // Create crew member with all generated roles
            return new CrewMember(character, position.Job, shift, allRoles);
        }

        /// <summary>
        /// Generate a CrewMember for a Position by ID.
        /// </summary>
        public static CrewMember GenerateForPosition(string positionId, Shift shift, int? seed = null)
        {
            var position = PositionRegistry.Get(positionId);
            if (position == null)
            {
                Debug.LogError($"[CharacterFactory] Position not found: {positionId}");
                return null;
            }
            return GenerateForPosition(position, shift, seed);
        }

        /// <summary>
        /// Generate a CrewMember for a specific job and role.
        /// Uses role adjacency for realistic cross-training based on experience.
        /// </summary>
        public static CrewMember GenerateForJob(Job job, Role primaryRole, Shift shift, int? seed = null)
        {
            var definition = JobDefinitions.Get(job);
            if (definition == null)
            {
                Debug.LogError($"[CharacterFactory] No definition found for job: {job}");
                return null;
            }

            // Validate role is allowed for this job
            if (!definition.IsRoleAllowed(primaryRole))
            {
                Debug.LogWarning($"[CharacterFactory] Role {primaryRole} not allowed for job {job}, using first allowed role");
                primaryRole = definition.AllowedRoles[0];
            }

            System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            // Pick a random rank within the job's range
            int rank = rng.Next(definition.MinRank, definition.MaxRank + 1);

            // Calculate years of service
            int yearsOfService = CalculateYearsOfService(definition.RequiresOfficer, rank, rng);

            // Determine if prodigy
            bool isProdigy = rng.NextDouble() < PRODIGY_CHANCE;

            // Generate roles with experience-based cross-training
            Role[] allRoles = RoleAdjacency.GenerateRolesWithExperience(primaryRole, yearsOfService, isProdigy, rng);

            // Map primary role to specialization for weapon assignment
            Specialization specialization = RoleToSpecialization(primaryRole);

            // Generate the base character
            Character character = Generate(
                definition.Profession,
                specialization,
                definition.Branch,
                definition.RequiresOfficer,
                rank,
                seed
            );

            // Create crew member with all generated roles
            return new CrewMember(character, job, shift, allRoles);
        }

        /// <summary>
        /// Generate a CrewMember with explicitly specified roles (no auto-generation).
        /// </summary>
        public static CrewMember GenerateForJobWithRoles(Job job, Role[] roles, Shift shift, int? seed = null)
        {
            if (roles == null || roles.Length == 0)
            {
                return GenerateForJob(job, Role.None, shift, seed);
            }

            var definition = JobDefinitions.Get(job);
            if (definition == null)
            {
                Debug.LogError($"[CharacterFactory] No definition found for job: {job}");
                return null;
            }

            System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            // Pick a random rank within the job's range
            int rank = rng.Next(definition.MinRank, definition.MaxRank + 1);

            // Use first role for specialization
            Specialization specialization = RoleToSpecialization(roles[0]);

            // Generate the base character
            Character character = Generate(
                definition.Profession,
                specialization,
                definition.Branch,
                definition.RequiresOfficer,
                rank,
                seed
            );

            // Create crew member with specified roles (no auto-generation)
            return new CrewMember(character, job, shift, roles);
        }

        /// <summary>
        /// Generate a CrewMember with a specific rank (for key positions).
        /// Uses role adjacency for cross-training.
        /// </summary>
        public static CrewMember GenerateForJobAtRank(Job job, Role primaryRole, Shift shift, int rank, int? seed = null)
        {
            var definition = JobDefinitions.Get(job);
            if (definition == null)
            {
                Debug.LogError($"[CharacterFactory] No definition found for job: {job}");
                return null;
            }

            // Clamp rank to valid range
            rank = Mathf.Clamp(rank, definition.MinRank, definition.MaxRank);

            System.Random rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();

            // Calculate years of service
            int yearsOfService = CalculateYearsOfService(definition.RequiresOfficer, rank, rng);

            // Determine if prodigy
            bool isProdigy = rng.NextDouble() < PRODIGY_CHANCE;

            // Generate roles with experience-based cross-training
            Role[] allRoles = RoleAdjacency.GenerateRolesWithExperience(primaryRole, yearsOfService, isProdigy, rng);

            // Map role to specialization
            Specialization specialization = RoleToSpecialization(primaryRole);

            // Generate the base character
            Character character = Generate(
                definition.Profession,
                specialization,
                definition.Branch,
                definition.RequiresOfficer,
                rank,
                seed
            );

            // Create crew member with all generated roles
            return new CrewMember(character, job, shift, allRoles);
        }

        /// <summary>
        /// Map a Role to a combat Specialization for weapon assignment.
        /// </summary>
        private static Specialization RoleToSpecialization(Role role)
        {
            return role switch
            {
                // Marine combat roles map directly
                Role.Rifleman => Specialization.Rifleman,
                Role.Marksman => Specialization.Marksman,
                Role.Shocktrooper => Specialization.Shocktrooper,
                Role.Demolitions => Specialization.Shocktrooper, // Similar loadout

                // Non-combat roles default to Rifleman (sidearm)
                _ => Specialization.Rifleman
            };
        }

        /// <summary>
        /// Get an appropriate callsign based on job/role.
        /// </summary>
        private static string GetCallsignForRole(Role role, System.Random rng)
        {
            // Pilot roles get pilot callsigns
            if (role == Role.Fighter || role == Role.Bomber ||
                role == Role.Shuttle || role == Role.Capital)
            {
                return GetRandomName(pilotCallsigns, rng, "");
            }

            // Combat roles get soldier callsigns
            if (role == Role.Rifleman || role == Role.Marksman ||
                role == Role.Shocktrooper || role == Role.Demolitions ||
                role == Role.Combat)
            {
                return GetRandomName(soldierCallsigns, rng, "");
            }

            // Other roles might not have callsigns
            return "";
        }

        #endregion

        #region Stat Generation

        private struct StatBlock
        {
            public int Fitness, Accuracy, Reflexes, Bravery, Perception;
            public int Stealth, Tactics, Leadership, Technical, Composure;
            public int Discipline, Logic, Communication;
        }

        private static StatBlock GenerateStats(
            ProfessionCategory profession,
            int yearsOfService,
            bool isProdigy,
            bool isOfficer,
            System.Random rng)
        {
            var stats = new StatBlock();

            // Start with base stats
            stats.Fitness = BASE_STAT;
            stats.Accuracy = BASE_STAT;
            stats.Reflexes = BASE_STAT;
            stats.Bravery = BASE_STAT;
            stats.Perception = BASE_STAT;
            stats.Stealth = BASE_STAT;
            stats.Tactics = BASE_STAT;
            stats.Leadership = BASE_STAT;
            stats.Technical = BASE_STAT;
            stats.Composure = BASE_STAT;
            stats.Discipline = BASE_STAT;
            stats.Logic = BASE_STAT;
            stats.Communication = BASE_STAT;

            // Apply profession bonuses
            ApplyProfessionBonuses(ref stats, profession);

            // Apply experience bonuses (diminishing returns)
            float expBonus = Mathf.Min(yearsOfService * EXPERIENCE_BONUS_PER_YEAR, MAX_EXPERIENCE_BONUS);
            ApplyExperienceBonus(ref stats, expBonus, rng);

            // Apply prodigy bonus
            if (isProdigy)
            {
                ApplyProdigyBonus(ref stats, profession, rng);
            }

            // Apply officer bonus
            if (isOfficer)
            {
                stats.Leadership += 2;
                stats.Tactics += 1;
                stats.Communication += 1;
            }

            // Apply random variance
            ApplyVariance(ref stats, rng);

            // Clamp all stats
            ClampStats(ref stats);

            return stats;
        }

        private static void ApplyProfessionBonuses(ref StatBlock stats, ProfessionCategory profession)
        {
            switch (profession)
            {
                case ProfessionCategory.Combat:
                    stats.Fitness += 3;
                    stats.Accuracy += 3;
                    stats.Bravery += 2;
                    stats.Reflexes += 1;
                    break;

                case ProfessionCategory.Pilot:
                    stats.Reflexes += 3;
                    stats.Perception += 2;
                    stats.Composure += 2;
                    stats.Technical += 1;
                    break;

                case ProfessionCategory.Engineering:
                    stats.Technical += 3;
                    stats.Logic += 2;
                    stats.Discipline += 2;
                    stats.Perception += 1;
                    break;

                case ProfessionCategory.Medical:
                    stats.Technical += 3;
                    stats.Composure += 2;
                    stats.Logic += 2;
                    stats.Perception += 1;
                    break;

                case ProfessionCategory.Operations:
                    stats.Technical += 2;
                    stats.Perception += 2;
                    stats.Discipline += 2;
                    stats.Logic += 1;
                    stats.Composure += 1;
                    break;

                case ProfessionCategory.Command:
                    stats.Leadership += 3;
                    stats.Tactics += 2;
                    stats.Communication += 2;
                    stats.Composure += 1;
                    break;

                case ProfessionCategory.Administration:
                    stats.Logic += 2;
                    stats.Communication += 2;
                    stats.Discipline += 2;
                    stats.Technical += 1;
                    break;

                case ProfessionCategory.Maintenance:
                    stats.Technical += 3;
                    stats.Fitness += 2;
                    stats.Logic += 1;
                    stats.Discipline += 1;
                    break;
            }
        }

        private static void ApplyExperienceBonus(ref StatBlock stats, float bonus, System.Random rng)
        {
            // Experience improves most stats, but not evenly
            // Core profession stats improve more than others
            int baseBonus = Mathf.FloorToInt(bonus);

            // All stats get some improvement from experience
            stats.Discipline += baseBonus;
            stats.Composure += Mathf.FloorToInt(bonus * 0.8f);
            stats.Tactics += Mathf.FloorToInt(bonus * 0.6f);

            // Randomly distribute remaining points
            int extraPoints = Mathf.FloorToInt(bonus * 0.5f);
            for (int i = 0; i < extraPoints; i++)
            {
                int stat = rng.Next(0, 13);
                AddToStat(ref stats, stat, 1);
            }
        }

        private static void ApplyProdigyBonus(ref StatBlock stats, ProfessionCategory profession, System.Random rng)
        {
            // Prodigies get significant bonuses to profession-relevant stats
            int prodigyBonus = rng.Next(3, 6); // +3 to +5

            switch (profession)
            {
                case ProfessionCategory.Combat:
                    stats.Accuracy += prodigyBonus;
                    stats.Reflexes += prodigyBonus / 2;
                    break;
                case ProfessionCategory.Pilot:
                    stats.Reflexes += prodigyBonus;
                    stats.Perception += prodigyBonus / 2;
                    break;
                case ProfessionCategory.Engineering:
                case ProfessionCategory.Maintenance:
                    stats.Technical += prodigyBonus;
                    stats.Logic += prodigyBonus / 2;
                    break;
                case ProfessionCategory.Medical:
                    stats.Technical += prodigyBonus;
                    stats.Composure += prodigyBonus / 2;
                    break;
                case ProfessionCategory.Operations:
                    stats.Perception += prodigyBonus;
                    stats.Technical += prodigyBonus / 2;
                    break;
                case ProfessionCategory.Command:
                    stats.Leadership += prodigyBonus;
                    stats.Tactics += prodigyBonus / 2;
                    break;
                case ProfessionCategory.Administration:
                    stats.Logic += prodigyBonus;
                    stats.Communication += prodigyBonus / 2;
                    break;
            }
        }

        private static void ApplyVariance(ref StatBlock stats, System.Random rng)
        {
            // Apply -2 to +2 variance to each stat
            stats.Fitness += rng.Next(-2, 3);
            stats.Accuracy += rng.Next(-2, 3);
            stats.Reflexes += rng.Next(-2, 3);
            stats.Bravery += rng.Next(-2, 3);
            stats.Perception += rng.Next(-2, 3);
            stats.Stealth += rng.Next(-2, 3);
            stats.Tactics += rng.Next(-2, 3);
            stats.Leadership += rng.Next(-2, 3);
            stats.Technical += rng.Next(-2, 3);
            stats.Composure += rng.Next(-2, 3);
            stats.Discipline += rng.Next(-2, 3);
            stats.Logic += rng.Next(-2, 3);
            stats.Communication += rng.Next(-2, 3);
        }

        private static void AddToStat(ref StatBlock stats, int index, int amount)
        {
            switch (index)
            {
                case 0: stats.Fitness += amount; break;
                case 1: stats.Accuracy += amount; break;
                case 2: stats.Reflexes += amount; break;
                case 3: stats.Bravery += amount; break;
                case 4: stats.Perception += amount; break;
                case 5: stats.Stealth += amount; break;
                case 6: stats.Tactics += amount; break;
                case 7: stats.Leadership += amount; break;
                case 8: stats.Technical += amount; break;
                case 9: stats.Composure += amount; break;
                case 10: stats.Discipline += amount; break;
                case 11: stats.Logic += amount; break;
                case 12: stats.Communication += amount; break;
            }
        }

        private static void ClampStats(ref StatBlock stats)
        {
            stats.Fitness = Mathf.Clamp(stats.Fitness, MIN_STAT, MAX_STAT);
            stats.Accuracy = Mathf.Clamp(stats.Accuracy, MIN_STAT, MAX_STAT);
            stats.Reflexes = Mathf.Clamp(stats.Reflexes, MIN_STAT, MAX_STAT);
            stats.Bravery = Mathf.Clamp(stats.Bravery, MIN_STAT, MAX_STAT);
            stats.Perception = Mathf.Clamp(stats.Perception, MIN_STAT, MAX_STAT);
            stats.Stealth = Mathf.Clamp(stats.Stealth, MIN_STAT, MAX_STAT);
            stats.Tactics = Mathf.Clamp(stats.Tactics, MIN_STAT, MAX_STAT);
            stats.Leadership = Mathf.Clamp(stats.Leadership, MIN_STAT, MAX_STAT);
            stats.Technical = Mathf.Clamp(stats.Technical, MIN_STAT, MAX_STAT);
            stats.Composure = Mathf.Clamp(stats.Composure, MIN_STAT, MAX_STAT);
            stats.Discipline = Mathf.Clamp(stats.Discipline, MIN_STAT, MAX_STAT);
            stats.Logic = Mathf.Clamp(stats.Logic, MIN_STAT, MAX_STAT);
            stats.Communication = Mathf.Clamp(stats.Communication, MIN_STAT, MAX_STAT);
        }

        #endregion

        #region Years of Service

        private static int CalculateYearsOfService(bool isOfficer, int rank, System.Random rng)
        {
            if (isOfficer)
            {
                return rank switch
                {
                    1 => rng.Next(0, 3),    // O-1: 0-2 years
                    2 => rng.Next(2, 5),    // O-2: 2-4 years
                    3 => rng.Next(4, 8),    // O-3: 4-7 years
                    4 => rng.Next(7, 12),   // O-4: 7-11 years
                    5 => rng.Next(11, 17),  // O-5: 11-16 years
                    6 => rng.Next(16, 23),  // O-6: 16-22 years
                    7 => rng.Next(22, 28),  // O-7: 22-27 years
                    8 => rng.Next(26, 32),  // O-8: 26-31 years
                    9 => rng.Next(30, 36),  // O-9: 30-35 years
                    10 => rng.Next(32, 40), // O-10: 32+ years
                    _ => 0
                };
            }
            else
            {
                return rank switch
                {
                    1 => rng.Next(0, 2),    // E-1: 0-1 years
                    2 => rng.Next(1, 3),    // E-2: 1-2 years
                    3 => rng.Next(2, 4),    // E-3: 2-3 years
                    4 => rng.Next(3, 6),    // E-4: 3-5 years
                    5 => rng.Next(5, 8),    // E-5: 5-7 years
                    6 => rng.Next(7, 11),   // E-6: 7-10 years
                    7 => rng.Next(10, 15),  // E-7: 10-14 years
                    8 => rng.Next(14, 19),  // E-8: 14-18 years
                    9 => rng.Next(18, 26),  // E-9: 18-25 years
                    _ => 0
                };
            }
        }

        #endregion

        #region Name Generation

        private static void EnsureNamesLoaded()
        {
            if (namesLoaded) return;
            LoadNames();
        }

        private static void LoadNames()
        {
            var jsonAsset = Resources.Load<TextAsset>("Data/Names");
            if (jsonAsset == null)
            {
                Debug.LogWarning("[CharacterFactory] Names.json not found, using fallback names");
                SetFallbackNames();
                namesLoaded = true;
                return;
            }

            var data = JsonUtility.FromJson<NamesData>(jsonAsset.text);
            if (data == null)
            {
                Debug.LogWarning("[CharacterFactory] Failed to parse Names.json, using fallback names");
                SetFallbackNames();
                namesLoaded = true;
                return;
            }

            mascNames = data.mascNames ?? new string[0];
            femNames = data.femNames ?? new string[0];
            surnames = data.surnames ?? new string[0];
            pilotCallsigns = data.pilotCallsigns ?? new string[0];
            soldierCallsigns = data.soldierCallsigns ?? new string[0];

            // Warn if pools are empty
            if (mascNames.Length == 0) Debug.LogWarning("[CharacterFactory] mascNames pool is empty");
            if (femNames.Length == 0) Debug.LogWarning("[CharacterFactory] femNames pool is empty");
            if (surnames.Length == 0) Debug.LogWarning("[CharacterFactory] surnames pool is empty");

            namesLoaded = true;
            Debug.Log($"[CharacterFactory] Loaded name pools: {mascNames.Length} masc, {femNames.Length} fem, {surnames.Length} surnames, {pilotCallsigns.Length} pilot callsigns, {soldierCallsigns.Length} soldier callsigns");
        }

        private static void SetFallbackNames()
        {
            mascNames = new[] { "John", "James", "Michael", "William", "David" };
            femNames = new[] { "Jane", "Sarah", "Emily", "Maria", "Anna" };
            surnames = new[] { "Smith", "Johnson", "Williams", "Brown", "Jones" };
            pilotCallsigns = new[] { "Maverick", "Viper", "Ghost", "Phoenix", "Hawk" };
            soldierCallsigns = new[] { "Ironside", "Reaper", "Tank", "Shadow", "Wolf" };
        }

        private static string GetRandomName(string[] pool, System.Random rng, string fallback)
        {
            if (pool == null || pool.Length == 0)
                return fallback;
            return pool[rng.Next(pool.Length)];
        }

        private static string GetCallsign(ProfessionCategory profession, System.Random rng)
        {
            string[] pool = profession == ProfessionCategory.Pilot ? pilotCallsigns : soldierCallsigns;

            if (pool == null || pool.Length == 0)
                return "";

            return pool[rng.Next(pool.Length)];
        }

        #endregion

        #region Weapon Assignment

        private static string GetWeaponForRole(Specialization specialization, bool isOfficer)
        {
            // Officers typically carry sidearms
            if (isOfficer)
                return "pistol";

            return specialization switch
            {
                Specialization.Rifleman => "assault_rifle",
                Specialization.Marksman => "sniper_rifle",
                Specialization.Shocktrooper => "smg",
                _ => "assault_rifle"
            };
        }

        #endregion

        #region Data Structures

        [Serializable]
        private class NamesData
        {
            public string[] mascNames;
            public string[] femNames;
            public string[] surnames;
            public string[] pilotCallsigns;
            public string[] soldierCallsigns;
        }

        #endregion

        /// <summary>
        /// Force reload name pools (useful for hot-reloading in editor).
        /// </summary>
        public static void ReloadNames()
        {
            namesLoaded = false;
            EnsureNamesLoaded();
        }
    }
}
