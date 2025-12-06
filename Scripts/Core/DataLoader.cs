using UnityEngine;
using System.Collections.Generic;
using Starbelter.Combat;

namespace Starbelter.Core
{
    /// <summary>
    /// Loads game data from JSON files.
    /// </summary>
    public static class DataLoader
    {
        private static Dictionary<string, ProjectileWeapon> weapons;
        private static bool weaponsLoaded = false;

        private static Dictionary<string, Character> allyRoster;
        private static Dictionary<string, Character> enemyRoster;
        private static bool rostersLoaded = false;

        private static Dictionary<string, string[]> radioLines;
        private static bool radioLinesLoaded = false;

        /// <summary>
        /// Get a weapon by ID. Returns null if not found.
        /// </summary>
        public static ProjectileWeapon GetWeapon(string id)
        {
            EnsureWeaponsLoaded();
            return weapons.TryGetValue(id, out var weapon) ? CloneWeapon(weapon) : null;
        }

        /// <summary>
        /// Get all weapon IDs.
        /// </summary>
        public static IEnumerable<string> GetWeaponIds()
        {
            EnsureWeaponsLoaded();
            return weapons.Keys;
        }

        /// <summary>
        /// Clone a weapon so each character gets their own instance.
        /// </summary>
        private static ProjectileWeapon CloneWeapon(ProjectileWeapon source)
        {
            return new ProjectileWeapon
            {
                // Base stats
                Name = source.Name,
                Type = source.Type,
                Damage = source.Damage,
                Accuracy = source.Accuracy,
                OptimalRange = source.OptimalRange,
                MaxRange = source.MaxRange,
                MagazineSize = source.MagazineSize,
                CurrentAmmo = source.MagazineSize, // Start with full mag
                ReloadTime = source.ReloadTime,
                ProjectilePrefab = source.ProjectilePrefab,

                // Snap Shot
                SnapAccuracy = source.SnapAccuracy,
                SnapCoverPenetration = source.SnapCoverPenetration,

                // Aimed Shot
                CanAimedShot = source.CanAimedShot,
                AimTime = source.AimTime,
                AimedAccuracy = source.AimedAccuracy,
                AimedCoverPenetration = source.AimedCoverPenetration,

                // Suppressing Fire
                CanSuppress = source.CanSuppress,
                SuppressionEffectiveness = source.SuppressionEffectiveness,
                SuppressAccuracy = source.SuppressAccuracy,
                SuppressFireRateMultiplier = source.SuppressFireRateMultiplier,
                SuppressCoverPenetration = source.SuppressCoverPenetration,

                // Burst Fire
                CanBurst = source.CanBurst,
                BurstCount = source.BurstCount,
                BurstDelay = source.BurstDelay,
                BurstAccuracy = source.BurstAccuracy,
                BurstCoverPenetration = source.BurstCoverPenetration
            };
        }

        private static void EnsureWeaponsLoaded()
        {
            if (weaponsLoaded) return;
            LoadWeapons();
        }

