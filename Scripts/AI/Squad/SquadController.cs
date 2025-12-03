using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;
using Starbelter.Combat;

namespace Starbelter.AI
{
    /// <summary>
    /// Manages a squad of units. Spawns them in a grid formation and assigns team.
    /// Simplified version - focuses on spawning and basic squad management.
    /// </summary>
    public class SquadController : MonoBehaviour
    {
        [Header("Squad Settings")]
        [SerializeField] private Team team = Team.Federation;
        [SerializeField] private GameObject unitPrefab;

        [Header("Roster Settings")]
        [Tooltip("Number of enlisted members (officers are added automatically)")]
        [SerializeField] private int enlistedCount = 3;
        [Tooltip("Maximum officers to include (1-2, randomly chosen)")]
        [SerializeField] private int maxOfficers = 2;
        [Tooltip("Seed for reproducible squad composition (-1 for random)")]
        [SerializeField] private int rosterSeed = -1;
        [Tooltip("Use roster data to populate squad characters")]
        [SerializeField] private bool useRoster = true;

        [Header("Spawn Position")]
        [SerializeField] private Transform spawnPoint;

        [Header("Rally Point")]
        [Tooltip("Squad will prefer fighting positions near this point. Defaults to spawn point.")]
        [SerializeField] private Transform rallyPoint;

        [Header("Combat Posture")]
        [Tooltip("Squad combat posture - affects cover selection and aggression")]
        [SerializeField] private Posture squadPosture = Posture.Neutral;

        private List<UnitController> members = new List<UnitController>();
        private UnitController leader;
        private int unitCount;
        private bool hasBeenEngaged = false;

        public Team Team => team;
        public bool HasBeenEngaged => hasBeenEngaged;
        public IReadOnlyList<UnitController> Members => members;
        public UnitController Leader => leader;

        /// <summary>
        /// The rally point position. Units prefer cover positions near this point.
        /// </summary>
        public Vector3? RallyPointPosition
        {
            get
            {
                if (rallyPoint != null) return rallyPoint.position;
                if (spawnPoint != null) return spawnPoint.position;
                return transform.position;
            }
        }

