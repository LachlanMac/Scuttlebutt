using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Starbelter.Core
{
    /// <summary>
    /// Manages all crew members on the ship.
    /// Finds beds, generates crew, provides lookups.
    /// </summary>
    public class CrewManager : MonoBehaviour
    {
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

        // Reference to the ship root (auto-detected or manually assigned)
        [Header("Ship Reference")]
        [Tooltip("Root transform of this ship. If not set, uses this GameObject's root parent.")]
        [SerializeField] private Transform shipRoot;

        /// <summary>
        /// The ship this CrewManager belongs to.
        /// </summary>
        public Transform ShipRoot => shipRoot;

        private void Awake()
        {
            // Auto-detect ship root if not assigned
            if (shipRoot == null)
            {
                shipRoot = transform.root;
            }
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
        /// Find all Bed components on this ship.
        /// </summary>
        public void FindAllBeds()
        {
            allBeds.Clear();

            if (shipRoot == null)
            {
                Debug.LogError("[CrewManager] No ship root assigned!");
                return;
            }

            // Only find beds that are children of this ship
            allBeds.AddRange(shipRoot.GetComponentsInChildren<Bed>());
            Debug.Log($"[CrewManager] {shipRoot.name}: Found {allBeds.Count} beds");
        }

        /// <summary>
        /// Generate crew for all beds that haven't generated yet.
        /// </summary>
        public void GenerateAllCrew()
        {
            allCrew.Clear();
            crewByJob.Clear();
            crewByShift.Clear();

            foreach (var bed in allBeds)
            {
                if (bed.AssignedCrew == null && !string.IsNullOrEmpty(bed.PositionId))
                {
                    bed.GenerateCrew();
                }

                if (bed.AssignedCrew != null)
                {
                    RegisterCrew(bed.AssignedCrew);
                }
            }

            UpdateSummary();
            Debug.Log($"[CrewManager] {shipRoot.name}: Generated {allCrew.Count} crew members");
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