        private static void LoadWeapons()
        {
            weapons = new Dictionary<string, ProjectileWeapon>();

            var jsonAsset = Resources.Load<TextAsset>("Data/Weapons");
            if (jsonAsset == null)
            {
                Debug.LogError("[DataLoader] Failed to load Weapons.json from Resources/Data/");
                weaponsLoaded = true;
                return;
            }

            var data = JsonUtility.FromJson<WeaponDataFile>(jsonAsset.text);
            if (data?.weapons == null)
            {
                Debug.LogError("[DataLoader] Failed to parse Weapons.json");
                weaponsLoaded = true;
                return;
            }

            foreach (var entry in data.weapons)
            {
                var weapon = new ProjectileWeapon
                {
                    // Base stats
                    Name = entry.name,
                    Type = ParseProjectileType(entry.type),
                    Damage = entry.damage,
                    Accuracy = entry.accuracy,
                    OptimalRange = entry.optimalRange,
                    MaxRange = entry.maxRange,
                    MagazineSize = entry.magazineSize,
                    CurrentAmmo = entry.magazineSize,
                    ReloadTime = entry.reloadTime,

                    // Snap Shot
                    SnapAccuracy = entry.snapAccuracy > 0 ? entry.snapAccuracy : 0.7f,
                    SnapCoverPenetration = entry.snapCoverPenetration > 0 ? entry.snapCoverPenetration : 1.0f,

                    // Aimed Shot
                    CanAimedShot = entry.canAimedShot,
                    AimTime = entry.aimTime > 0 ? entry.aimTime : 1.5f,
                    AimedAccuracy = entry.aimedAccuracy > 0 ? entry.aimedAccuracy : 1.0f,
                    AimedCoverPenetration = entry.aimedCoverPenetration > 0 ? entry.aimedCoverPenetration : 0.5f,

                    // Suppressing Fire
                    CanSuppress = entry.canSuppress,
                    SuppressionEffectiveness = entry.suppressionEffectiveness,
                    SuppressAccuracy = entry.suppressAccuracy > 0 ? entry.suppressAccuracy : 0.5f,
                    SuppressFireRateMultiplier = entry.suppressFireRateMultiplier > 0 ? entry.suppressFireRateMultiplier : 2.0f,
                    SuppressCoverPenetration = entry.suppressCoverPenetration > 0 ? entry.suppressCoverPenetration : 1.5f,

                    // Burst Fire
                    CanBurst = entry.canBurst,
                    BurstCount = entry.burstCount > 0 ? entry.burstCount : 3,
                    BurstDelay = entry.burstDelay > 0 ? entry.burstDelay : 0.1f,
                    BurstAccuracy = entry.burstAccuracy > 0 ? entry.burstAccuracy : 0.8f,
                    BurstCoverPenetration = entry.burstCoverPenetration > 0 ? entry.burstCoverPenetration : 1.25f
                };

                weapons[entry.id] = weapon;
                Debug.Log($"[DataLoader] Loaded weapon: {entry.id} ({entry.name}) - Burst:{entry.canBurst}, Suppress:{entry.canSuppress}, Aimed:{entry.canAimedShot}");
            }

            Debug.Log($"[DataLoader] Loaded {weapons.Count} weapons");
            weaponsLoaded = true;
        }

        private static ProjectileType ParseProjectileType(string type)
        {
            return type?.ToLower() switch
            {
                "kinetic" => ProjectileType.Kinetic,
                "energy" => ProjectileType.Energy,
                "plasma" => ProjectileType.Plasma,
                _ => ProjectileType.Kinetic
            };
        }

        /// <summary>
        /// Force reload all data (useful for hot-reloading in editor).
        /// </summary>
        public static void ReloadAll()
        {
            weaponsLoaded = false;
            weapons = null;
            rostersLoaded = false;
            allyRoster = null;
            enemyRoster = null;
            radioLinesLoaded = false;
            radioLines = null;
            EnsureWeaponsLoaded();
            EnsureRostersLoaded();
            EnsureRadioLinesLoaded();
        }

        #region Radio Lines

        /// <summary>
        /// Get a random radio line for the given event.
        /// Returns the event name if no lines found (fallback).
        /// </summary>
        public static string GetRadioLine(string eventName)
        {
            EnsureRadioLinesLoaded();

            if (radioLines.TryGetValue(eventName, out var variants) && variants.Length > 0)
            {
                return variants[Random.Range(0, variants.Length)];
            }

            // Fallback: return event name formatted nicely
            return eventName.Replace("_", " ");
        }

        /// <summary>
        /// Check if a radio event exists.
        /// </summary>
        public static bool HasRadioEvent(string eventName)
        {
            EnsureRadioLinesLoaded();
            return radioLines.ContainsKey(eventName);
        }

        /// <summary>
        /// Get all event names.
        /// </summary>
        public static IEnumerable<string> GetRadioEventNames()
        {
            EnsureRadioLinesLoaded();
            return radioLines.Keys;
        }

        private static void EnsureRadioLinesLoaded()
        {
            if (radioLinesLoaded) return;
            LoadRadioLines();
        }

        private static void LoadRadioLines()
        {
            radioLines = new Dictionary<string, string[]>();

            var jsonAsset = Resources.Load<TextAsset>("Data/RadioLines");
            if (jsonAsset == null)
            {
                Debug.LogWarning("[DataLoader] Failed to load RadioLines.json from Resources/Data/");
                radioLinesLoaded = true;
                return;
            }

            var data = JsonUtility.FromJson<RadioLinesDataFile>(jsonAsset.text);
            if (data?.radioLines == null)
            {
                Debug.LogError("[DataLoader] Failed to parse RadioLines.json");
                radioLinesLoaded = true;
                return;
            }

            foreach (var entry in data.radioLines)
            {
                if (!string.IsNullOrEmpty(entry.@event) && entry.variants != null)
                {
                    radioLines[entry.@event] = entry.variants;
                }
            }

            Debug.Log($"[DataLoader] Loaded {radioLines.Count} radio events");
            radioLinesLoaded = true;
        }

