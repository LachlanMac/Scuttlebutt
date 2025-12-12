using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Starbelter.AI;
using Starbelter.Arena;
using Starbelter.Unit;

namespace Starbelter.Core
{
    /// <summary>
    /// Manages all crew members on the ship.
    /// Finds beds, generates crew, spawns units, provides lookups.
    /// </summary>
    public class CrewManager : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("Unit prefab to spawn for each crew member")]
        [SerializeField] private GameObject unitPrefab;

        [Header("Settings")]
        [Tooltip("Auto-find all beds and generate crew on Start")]
        [SerializeField] private bool autoGenerateOnStart = true;

        [Tooltip("Log crew roster to console on generation")]
        [SerializeField] private bool logRosterOnGenerate = true;

        [Header("Crew Summary (Runtime)")]
        [SerializeField] private int totalCrew;
        [SerializeField] private int officerCount;
        [SerializeField] private int enlistedCount;
        [SerializeField] private int marineCount;
        [SerializeField] private int navyCount;

        // Runtime data
        private List<Bed> allBeds = new List<Bed>();
        private List<CrewMember> allCrew = new List<CrewMember>();
        private Dictionary<Job, List<CrewMember>> crewByJob = new Dictionary<Job, List<CrewMember>>();
        private Dictionary<Shift, List<CrewMember>> crewByShift = new Dictionary<Shift, List<CrewMember>>();
        private Dictionary<CrewMember, UnitController> spawnedUnits = new Dictionary<CrewMember, UnitController>();
        private Dictionary<Bed, ArenaFloor> bedFloors = new Dictionary<Bed, ArenaFloor>();

        // Cached reference to ship root
        private Transform shipRoot;
        private Starbelter.Arena.Arena arena;

        /// <summary>
        /// The ship this CrewManager belongs to.
        /// </summary>
        public Transform ShipRoot => shipRoot;

        private void Awake()
        {
            shipRoot = transform.root;
            arena = GetComponentInParent<Starbelter.Arena.Arena>();
        }

        private void Start()
        {
            if (autoGenerateOnStart)
            {
                FindAllBeds();
                GenerateAllCrew();

                if (logRosterOnGenerate)
                {
                    LogCrewRoster();
                }
            }
        }

        /// <summary>
        /// Find all Bed components on this ship and cache their floor references.
        /// </summary>
        public void FindAllBeds()
        {
            allBeds.Clear();
            bedFloors.Clear();

            if (shipRoot == null)
            {
                Debug.LogError("[CrewManager] No ship root assigned!");
                return;
            }

            // Only find beds that are children of this ship
            allBeds.AddRange(shipRoot.GetComponentsInChildren<Bed>());

            // Cache floor reference for each bed
            foreach (var bed in allBeds)
            {
                var floor = bed.GetComponentInParent<ArenaFloor>();
                if (floor != null)
                {
                    bedFloors[bed] = floor;
                }
            }

            Debug.Log($"[CrewManager] {shipRoot.name}: Found {allBeds.Count} beds");
        }

        /// <summary>
        /// Generate crew for all beds that haven't generated yet, then spawn units.
        /// </summary>
        public void GenerateAllCrew()
        {
            allCrew.Clear();
            crewByJob.Clear();
            crewByShift.Clear();
            spawnedUnits.Clear();

            foreach (var bed in allBeds)
            {
                if (bed.AssignedCrew == null && !string.IsNullOrEmpty(bed.PositionId))
                {
                    bed.GenerateCrew();
                }

                if (bed.AssignedCrew != null)
                {
                    RegisterCrew(bed.AssignedCrew);
                    SpawnUnitAtBed(bed);
                }
            }

            UpdateSummary();
            Debug.Log($"[CrewManager] {shipRoot.name}: Generated {allCrew.Count} crew members, spawned {spawnedUnits.Count} units");
        }

        /// <summary>
        /// Spawn a unit at a bed's position with correct floor layer.
        /// </summary>
        private void SpawnUnitAtBed(Bed bed)
        {
            if (unitPrefab == null)
            {
                Debug.LogWarning("[CrewManager] No unit prefab assigned - skipping unit spawn");
                return;
            }

            var crew = bed.AssignedCrew;
            if (crew == null) return;

            // Spawn at bed position
            var unitObj = Instantiate(unitPrefab, bed.transform.position, Quaternion.identity);
            unitObj.name = $"Unit_{crew.RankAndName}";

            // Parent to arena if available
            if (arena != null)
            {
                unitObj.transform.SetParent(arena.transform);
            }

            // Get UnitController and set character
            var unitController = unitObj.GetComponent<UnitController>();
            if (unitController != null)
            {
                unitController.SetCharacter(crew.Character);
                unitController.SetArena(arena);
                spawnedUnits[crew] = unitController;
            }

            // Set floor layer
            if (bedFloors.TryGetValue(bed, out var floor))
            {
                floor.SetUnitLayer(unitObj);
                floor.RegisterUnit(unitController);
            }

            // Set up appearance
            var appearance = unitObj.GetComponent<CharacterAppearance>();
            if (appearance != null)
            {
                bool isMale = crew.Character.Gender == Gender.Male;
                appearance.Initialize(isMale, crew.Character.SkinTone, crew.Character.HairStyle, crew.Character.HairColor);
            }
        }

        /// <summary>
        /// Register a crew member in the lookup tables.
        /// </summary>
        private void RegisterCrew(CrewMember crew)
        {
            allCrew.Add(crew);

            // By job
            if (!crewByJob.ContainsKey(crew.AssignedJob))
            {
                crewByJob[crew.AssignedJob] = new List<CrewMember>();
            }
            crewByJob[crew.AssignedJob].Add(crew);

            // By shift
            if (!crewByShift.ContainsKey(crew.AssignedShift))
            {
                crewByShift[crew.AssignedShift] = new List<CrewMember>();
            }
            crewByShift[crew.AssignedShift].Add(crew);
        }

        /// <summary>
        /// Update the summary counts.
        /// </summary>
        private void UpdateSummary()
        {
            totalCrew = allCrew.Count;
            officerCount = allCrew.Count(c => c.IsOfficer);
            enlistedCount = allCrew.Count(c => !c.IsOfficer);
            marineCount = allCrew.Count(c => c.Branch == ServiceBranch.Marine);
            navyCount = allCrew.Count(c => c.Branch == ServiceBranch.Navy);
        }

        /// <summary>
        /// Log the crew roster to the console.
        /// </summary>
        public void LogCrewRoster()
        {
            var sb = new StringBuilder();
            string shipName = shipRoot != null ? shipRoot.name : "Unknown Ship";

            sb.AppendLine($"=== CREW ROSTER: {shipName} ===");
            sb.AppendLine($"Total: {totalCrew} ({officerCount} officers, {enlistedCount} enlisted)");
            sb.AppendLine($"Navy: {navyCount}, Marines: {marineCount}");
            sb.AppendLine();

            // Group by job
            foreach (var kvp in crewByJob.OrderBy(k => k.Key))
            {
                sb.AppendLine($"--- {kvp.Key} ({kvp.Value.Count}) ---");
                foreach (var crew in kvp.Value.OrderByDescending(c => c.Rank))
                {
                    sb.AppendLine($"  [{crew.AssignedShift}] {crew.RankAndName} - {crew.GetRolesString()}");
                }
            }

            sb.AppendLine("===================");
            Debug.Log(sb.ToString());
        }

        #region Lookups

        /// <summary>
        /// Get all crew members.
        /// </summary>
        public IReadOnlyList<CrewMember> GetAllCrew() => allCrew;

        /// <summary>
        /// Get crew by job.
        /// </summary>
        public List<CrewMember> GetCrewByJob(Job job)
        {
            return crewByJob.TryGetValue(job, out var list) ? list : new List<CrewMember>();
        }

        /// <summary>
        /// Get crew by shift.
        /// </summary>
        public List<CrewMember> GetCrewByShift(Shift shift)
        {
            return crewByShift.TryGetValue(shift, out var list) ? list : new List<CrewMember>();
        }

        /// <summary>
        /// Get crew who have a specific role.
        /// </summary>
        public List<CrewMember> GetCrewWithRole(Role role)
        {
            return allCrew.Where(c => c.HasRole(role)).ToList();
        }

        /// <summary>
        /// Find a crew member by name (partial match).
        /// </summary>
        public CrewMember FindCrewByName(string name)
        {
            return allCrew.FirstOrDefault(c =>
                c.Character.FullName.Contains(name, System.StringComparison.OrdinalIgnoreCase) ||
                c.Character.Callsign.Contains(name, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get the spawned unit for a crew member.
        /// </summary>
        public UnitController GetUnit(CrewMember crew)
        {
            return spawnedUnits.TryGetValue(crew, out var unit) ? unit : null;
        }

        /// <summary>
        /// Get all spawned units.
        /// </summary>
        public IReadOnlyCollection<UnitController> GetAllUnits() => spawnedUnits.Values;

        #endregion

        #region Editor

        [ContextMenu("Regenerate All Crew")]
        private void EditorRegenerateAllCrew()
        {
            FindAllBeds();
            foreach (var bed in allBeds)
            {
                bed.Regenerate();
            }
            GenerateAllCrew();
            LogCrewRoster();
        }

        [ContextMenu("Log Crew Roster")]
        private void EditorLogRoster()
        {
            LogCrewRoster();
        }

        #endregion
    }
}
