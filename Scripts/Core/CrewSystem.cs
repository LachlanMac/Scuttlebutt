using UnityEngine;
using System;
using System.Collections.Generic;

namespace Starbelter.Core
{
    /// <summary>
    /// Shift assignment for crew members.
    /// </summary>
    public enum Shift
    {
        Main,   // 12 hours on duty
        Off     // 12 hours off duty
    }

    /// <summary>
    /// Job positions available on the ship.
    /// </summary>
    public enum Job
    {
        // Command
        Captain,
        ExecutiveOfficer,

        // Operations
        Pilot,
        Operator,

        // Technical
        Engineer,
        Machinist,

        // Medical
        Medical,

        // Hangar
        DeckCrew,

        // Logistics & Admin
        Logistics,
        Admin,

        // Security
        Armsman,

        // Combat
        Marine
    }

    /// <summary>
    /// Role qualifications/specializations within jobs.
    /// A crew member may have multiple roles.
    /// </summary>
    public enum Role
    {
        None,   // No specialization / not applicable

        // Pilot roles
        Fighter,        // Single-seat combat craft
        Bomber,         // Attack craft, torpedoes
        Shuttle,        // Transport, utility craft
        Capital,        // Starship helm qualified

        // Marine roles
        Rifleman,       // Standard infantry
        Marksman,       // Long-range precision
        Demolitions,    // Explosives, breaching
        Shocktrooper,   // Aggressive close-quarters

        // Machinist roles
        Fabrication,    // Creating parts from raw materials
        Repair,         // Fixing equipment/components

        // Engineer roles
        Power,          // Reactor, electrical, power distribution
        Propulsion,     // Engines, thrusters, FTL
        Systems,        // Computers, life support, shields

        // Operator roles
        Sensors,        // Detection, scanning, tracking
        Comms,          // Communications, signals
        Weapons,        // Weapons console operation
        Tactical,       // CIC coordination, threat assessment

        // Medical roles
        Combat,         // Field medicine, deploys with Marines
        Trauma,         // Emergency surgery, critical care
        General,        // Routine care, checkups

        // DeckCrew roles
        Mechanic,       // Repairs fighters/shuttles
        Ordnance,       // Loads weapons/fuel
        Handler,        // Moves craft, directs launches

        // Logistics roles
        Quartermaster,  // Senior - inventory management, requisitions
        Storekeeper,    // Stock management, issues supplies
        CargoTech,      // Physical labor - moving, loading, stocking

        // Admin roles
        Yeoman,         // Clerical, paperwork, correspondence
        Personnel       // HR - records, assignments, crew welfare, guests
    }

    /// <summary>
    /// Defines role adjacency for cross-training.
    /// Adjacent roles are easier/more likely to acquire through experience.
    /// </summary>
    public static class RoleAdjacency
    {
        private static Dictionary<Role, Role[]> adjacentRoles;
        private static Dictionary<Role, Role> baseRoles;
        private static bool initialized = false;

        /// <summary>
        /// Get roles that are adjacent (easy to cross-train) to the given role.
        /// </summary>
        public static Role[] GetAdjacentRoles(Role role)
        {
            EnsureInitialized();
            return adjacentRoles.TryGetValue(role, out var adjacent) ? adjacent : new Role[0];
        }

        /// <summary>
        /// Get the base/prerequisite role for a specialization.
        /// e.g., Marksman requires Rifleman base.
        /// </summary>
        public static Role GetBaseRole(Role role)
        {
            EnsureInitialized();
            return baseRoles.TryGetValue(role, out var baseRole) ? baseRole : Role.None;
        }

        /// <summary>
        /// Check if two roles are adjacent (easy to cross-train between).
        /// </summary>
        public static bool AreAdjacent(Role role1, Role role2)
        {
            if (role1 == role2) return true;

            var adjacent = GetAdjacentRoles(role1);
            foreach (var r in adjacent)
            {
                if (r == role2) return true;
            }
            return false;
        }