        #endregion

        #region Character Roster

        /// <summary>
        /// Get an ally character by ID. Returns a clone so each instance is unique.
        /// </summary>
        public static Character GetAlly(string id)
        {
            EnsureRostersLoaded();
            return allyRoster.TryGetValue(id, out var character) ? CloneCharacter(character) : null;
        }

        /// <summary>
        /// Get an enemy character by ID. Returns a clone so each instance is unique.
        /// </summary>
        public static Character GetEnemy(string id)
        {
            EnsureRostersLoaded();
            return enemyRoster.TryGetValue(id, out var character) ? CloneCharacter(character) : null;
        }

        /// <summary>
        /// Get all ally character IDs.
        /// </summary>
        public static IEnumerable<string> GetAllyIds()
        {
            EnsureRostersLoaded();
            return allyRoster.Keys;
        }

        /// <summary>
        /// Get all enemy character IDs.
        /// </summary>
        public static IEnumerable<string> GetEnemyIds()
        {
            EnsureRostersLoaded();
            return enemyRoster.Keys;
        }

        /// <summary>
        /// Get all allies as a list (cloned instances).
        /// </summary>
        public static List<Character> GetAllAllies()
        {
            EnsureRostersLoaded();
            var result = new List<Character>();
            foreach (var kvp in allyRoster)
            {
                result.Add(CloneCharacter(kvp.Value));
            }
            return result;
        }

        /// <summary>
        /// Get all enemies as a list (cloned instances).
        /// </summary>
        public static List<Character> GetAllEnemies()
        {
            EnsureRostersLoaded();
            var result = new List<Character>();
            foreach (var kvp in enemyRoster)
            {
                result.Add(CloneCharacter(kvp.Value));
            }
            return result;
        }

        private static Character CloneCharacter(Character source)
        {
            var clone = new Character
            {
                FirstName = source.FirstName,
                LastName = source.LastName,
                Callsign = source.Callsign,
                IsOfficer = source.IsOfficer,
                EnlistedRank = source.EnlistedRank,
                OfficerRank = source.OfficerRank,
                YearsOfService = source.YearsOfService,
                Specialization = source.Specialization,
                MainWeaponId = source.MainWeaponId,
                Vitality = source.Vitality,
                Accuracy = source.Accuracy,
                Reflex = source.Reflex,
                Bravery = source.Bravery,
                Agility = source.Agility,
                Perception = source.Perception,
                Stealth = source.Stealth,
                Tactics = source.Tactics,
                Leadership = source.Leadership,
                PhysicalMitigation = source.PhysicalMitigation,
                HeatMitigation = source.HeatMitigation,
                EnergyMitigation = source.EnergyMitigation,
                IonMitigation = source.IonMitigation
            };
            clone.LoadWeapon();
            return clone;
        }

        private static void EnsureRostersLoaded()
        {
            if (rostersLoaded) return;
            LoadRosters();
        }

        private static void LoadRosters()
        {
            allyRoster = LoadRoster("Data/AllyRoster");
            enemyRoster = LoadRoster("Data/EnemyRoster");
            rostersLoaded = true;
        }

        private static Dictionary<string, Character> LoadRoster(string path)
        {
            var roster = new Dictionary<string, Character>();

            var jsonAsset = Resources.Load<TextAsset>(path);
            if (jsonAsset == null)
            {
                Debug.LogWarning($"[DataLoader] Failed to load {path}.json from Resources/");
                return roster;
            }

            var data = JsonUtility.FromJson<CharacterDataFile>(jsonAsset.text);
            if (data?.characters == null)
            {
                Debug.LogError($"[DataLoader] Failed to parse {path}.json");
                return roster;
            }

            foreach (var entry in data.characters)
            {
                var character = new Character
                {
                    FirstName = entry.firstName,
                    LastName = entry.lastName,
                    Callsign = entry.callsign,
                    IsOfficer = entry.isOfficer,
                    EnlistedRank = ParseEnlistedRank(entry.enlistedRank),
                    OfficerRank = ParseOfficerRank(entry.officerRank),
                    YearsOfService = entry.yearsOfService,
                    Specialization = ParseSpecialization(entry.specialization),
                    MainWeaponId = entry.mainWeaponId,
                    Vitality = entry.vitality,
                    Accuracy = entry.accuracy,
                    Reflex = entry.reflex,
                    Bravery = entry.bravery,
                    Agility = entry.agility,
                    Perception = entry.perception,
                    Stealth = entry.stealth,
                    Tactics = entry.tactics,
                    Leadership = entry.leadership
                };

                roster[entry.id] = character;
                Debug.Log($"[DataLoader] Loaded character: {entry.id} ({character.RankAndName} \"{character.Callsign}\")");
            }

            Debug.Log($"[DataLoader] Loaded {roster.Count} characters from {path}");
            return roster;
        }

