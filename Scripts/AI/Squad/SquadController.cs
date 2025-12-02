using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Manages a squad of units. Spawns them in a grid formation and assigns team.
    /// </summary>
    public class SquadController : MonoBehaviour
    {
        [Header("Squad Settings")]
        [SerializeField] private Team team = Team.Federation;
        [SerializeField] private GameObject unitPrefab;
        [SerializeField] private int unitCount = 4;

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

        [Header("Threat Settings")]
        [Tooltip("Squad threat above this triggers aggressive suppression")]
        [SerializeField] private float highThreatThreshold = 10f;

        [Header("Rally Point")]
        [Tooltip("Squad will prefer fighting positions near this point. Defaults to spawn point.")]
        [SerializeField] private Transform rallyPoint;

        [Header("Combat Posture")]
        [Tooltip("Squad combat posture - affects cover selection and aggression")]
        [SerializeField] private Posture squadPosture = Posture.Neutral;

        private List<UnitController> members = new List<UnitController>();
        private UnitController leader;

        // Suppression tracking - one suppressor per target
        private Dictionary<GameObject, UnitController> activeSuppressions = new Dictionary<GameObject, UnitController>();
        private float suppressionCheckTimer;
        private const float SUPPRESSION_CHECK_BASE_INTERVAL = 15f; // Base interval in seconds

        public Team Team => team;
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
        /// Gets the combined threat level of all squad members.
        /// </summary>
        public float SquadThreatLevel
        {
            get
            {
                float total = 0f;
                foreach (var member in members)
                {
                    if (member != null && !member.IsDead && member.PerceptionManager != null)
                    {
                        total += member.PerceptionManager.GetTotalThreat();
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// Returns true if the squad is under heavy fire and should prioritize suppression.
        /// </summary>
        public bool IsUnderHeavyFire => SquadThreatLevel > highThreatThreshold;

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
        /// Returns true if any squad member has active threats - squad is in combat.
        /// </summary>
        public bool IsEngaged
        {
            get
            {
                foreach (var member in members)
                {
                    if (member != null && !member.IsDead && member.PerceptionManager != null)
                    {
                        if (member.PerceptionManager.GetTotalThreat() > 0f)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
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

        private void Update()
        {
            //Debug.Log($"{gameObject.name} THREAT: {SquadThreatLevel:F1} {(IsUnderHeavyFire ? "[HEAVY FIRE]" : "")}");

            // Periodically check for suppression opportunities
            suppressionCheckTimer -= Time.deltaTime;
            if (suppressionCheckTimer <= 0f)
            {
                suppressionCheckTimer = GetSuppressionCheckInterval();
                CleanupSuppressions();
                AssignSuppressions();
            }
        }

        /// <summary>
        /// Get suppression check interval based on leader's Tactics stat.
        /// Formula: 15 seconds - (Tactics / 5) = 13s at Tactics 10, 11s at Tactics 20, 14.8s at Tactics 1
        /// </summary>
        private float GetSuppressionCheckInterval()
        {
            int tactics = leader?.Character?.Tactics ?? 10;
            float interval = SUPPRESSION_CHECK_BASE_INTERVAL - (tactics / 5f);
            return Mathf.Max(5f, interval); // Minimum 5 seconds
        }

        /// <summary>
        /// Clean up suppression assignments for dead targets or dead/busy suppressors.
        /// </summary>
        private void CleanupSuppressions()
        {
            var toRemove = new List<GameObject>();

            foreach (var kvp in activeSuppressions)
            {
                var target = kvp.Key;
                var suppressor = kvp.Value;

                // Remove if target is gone/dead
                if (target == null || !target.activeInHierarchy)
                {
                    toRemove.Add(target);
                    continue;
                }

                var targetable = target.GetComponent<ITargetable>();
                if (targetable != null && targetable.IsDead)
                {
                    toRemove.Add(target);
                    continue;
                }

                // Remove if suppressor is gone/dead/no longer suppressing
                if (suppressor == null || suppressor.IsDead)
                {
                    toRemove.Add(target);
                    continue;
                }

                // Check if suppressor is still in SuppressState
                if (suppressor.GetCurrentStateName() != "SuppressState")
                {
                    toRemove.Add(target);
                }
            }

            foreach (var target in toRemove)
            {
                activeSuppressions.Remove(target);
            }
        }

        /// <summary>
        /// Find high-threat enemies in cover and assign suppressors.
        /// Prioritizes snipers (enemies using aimed shots).
        /// </summary>
        private void AssignSuppressions()
        {
            // Only assign suppression if squad is engaged
            if (!IsEngaged) return;

            int tactics = leader?.Character?.Tactics ?? 10;
            Debug.Log($"[{gameObject.name}] SuppressionCheck: Tactics={tactics}, Interval={GetSuppressionCheckInterval():F1}s");

            // First priority: Find snipers (enemies using aimed shots)
            var snipers = FindSnipers();
            foreach (var sniper in snipers)
            {
                if (activeSuppressions.ContainsKey(sniper))
                {
                    Debug.Log($"[{gameObject.name}] SuppressionCheck: Sniper {sniper.name} already being suppressed");
                    continue;
                }

                float aimedThreat = GetSquadAimedShotThreat(sniper);
                Debug.Log($"[{gameObject.name}] SuppressionCheck: SNIPER DETECTED - {sniper.name} has {aimedThreat:F1} aimed shot threat");

                var suppressor = FindBestSuppressor(sniper);
                if (suppressor != null)
                {
                    activeSuppressions[sniper] = suppressor;
                    Debug.Log($"[{gameObject.name}] SuppressionCheck: Assigning {suppressor.name} to suppress SNIPER {sniper.name}");
                    suppressor.CommandSuppress(sniper);
                }
                else
                {
                    Debug.Log($"[{gameObject.name}] SuppressionCheck: No valid suppressor found for sniper {sniper.name}");
                }
            }

            // Second priority: Other dangerous enemies in cover
            var dangerousEnemies = FindDangerousEnemiesInCover();
            foreach (var enemy in dangerousEnemies)
            {
                // Skip if already being suppressed
                if (activeSuppressions.ContainsKey(enemy)) continue;

                // Skip snipers (already handled above)
                if (snipers.Contains(enemy)) continue;

                Debug.Log($"[{gameObject.name}] SuppressionCheck: Dangerous enemy {enemy.name} in cover");

                var suppressor = FindBestSuppressor(enemy);
                if (suppressor != null)
                {
                    activeSuppressions[enemy] = suppressor;
                    Debug.Log($"[{gameObject.name}] SuppressionCheck: Assigning {suppressor.name} to suppress {enemy.name}");
                    suppressor.CommandSuppress(enemy);
                }
                else
                {
                    Debug.Log($"[{gameObject.name}] SuppressionCheck: No valid suppressor found for {enemy.name}");
                }
            }
        }

        /// <summary>
        /// Find enemies acting as snipers (aimed shot threat >= 2 for any squad member).
        /// Unlike regular dangerous enemies, we suppress snipers even if exposed -
        /// the goal is to disrupt their aimed shots, not just hit someone in cover.
        /// </summary>
        private List<GameObject> FindSnipers()
        {
            // Collect all snipers detected by any squad member
            var snipers = new HashSet<GameObject>();

            foreach (var member in GetLivingMembers())
            {
                if (member.PerceptionManager == null) continue;

                foreach (var perceived in member.PerceptionManager.GetPerceivedEnemies())
                {
                    if (perceived.Unit == null) continue;
                    if (perceived.IsSniper)
                    {
                        snipers.Add(perceived.Unit);
                    }
                }
            }

            if (snipers.Count > 0)
            {
                Debug.Log($"[{gameObject.name}] FindSnipers: {snipers.Count} snipers detected");
            }

            return snipers.ToList();
        }

        /// <summary>
        /// Get max aimed shot threat from an enemy across all squad members.
        /// </summary>
        private float GetSquadAimedShotThreat(GameObject enemy)
        {
            float maxThreat = 0f;
            foreach (var member in GetLivingMembers())
            {
                if (member.PerceptionManager != null)
                {
                    float threat = member.PerceptionManager.GetAimedShotThreat(enemy);
                    if (threat > maxThreat) maxThreat = threat;
                }
            }
            return maxThreat;
        }

        /// <summary>
        /// Find enemies that are dangerous (have shot at us) and are in cover.
        /// </summary>
        private List<GameObject> FindDangerousEnemiesInCover()
        {
            var result = new List<GameObject>();

            // Collect all enemies that have shot at any squad member
            var allDangerousEnemies = new HashSet<GameObject>();
            foreach (var member in GetLivingMembers())
            {
                if (member.PerceptionManager != null)
                {
                    var dangerous = member.PerceptionManager.GetMostDangerousEnemies(3);
                    foreach (var enemy in dangerous)
                    {
                        if (enemy != null) allDangerousEnemies.Add(enemy);
                    }
                }
            }

            // Filter to enemies that are in cover (can't be easily shot)
            foreach (var enemy in allDangerousEnemies)
            {
                // Check if any of our units have clear LOS to this enemy
                bool enemyIsExposed = false;
                foreach (var member in GetLivingMembers())
                {
                    var los = CombatUtils.CheckLineOfSight(member.FirePosition, enemy.transform.position);
                    if (!los.IsBlocked)
                    {
                        enemyIsExposed = true;
                        break;
                    }
                }

                // If no one has clear LOS, enemy is in cover - candidate for suppression
                if (!enemyIsExposed)
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        /// <summary>
        /// Find the best unit to suppress a target.
        /// </summary>
        private UnitController FindBestSuppressor(GameObject target)
        {
            var candidates = FindSuppressCandidates(target, null, 1);
            return candidates.Count > 0 ? candidates[0] : null;
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

                // Update unit count to match roster size
                unitCount = squadRoster.Count;
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

                // Subscribe to perception events for squad intel sharing
                if (unitController.PerceptionManager != null)
                {
                    unitController.PerceptionManager.OnNewContact += (contact) => ShareIntelWithSquad(unitController, contact);
                }

                if (isLeader)
                {
                    leader = unitController;
                }

                // Log spawned unit info
                if (character != null)
                {
                    Debug.Log($"[{gameObject.name}] Spawned {character.RankAndName} \"{character.Callsign}\" ({character.Specialization}) with {character.MainWeapon?.Name ?? "no weapon"}");
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

        #region Squad Intel Sharing

        /// <summary>
        /// Share intel about a new contact with all other squad members.
        /// </summary>
        private void ShareIntelWithSquad(UnitController source, PerceivedUnit contact)
        {
            if (contact?.Unit == null) return;

            foreach (var member in members)
            {
                // Skip dead, null, or source unit
                if (member == null || member.IsDead || member == source) continue;

                // Share intel with this member's PerceptionManager
                if (member.PerceptionManager != null)
                {
                    member.PerceptionManager.ReceiveSquadIntel(contact);
                }
            }
        }

        #endregion

        #region Squad Coordination

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

        /// <summary>
        /// Find units that can provide suppression fire on a target.
        /// Returns units that:
        /// - Are alive and not the requester
        /// - Are in CombatState (ready to engage, not moving/flanking)
        /// - Can suppress from current position (have LOS to target's cover)
        /// - Are in cover themselves (relatively safe)
        /// - Have low personal threat
        /// Uses weighted random selection for less predictable behavior.
        /// </summary>
        public List<UnitController> FindSuppressCandidates(GameObject target, UnitController requester, int maxCount = 2)
        {
            if (target == null) return new List<UnitController>();

            var candidates = new List<(UnitController unit, float score)>();
            Vector3 targetPos = target.transform.position;

            foreach (var member in GetLivingMembers())
            {
                // Skip requester
                if (member == requester) continue;

                // Only consider units in CombatState - they're settled and ready
                var stateName = member.GetCurrentStateName();
                if (stateName != "CombatState")
                {
                    Debug.Log($"[{gameObject.name}] SuppressCandidate {member.name}: SKIP - not in CombatState ({stateName})");
                    continue;
                }

                // Check if they can suppress from current position
                if (!CanSuppressFrom(member, targetPos))
                {
                    Debug.Log($"[{gameObject.name}] SuppressCandidate {member.name}: SKIP - can't suppress from position");
                    continue;
                }

                // Check if they're in cover (relatively safe)
                if (!member.IsInCover)
                {
                    Debug.Log($"[{gameObject.name}] SuppressCandidate {member.name}: SKIP - not in cover");
                    continue;
                }

                // Check personal threat level (don't pull someone under heavy fire)
                // Threshold of 40 allows suppression during normal combat (threat ~20-30)
                // but avoids assigning units that are being actively focused
                float personalThreat = member.PerceptionManager != null ? member.PerceptionManager.GetTotalThreat() : 0f;
                if (personalThreat > 40f)
                {
                    Debug.Log($"[{gameObject.name}] SuppressCandidate {member.name}: SKIP - threat too high ({personalThreat:F1})");
                    continue;
                }

                // Score: prefer low threat, good health, high accuracy
                float score = 10f; // Base score
                score -= personalThreat * 0.3f; // Light penalty for being under fire
                score += (member.Health?.HealthPercent ?? 1f) * 5f; // Health bonus
                score += (member.Character?.Accuracy ?? 10) * 0.5f; // Accuracy bonus

                // Small random factor for unpredictability
                score += Random.Range(-2f, 2f);

                candidates.Add((member, score));
            }

            if (candidates.Count == 0) return new List<UnitController>();

            // Weighted random selection instead of strict top-N
            return WeightedRandomSelect(candidates, maxCount);
        }

        /// <summary>
        /// Select units using weighted random (higher scores = higher chance, but not guaranteed).
        /// </summary>
        private List<UnitController> WeightedRandomSelect(List<(UnitController unit, float score)> candidates, int count)
        {
            var result = new List<UnitController>();
            var remaining = new List<(UnitController unit, float score)>(candidates);

            // Normalize scores to positive values for weighting
            float minScore = remaining.Min(c => c.score);
            if (minScore < 0)
            {
                remaining = remaining.Select(c => (c.unit, c.score - minScore + 1f)).ToList();
            }

            while (result.Count < count && remaining.Count > 0)
            {
                float totalWeight = remaining.Sum(c => c.score);
                float roll = Random.Range(0f, totalWeight);

                float cumulative = 0f;
                for (int i = 0; i < remaining.Count; i++)
                {
                    cumulative += remaining[i].score;
                    if (roll <= cumulative)
                    {
                        result.Add(remaining[i].unit);
                        remaining.RemoveAt(i);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a unit can suppress a target from their current position.
        /// </summary>
        private bool CanSuppressFrom(UnitController unit, Vector3 targetPos)
        {
            Vector3 firePos = unit.FirePosition;
            Vector2 direction = ((Vector2)(targetPos - firePos)).normalized;
            float distance = Vector2.Distance(firePos, targetPos);

            // Check if in weapon range
            if (distance > unit.WeaponRange) return false;

            // Raycast to see if blocked by our own cover
            RaycastHit2D[] hits = Physics2D.RaycastAll(firePos, direction, distance);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.distance < 0.1f) continue;

                var structure = hit.collider.GetComponent<Structure>();
                if (structure != null && structure.CoverType == CoverType.Full)
                {
                    // If full cover is in first 30% of distance, we're blocked by our own cover
                    if (hit.distance < distance * 0.3f)
                    {
                        return false;
                    }
                    // Otherwise it's target's cover - we can suppress it
                    return true;
                }
            }

            return true; // No blocking cover
        }

        /// <summary>
        /// Request suppression fire on a target. Called by a unit that needs cover.
        /// </summary>
        public void RequestSuppression(GameObject target, UnitController requester)
        {
            if (target == null) return;

            var candidates = FindSuppressCandidates(target, requester, 2);

            foreach (var unit in candidates)
            {
                Debug.Log($"[{gameObject.name}] Assigning {unit.name} to suppress {target.name}");
                // TODO: Need a way to command units into SuppressState
                // For now, this is just finding candidates - the actual command system comes next
            }
        }

        /// <summary>
        /// Check if a target is already being suppressed by this squad.
        /// </summary>
        public bool IsTargetBeingSuppressed(GameObject target)
        {
            if (target == null) return false;
            return activeSuppressions.ContainsKey(target);
        }

        /// <summary>
        /// Check if we're winning the engagement (for aggressive tactics).
        /// </summary>
        public bool IsWinningEngagement(SquadController enemySquad)
        {
            if (enemySquad == null) return true;

            int ourCount = LivingMemberCount;
            int theirCount = enemySquad.LivingMemberCount;

            // Up by 2+ members = winning
            if (ourCount >= theirCount + 2) return true;

            // Or up by 1 with better average health
            if (ourCount >= theirCount + 1 && SquadHealthPercent > enemySquad.SquadHealthPercent + 0.2f)
            {
                return true;
            }

            return false;
        }

        #endregion

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Use spawn point position, or this object's position as fallback
            Vector3 center = spawnPoint != null ? spawnPoint.position : transform.position;

            // Show spawn positions in editor
            var positions = GetGridPositions(center, unitCount);

            Gizmos.color = team == Team.Federation ? Color.blue : Color.red;

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