        /// <summary>
        /// Generate additional roles based on experience and primary role.
        /// </summary>
        /// <param name="primaryRole">The character's main role</param>
        /// <param name="yearsOfService">Experience level</param>
        /// <param name="isProdigy">Prodigies learn faster</param>
        /// <param name="rng">Random for consistent generation</param>
        /// <returns>Array of roles including primary</returns>
        public static Role[] GenerateRolesWithExperience(Role primaryRole, int yearsOfService, bool isProdigy, System.Random rng)
        {
            EnsureInitialized();

            var roles = new List<Role> { primaryRole };

            // Add base role if this is a specialization
            var baseRole = GetBaseRole(primaryRole);
            if (baseRole != Role.None && baseRole != primaryRole && !roles.Contains(baseRole))
            {
                roles.Add(baseRole);
            }

            // Calculate how many additional roles based on experience
            // Base: 0 extra roles
            // 5+ years: chance for 1 extra
            // 10+ years: chance for 2 extra
            // 15+ years: chance for 3 extra
            // Prodigies: +1 to all thresholds
            int prodigyBonus = isProdigy ? 1 : 0;
            int maxExtraRoles = 0;

            if (yearsOfService >= 5) maxExtraRoles = 1 + prodigyBonus;
            if (yearsOfService >= 10) maxExtraRoles = 2 + prodigyBonus;
            if (yearsOfService >= 15) maxExtraRoles = 3 + prodigyBonus;

            // Try to add adjacent roles
            var adjacent = GetAdjacentRoles(primaryRole);
            if (adjacent.Length > 0 && maxExtraRoles > 0)
            {
                // Shuffle adjacent roles
                var shuffled = new List<Role>(adjacent);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
                }

                // Add roles with decreasing probability
                int added = 0;
                foreach (var adjRole in shuffled)
                {
                    if (added >= maxExtraRoles) break;
                    if (roles.Contains(adjRole)) continue;

                    // 70% chance for first extra, 50% for second, 30% for third
                    float chance = added switch
                    {
                        0 => 0.7f,
                        1 => 0.5f,
                        2 => 0.3f,
                        _ => 0.2f
                    };

                    // Prodigies get +20% chance
                    if (isProdigy) chance += 0.2f;

                    if (rng.NextDouble() < chance)
                    {
                        roles.Add(adjRole);
                        added++;
                    }
                }
            }

            return roles.ToArray();
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            Initialize();
        }

        private static void Initialize()
        {
            // Define adjacent roles (roles that are easy to cross-train)
            adjacentRoles = new Dictionary<Role, Role[]>
            {
                // Pilot roles - natural progression
                [Role.Shuttle] = new[] { Role.Fighter, Role.Capital },
                [Role.Fighter] = new[] { Role.Shuttle, Role.Bomber, Role.Capital },
                [Role.Bomber] = new[] { Role.Fighter, Role.Capital },
                [Role.Capital] = new[] { Role.Shuttle, Role.Fighter, Role.Bomber },

                // Marine roles - all marines start as riflemen
                [Role.Rifleman] = new[] { Role.Marksman, Role.Shocktrooper, Role.Demolitions },
                [Role.Marksman] = new[] { Role.Rifleman },
                [Role.Shocktrooper] = new[] { Role.Rifleman, Role.Demolitions },
                [Role.Demolitions] = new[] { Role.Rifleman, Role.Shocktrooper },

                // Machinist roles - often overlap
                [Role.Fabrication] = new[] { Role.Repair },
                [Role.Repair] = new[] { Role.Fabrication },

                // Engineer roles - some overlap
                [Role.Power] = new[] { Role.Propulsion, Role.Systems },
                [Role.Propulsion] = new[] { Role.Power },
                [Role.Systems] = new[] { Role.Power },

                // Operator roles - bridge officers often cross-train
                [Role.Sensors] = new[] { Role.Comms, Role.Tactical },
                [Role.Comms] = new[] { Role.Sensors },
                [Role.Weapons] = new[] { Role.Tactical },
                [Role.Tactical] = new[] { Role.Sensors, Role.Weapons },

                // Medical roles
                [Role.Combat] = new[] { Role.Trauma, Role.General },
                [Role.Trauma] = new[] { Role.General, Role.Combat },
                [Role.General] = new[] { Role.Trauma },

                // Deck crew roles - often multi-skilled
                [Role.Mechanic] = new[] { Role.Handler, Role.Ordnance },
                [Role.Ordnance] = new[] { Role.Handler },
                [Role.Handler] = new[] { Role.Mechanic, Role.Ordnance },

                // Logistics roles - supply chain adjacent
                [Role.Quartermaster] = new[] { Role.Storekeeper },
                [Role.Storekeeper] = new[] { Role.Quartermaster, Role.CargoTech },
                [Role.CargoTech] = new[] { Role.Storekeeper },

                // Admin roles - clerical adjacent
                [Role.Yeoman] = new[] { Role.Personnel },
                [Role.Personnel] = new[] { Role.Yeoman }
            };

            // Define base roles (prerequisites for specializations)
            baseRoles = new Dictionary<Role, Role>
            {
                // All marine specialists have rifleman base training
                [Role.Marksman] = Role.Rifleman,
                [Role.Shocktrooper] = Role.Rifleman,
                [Role.Demolitions] = Role.Rifleman,

                // Combat medics have general medical training
                [Role.Combat] = Role.General,

                // Trauma surgeons have general training
                [Role.Trauma] = Role.General
            };

            initialized = true;
        }