        private static MarineEnlistedRank ParseEnlistedRank(string rank)
        {
            return rank switch
            {
                "Private" => MarineEnlistedRank.Private,
                "PrivateFirstClass" => MarineEnlistedRank.PrivateFirstClass,
                "LanceCorporal" => MarineEnlistedRank.LanceCorporal,
                "Corporal" => MarineEnlistedRank.Corporal,
                "Sergeant" => MarineEnlistedRank.Sergeant,
                "StaffSergeant" => MarineEnlistedRank.StaffSergeant,
                "GunnerySergeant" => MarineEnlistedRank.GunnerySergeant,
                "MasterSergeant" => MarineEnlistedRank.MasterSergeant,
                "FirstSergeant" => MarineEnlistedRank.FirstSergeant,
                "MasterGunnerySergeant" => MarineEnlistedRank.MasterGunnerySergeant,
                "SergeantMajor" => MarineEnlistedRank.SergeantMajor,
                _ => MarineEnlistedRank.Private
            };
        }

        private static MarineOfficerRank ParseOfficerRank(string rank)
        {
            return rank switch
            {
                "SecondLieutenant" => MarineOfficerRank.SecondLieutenant,
                "FirstLieutenant" => MarineOfficerRank.FirstLieutenant,
                "Captain" => MarineOfficerRank.Captain,
                "Major" => MarineOfficerRank.Major,
                "LieutenantColonel" => MarineOfficerRank.LieutenantColonel,
                "Colonel" => MarineOfficerRank.Colonel,
                "BrigadierGeneral" => MarineOfficerRank.BrigadierGeneral,
                "MajorGeneral" => MarineOfficerRank.MajorGeneral,
                "LieutenantGeneral" => MarineOfficerRank.LieutenantGeneral,
                "General" => MarineOfficerRank.General,
                _ => MarineOfficerRank.SecondLieutenant
            };
        }

        private static Specialization ParseSpecialization(string spec)
        {
            return spec switch
            {
                "Rifleman" => Specialization.Rifleman,
                "Shocktrooper" => Specialization.Shocktrooper,
                "Marksman" => Specialization.Marksman,
                _ => Specialization.Rifleman
            };
        }

        #endregion

        #region Squad Building

        /// <summary>
        /// Build a random ally squad with 1-2 officers and X enlisted members.
        /// Officers are placed first (highest rank = squad leader).
        /// </summary>
        /// <param name="enlistedCount">Number of enlisted members (not including officers)</param>
        /// <param name="maxOfficers">Maximum officers (1-2, randomly chosen)</param>
        /// <param name="seed">Optional seed for reproducible results. Use -1 for random.</param>
        public static List<Character> BuildAllySquad(int enlistedCount, int maxOfficers = 2, int seed = -1)
        {
            EnsureRostersLoaded();
            return BuildSquad(allyRoster, enlistedCount, maxOfficers, seed);
        }

        /// <summary>
        /// Build a random enemy squad with 1-2 officers and X enlisted members.
        /// Officers are placed first (highest rank = squad leader).
        /// </summary>
        /// <param name="enlistedCount">Number of enlisted members (not including officers)</param>
        /// <param name="maxOfficers">Maximum officers (1-2, randomly chosen)</param>
        /// <param name="seed">Optional seed for reproducible results. Use -1 for random.</param>
        public static List<Character> BuildEnemySquad(int enlistedCount, int maxOfficers = 2, int seed = -1)
        {
            EnsureRostersLoaded();
            return BuildSquad(enemyRoster, enlistedCount, maxOfficers, seed);
        }