        /// <summary>
        /// Gets the number of alive units in the squad.
        /// </summary>
        public int GetAliveUnitCount()
        {
            int count = 0;
            foreach (var member in members)
            {
                if (member != null && !member.IsDead)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Returns true if any squad member has a target - squad is in combat.
        /// </summary>
        public bool IsEngaged
        {
            get
            {
                foreach (var member in members)
                {
                    if (member != null && !member.IsDead)
                    {
                        if (member.CurrentTarget != null)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Called when a unit spots an enemy for the first time.
        /// If squad hasn't been engaged yet, the spotter calls out FIRST_CONTACT.
        /// </summary>
        public void AlertSquadContact(UnitController spotter, Vector3 enemyPosition)
        {
            if (hasBeenEngaged) return;

            hasBeenEngaged = true;
            spotter.ShowRadioMessageDelayed("FIRST_CONTACT");

            // TODO: Alert other squad members about threat direction
            Debug.Log($"[{name}] FIRST CONTACT! Spotted by {spotter.name} at {enemyPosition}");
        }

        /// <summary>
        /// Called when any enemy is killed. Random squad member celebrates.
        /// </summary>
        public void NotifyEnemyKilled(UnitController killer)
        {
            // Killer or random alive member says it
            var speaker = killer;
            if (speaker == null || speaker.IsDead)
            {
                speaker = GetRandomAliveMember();
            }

            speaker?.ShowRadioMessageDelayed("ENEMY_DOWN");

            // Check if all enemies are down
            CheckForSquadClear();
        }

        /// <summary>
        /// Called when a squad member dies.
        /// </summary>
        public void NotifyAllyDown(UnitController downed)
        {
            var speaker = GetRandomAliveMember();
            speaker?.ShowRadioMessageDelayed("ALLY_DOWN");
        }

        private void CheckForSquadClear()
        {
            // Find all enemy units
            var allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
            bool anyEnemiesAlive = false;

            foreach (var unit in allUnits)
            {
                if (unit.Team != team && !unit.IsDead)
                {
                    anyEnemiesAlive = true;
                    break;
                }
            }

            if (!anyEnemiesAlive && hasBeenEngaged)
            {
                var speaker = GetRandomAliveMember();
                speaker?.ShowRadioMessageDelayed("SQUAD_CLEAR", 1f, 3f);
            }
        }

        private UnitController GetRandomAliveMember()
        {
            var alive = new List<UnitController>();
            foreach (var member in members)
            {
                if (member != null && !member.IsDead)
                {
                    alive.Add(member);
                }
            }

            if (alive.Count == 0) return null;
            return alive[Random.Range(0, alive.Count)];
        }

        private void Start()
        {
            // Delay enemy squad spawn to let allies get into position first
            if (team == Team.Empire)
            {
                StartCoroutine(DelayedSpawn(5f));
            }
            else
            {
                SpawnSquad();
            }
        }

        private System.Collections.IEnumerator DelayedSpawn(float delay)
        {
            yield return new WaitForSeconds(delay);
            SpawnSquad();
        }

        private void SpawnSquad()
        {
            if (unitPrefab == null)
            {
                Debug.LogWarning("[SquadController] No unit prefab assigned!");
                return;
            }

            // Build squad roster from data if enabled
            List<Character> squadRoster = null;
            if (useRoster)
            {
                squadRoster = team == Team.Federation
                    ? DataLoader.BuildAllySquad(enlistedCount, maxOfficers, rosterSeed)
                    : DataLoader.BuildEnemySquad(enlistedCount, maxOfficers, rosterSeed);

                unitCount = squadRoster.Count;
            }
            else
            {
                unitCount = enlistedCount + 1; // Default if no roster
            }

            // Use spawn point position, or this object's position as fallback
            Vector3 center = spawnPoint != null ? spawnPoint.position : transform.position;

            // Calculate grid positions around spawn point
            var spawnPositions = GetGridPositions(center, unitCount);

            for (int i = 0; i < unitCount && i < spawnPositions.Count; i++)
            {
                Character character = squadRoster != null && i < squadRoster.Count ? squadRoster[i] : null;
                SpawnUnit(spawnPositions[i], i, character);
            }

            // Set squad posture on all units
            SetSquadPosture(squadPosture);
        }

        /// <summary>
        /// Set combat posture for all units in the squad.
        /// </summary>
        public void SetSquadPosture(Posture newPosture)
        {
            squadPosture = newPosture;
            foreach (var member in members)
            {
                if (member != null && !member.IsDead)
                {
                    member.SetPosture(newPosture);
                }
            }
            Debug.Log($"[{gameObject.name}] Squad posture set to {newPosture}");
        }

        private void SpawnUnit(Vector3 position, int index, Character character = null)
        {
            GameObject unitObj = Instantiate(unitPrefab, position, Quaternion.identity);
            unitObj.transform.SetParent(transform);

            // First unit is the leader
            bool isLeader = index == 0;

            // Name the unit based on character or team/index
            string teamPrefix = team == Team.Federation ? "FED" : "IMP";
            string leaderSuffix = isLeader ? "_LEADER" : "";
            if (character != null)
            {
                unitObj.name = $"{teamPrefix}_{character.RankAndName}{leaderSuffix}";
            }
            else
            {
                unitObj.name = $"{teamPrefix}_UNIT_{index + 1}{leaderSuffix}";
            }

            var unitController = unitObj.GetComponent<UnitController>();
            if (unitController != null)
            {
                // Set team and squad FIRST so SetCharacter names correctly
                unitController.SetTeam(team);
                unitController.SetSquad(this, isLeader);

                // Assign character data if provided
                if (character != null)
                {
                    character.InitializeHealth();
                    unitController.SetCharacter(character);
                }
                members.Add(unitController);

                if (isLeader)
                {
                    leader = unitController;
                }

                // Log spawned unit info
                if (character != null)
                {
                    Debug.Log($"[{gameObject.name}] Spawned {character.RankAndName} \"{character.Callsign}\" ({character.Specialization})");
                }
            }
        }

        /// <summary>
        /// Generate grid positions centered around a point.
        /// </summary>
        private List<Vector3> GetGridPositions(Vector3 center, int count)
        {
            var positions = new List<Vector3>();

            // Snap center to nearest tile
            Vector3 snappedCenter = new Vector3(
                Mathf.Round(center.x) + 0.5f,
                Mathf.Round(center.y) + 0.5f,
                0f
            );

            // Determine grid size needed
            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(count));
            int halfGrid = gridSize / 2;

            // Generate positions in a grid pattern
            for (int y = -halfGrid; y <= halfGrid && positions.Count < count; y++)
            {
                for (int x = -halfGrid; x <= halfGrid && positions.Count < count; x++)
                {
                    Vector3 offset = new Vector3(x, y, 0);
                    positions.Add(snappedCenter + offset);
                }
            }

            return positions;
        }

        /// <summary>
        /// Get all living members of the squad.
        /// </summary>
        public List<UnitController> GetLivingMembers()
        {
            members.RemoveAll(m => m == null || m.IsDead);
            return new List<UnitController>(members);
        }

        /// <summary>
        /// Called when the leader dies. Promotes the next living member to leader.
        /// </summary>
        public void OnLeaderDied()
        {
            var living = GetLivingMembers();
            if (living.Count > 0)
            {
                leader = living[0];
                leader.SetSquad(this, true);
                Debug.Log($"[{gameObject.name}] New leader: {leader.name}");
            }
            else
            {
                leader = null;
                Debug.Log($"[{gameObject.name}] Squad wiped out!");
            }
        }

        /// <summary>
        /// Get the leader's position, or null if no leader.
        /// </summary>
        public Vector3? GetLeaderPosition()
        {
            if (leader == null || leader.IsDead) return null;
            return leader.transform.position;
        }

        /// <summary>
        /// Get count of living squad members.
        /// </summary>
        public int LivingMemberCount => GetLivingMembers().Count;

        /// <summary>
        /// Get average health percentage of the squad.
        /// </summary>
        public float SquadHealthPercent
        {
            get
            {
                var living = GetLivingMembers();
                if (living.Count == 0) return 0f;

                float total = 0f;
                foreach (var member in living)
                {
                    if (member.Health != null)
                    {
                        total += member.Health.HealthPercent;
                    }
                }
                return total / living.Count;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Use spawn point position, or this object's position as fallback
            Vector3 center = spawnPoint != null ? spawnPoint.position : transform.position;
            int count = enlistedCount + maxOfficers;

            // Show spawn positions in editor
            var positions = GetGridPositions(center, count);

            Gizmos.color = team == Team.Federation ? Color.blue : Color.red;

            foreach (var pos in positions)
            {
                Gizmos.DrawWireCube(pos, Vector3.one * 0.8f);
            }

            // Draw spawn center
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(center, 0.3f);

            // Draw rally point if set
            if (rallyPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(rallyPoint.position, 0.5f);
                Gizmos.DrawLine(center, rallyPoint.position);
            }
        }
#endif
    }
}