        /// <summary>
        /// Force reload (for hot-reloading in editor).
        /// </summary>
        public static void Reload()
        {
            initialized = false;
            adjacentRoles = null;
            baseRoles = null;
            EnsureInitialized();
        }
    }

    /// <summary>
    /// Defines the requirements and properties of a job position.
    /// </summary>
    [Serializable]
    public class JobDefinition
    {
        public Job Job;
        public ServiceBranch Branch;
        public ProfessionCategory Profession;
        public bool RequiresOfficer;
        public int MinRank;
        public int MaxRank;
        public Role[] AllowedRoles;

        public JobDefinition(
            Job job,
            ServiceBranch branch,
            ProfessionCategory profession,
            bool requiresOfficer,
            int minRank,
            int maxRank,
            params Role[] allowedRoles)
        {
            Job = job;
            Branch = branch;
            Profession = profession;
            RequiresOfficer = requiresOfficer;
            MinRank = minRank;
            MaxRank = maxRank;
            AllowedRoles = allowedRoles.Length > 0 ? allowedRoles : new[] { Role.None };
        }

        /// <summary>
        /// Check if a role is valid for this job.
        /// </summary>
        public bool IsRoleAllowed(Role role)
        {
            if (AllowedRoles == null || AllowedRoles.Length == 0)
                return role == Role.None;

            foreach (var allowed in AllowedRoles)
            {
                if (allowed == role) return true;
            }
            return false;
        }

        /// <summary>
        /// Check if a character qualifies for this job.
        /// </summary>
        public bool QualifiesFor(Character character, Role role)
        {
            if (character.Branch != Branch) return false;
            if (character.IsOfficer != RequiresOfficer) return false;
            if (character.Rank < MinRank || character.Rank > MaxRank) return false;
            if (!IsRoleAllowed(role)) return false;
            return true;
        }
    }

    /// <summary>
    /// Static registry of all job definitions.
    /// </summary>
    public static class JobDefinitions
    {
        private static Dictionary<Job, JobDefinition> definitions;
        private static bool initialized = false;

        public static JobDefinition Get(Job job)
        {
            EnsureInitialized();
            return definitions.TryGetValue(job, out var def) ? def : null;
        }

        public static IEnumerable<JobDefinition> GetAll()
        {
            EnsureInitialized();
            return definitions.Values;
        }

        /// <summary>
        /// Get all jobs that a character qualifies for.
        /// </summary>
        public static List<(Job job, Role role)> GetQualifiedPositions(Character character, Role[] characterRoles)
        {
            EnsureInitialized();
            var result = new List<(Job, Role)>();

            foreach (var kvp in definitions)
            {
                var def = kvp.Value;

                // Check basic qualifications (branch, officer status, rank)
                if (character.Branch != def.Branch) continue;
                if (character.IsOfficer != def.RequiresOfficer) continue;
                if (character.Rank < def.MinRank || character.Rank > def.MaxRank) continue;

                // Check if any of the character's roles match
                if (def.AllowedRoles[0] == Role.None)
                {
                    // Job doesn't require a specific role
                    result.Add((kvp.Key, Role.None));
                }
                else
                {
                    foreach (var charRole in characterRoles)
                    {
                        if (def.IsRoleAllowed(charRole))
                        {
                            result.Add((kvp.Key, charRole));
                        }
                    }
                }
            }

            return result;
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            Initialize();
        }

        private static void Initialize()
        {
            definitions = new Dictionary<Job, JobDefinition>
            {
                // Command
                [Job.Captain] = new JobDefinition(
                    Job.Captain, ServiceBranch.Navy, ProfessionCategory.Command,
                    requiresOfficer: true, minRank: 6, maxRank: 6  // O-6 Captain
                ),
                [Job.ExecutiveOfficer] = new JobDefinition(
                    Job.ExecutiveOfficer, ServiceBranch.Navy, ProfessionCategory.Command,
                    requiresOfficer: true, minRank: 4, maxRank: 5  // O-4 to O-5
                ),

                // Pilot
                [Job.Pilot] = new JobDefinition(
                    Job.Pilot, ServiceBranch.Navy, ProfessionCategory.Pilot,
                    requiresOfficer: true, minRank: 1, maxRank: 4,  // O-1 to O-4
                    Role.Fighter, Role.Bomber, Role.Shuttle, Role.Capital
                ),

                // Operator
                [Job.Operator] = new JobDefinition(
                    Job.Operator, ServiceBranch.Navy, ProfessionCategory.Operations,
                    requiresOfficer: true, minRank: 1, maxRank: 3,  // O-1 to O-3
                    Role.Sensors, Role.Comms, Role.Weapons, Role.Tactical
                ),

                // Engineer
                [Job.Engineer] = new JobDefinition(
                    Job.Engineer, ServiceBranch.Navy, ProfessionCategory.Engineering,
                    requiresOfficer: false, minRank: 3, maxRank: 8,  // E-3 to E-8
                    Role.Power, Role.Propulsion, Role.Systems
                ),

                // Machinist
                [Job.Machinist] = new JobDefinition(
                    Job.Machinist, ServiceBranch.Navy, ProfessionCategory.Maintenance,
                    requiresOfficer: false, minRank: 3, maxRank: 7,  // E-3 to E-7
                    Role.Fabrication, Role.Repair
                ),

                // Medical
                [Job.Medical] = new JobDefinition(
                    Job.Medical, ServiceBranch.Navy, ProfessionCategory.Medical,
                    requiresOfficer: false, minRank: 3, maxRank: 8,  // E-3 to E-8 for corpsmen
                    Role.Combat, Role.Trauma, Role.General
                ),
                // Note: Doctors (officers) would need a separate definition or this expanded

                // DeckCrew
                [Job.DeckCrew] = new JobDefinition(
                    Job.DeckCrew, ServiceBranch.Navy, ProfessionCategory.Maintenance,
                    requiresOfficer: false, minRank: 1, maxRank: 6,  // E-1 to E-6
                    Role.Mechanic, Role.Ordnance, Role.Handler
                ),

                // Logistics (enlisted and officer)
                [Job.Logistics] = new JobDefinition(
                    Job.Logistics, ServiceBranch.Navy, ProfessionCategory.Administration,
                    requiresOfficer: false, minRank: 1, maxRank: 7,  // E-1 to E-7 for enlisted
                    Role.Quartermaster, Role.Storekeeper, Role.CargoTech
                ),

                // Admin
                [Job.Admin] = new JobDefinition(
                    Job.Admin, ServiceBranch.Navy, ProfessionCategory.Administration,
                    requiresOfficer: false, minRank: 2, maxRank: 7,  // E-2 to E-7
                    Role.Yeoman, Role.Personnel
                ),

                // Armsman
                [Job.Armsman] = new JobDefinition(
                    Job.Armsman, ServiceBranch.Navy, ProfessionCategory.Combat,
                    requiresOfficer: false, minRank: 3, maxRank: 7  // E-3 to E-7
                ),

                // Marine
                [Job.Marine] = new JobDefinition(
                    Job.Marine, ServiceBranch.Marine, ProfessionCategory.Combat,
                    requiresOfficer: false, minRank: 1, maxRank: 8,  // E-1 to E-8
                    Role.Rifleman, Role.Marksman, Role.Demolitions, Role.Shocktrooper
                )
            };

            initialized = true;
            Debug.Log($"[JobDefinitions] Initialized {definitions.Count} job definitions");
        }

        /// <summary>
        /// Force reload definitions (useful for hot-reloading in editor).
        /// </summary>
        public static void Reload()
        {
            initialized = false;
            EnsureInitialized();
        }
    }
}