        private static List<Character> BuildSquad(Dictionary<string, Character> roster, int enlistedCount, int maxOfficers, int seed)
        {
            // Use seeded random if seed provided, otherwise use Unity's Random
            System.Random rng = seed >= 0 ? new System.Random(seed) : null;

            var squad = new List<Character>();
            var usedIds = new HashSet<string>();

            // Separate officers and enlisted
            var officers = new List<(string id, Character character)>();
            var enlisted = new List<(string id, Character character)>();

            foreach (var kvp in roster)
            {
                if (kvp.Value.IsOfficer)
                    officers.Add((kvp.Key, kvp.Value));
                else
                    enlisted.Add((kvp.Key, kvp.Value));
            }

            // Pick 1-2 officers randomly (if available)
            int officerCount = Mathf.Min(NextRange(rng, 1, maxOfficers + 1), officers.Count);
            var shuffledOfficers = ShuffleList(officers, rng);

            for (int i = 0; i < officerCount && i < shuffledOfficers.Count; i++)
            {
                var (id, _) = shuffledOfficers[i];
                usedIds.Add(id);
                squad.Add(CloneCharacter(roster[id]));
            }

            // Pick enlisted members randomly (no repeats)
            var shuffledEnlisted = ShuffleList(enlisted, rng);
            int enlistedAdded = 0;

            foreach (var (id, _) in shuffledEnlisted)
            {
                if (enlistedAdded >= enlistedCount) break;
                if (usedIds.Contains(id)) continue;

                usedIds.Add(id);
                squad.Add(CloneCharacter(roster[id]));
                enlistedAdded++;
            }

            // Sort by rank (highest first - they become squad leader)
            squad.Sort((a, b) => GetRankSortOrder(b).CompareTo(GetRankSortOrder(a)));

            string seedInfo = seed >= 0 ? $" (seed={seed})" : "";
            Debug.Log($"[DataLoader] Built squad with {squad.Count} members{seedInfo}: {string.Join(", ", squad.ConvertAll(c => c.RankAndName))}");

            return squad;
        }

        /// <summary>
        /// Get a numeric sort order for ranks (higher = more senior).
        /// Officers outrank all enlisted.
        /// </summary>
        private static int GetRankSortOrder(Character character)
        {
            if (character.IsOfficer)
            {
                // Officers: O-1 to O-10 map to 100-109
                return 100 + (int)character.OfficerRank;
            }
            else
            {
                // Enlisted: E-1 to E-9 map to 1-11 (based on enum order)
                return (int)character.EnlistedRank + 1;
            }
        }

        /// <summary>
        /// Get random int in range using either seeded RNG or Unity Random.
        /// </summary>
        private static int NextRange(System.Random rng, int minInclusive, int maxExclusive)
        {
            if (rng != null)
                return rng.Next(minInclusive, maxExclusive);
            return Random.Range(minInclusive, maxExclusive);
        }

        private static List<T> ShuffleList<T>(List<T> list, System.Random rng = null)
        {
            var shuffled = new List<T>(list);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = NextRange(rng, 0, i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            return shuffled;
        }

        #endregion

        // JSON data structures
        [System.Serializable]
        private class WeaponDataFile
        {
            public WeaponEntry[] weapons;
        }

        [System.Serializable]
        private class WeaponEntry
        {
            public string id;
            public string name;
            public string type;
            public float damage;
            public float accuracy;
            public float optimalRange;
            public float maxRange;
            public int magazineSize;
            public float reloadTime;

            // Snap Shot
            public float snapAccuracy;
            public float snapCoverPenetration;

            // Aimed Shot
            public bool canAimedShot;
            public float aimTime;
            public float aimedAccuracy;
            public float aimedCoverPenetration;

            // Suppressing Fire
            public bool canSuppress;
            public float suppressionEffectiveness;
            public float suppressAccuracy;
            public float suppressFireRateMultiplier;
            public float suppressCoverPenetration;

            // Burst Fire
            public bool canBurst;
            public int burstCount;
            public float burstDelay;
            public float burstAccuracy;
            public float burstCoverPenetration;
        }

        [System.Serializable]
        private class CharacterDataFile
        {
            public CharacterEntry[] characters;
        }

        [System.Serializable]
        private class CharacterEntry
        {
            public string id;
            public string firstName;
            public string lastName;
            public string callsign;
            public bool isOfficer;
            public string enlistedRank;
            public string officerRank;
            public int yearsOfService;
            public string specialization;
            public string mainWeaponId;
            public int vitality;
            public int accuracy;
            public int reflex;
            public int bravery;
            public int agility;
            public int perception;
            public int stealth;
            public int tactics;
            public int leadership;
        }

        [System.Serializable]
        private class RadioLinesDataFile
        {
            public RadioLineEntry[] radioLines;
        }

        [System.Serializable]
        private class RadioLineEntry
        {
            public string @event;  // @ prefix because 'event' is a C# keyword
            public string[] variants;
        }
    }
}
