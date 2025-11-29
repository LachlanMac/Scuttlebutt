using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;

namespace Starbelter.AI
{
    /// <summary>
    /// Manages a squad of units. Spawns them in a grid formation and assigns team.
    /// </summary>
    public class SquadController : MonoBehaviour
    {
        [Header("Squad Settings")]
        [SerializeField] private Team team = Team.Ally;
        [SerializeField] private GameObject unitPrefab;
        [SerializeField] private int unitCount = 4;

        [Header("Spawn Position")]
        [SerializeField] private Transform spawnPoint;

        [Header("Threat Settings")]
        [Tooltip("Squad threat above this triggers aggressive suppression")]
        [SerializeField] private float highThreatThreshold = 10f;

        private List<UnitController> members = new List<UnitController>();
        private UnitController leader;

        public Team Team => team;
        public IReadOnlyList<UnitController> Members => members;
        public UnitController Leader => leader;

        /// <summary>
        /// Gets the combined threat level of all squad members.
        /// </summary>
        public float SquadThreatLevel
        {
            get
            {
                float total = 0f;
                foreach (var member in members)
                {
                    if (member != null && !member.IsDead && member.ThreatManager != null)
                    {
                        total += member.ThreatManager.GetTotalThreat();
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// Returns true if the squad is under heavy fire and should prioritize suppression.
        /// </summary>
        public bool IsUnderHeavyFire => SquadThreatLevel > highThreatThreshold;

        private void Start()
        {
            SpawnSquad();
        }

        private void Update()
        {
            //Debug.Log($"{gameObject.name} THREAT: {SquadThreatLevel:F1} {(IsUnderHeavyFire ? "[HEAVY FIRE]" : "")}");
        }

        private void SpawnSquad()
        {
            if (unitPrefab == null)
            {
                Debug.LogWarning("[SquadController] No unit prefab assigned!");
                return;
            }

            // Use spawn point position, or this object's position as fallback
            Vector3 center = spawnPoint != null ? spawnPoint.position : transform.position;

            // Calculate grid positions around spawn point
            var spawnPositions = GetGridPositions(center, unitCount);

            for (int i = 0; i < unitCount && i < spawnPositions.Count; i++)
            {
                SpawnUnit(spawnPositions[i], i);
            }
        }

        private void SpawnUnit(Vector3 position, int index)
        {
            GameObject unitObj = Instantiate(unitPrefab, position, Quaternion.identity);
            unitObj.transform.SetParent(transform);

            // First unit is the leader
            bool isLeader = index == 0;

            // Name the unit based on team and index
            string teamPrefix = team == Team.Ally ? "ALLY" : "ENEMY";
            string leaderSuffix = isLeader ? "_LEADER" : "";
            unitObj.name = $"{teamPrefix}_UNIT_{index + 1}{leaderSuffix}";

            var unitController = unitObj.GetComponent<UnitController>();
            if (unitController != null)
            {
                unitController.SetTeam(team);
                unitController.SetSquad(this, isLeader);
                members.Add(unitController);

                if (isLeader)
                {
                    leader = unitController;
                }
            }
        }

        /// <summary>
        /// Generate grid positions centered around a point.
        /// Snaps to tile centers and expands outward in a spiral-like pattern.
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
            }
            else
            {
                leader = null;
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

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Use spawn point position, or this object's position as fallback
            Vector3 center = spawnPoint != null ? spawnPoint.position : transform.position;

            // Show spawn positions in editor
            var positions = GetGridPositions(center, unitCount);

            Gizmos.color = team == Team.Ally ? Color.blue : Color.red;

            foreach (var pos in positions)
            {
                Gizmos.DrawWireCube(pos, Vector3.one * 0.8f);
            }

            // Draw spawn center
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(center, 0.3f);
        }
#endif
    }
}
