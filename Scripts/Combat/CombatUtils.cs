using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;
using Starbelter.Pathfinding;
using Pathfinding;

namespace Starbelter.Combat
{
    /// <summary>
    /// Utility methods for combat calculations.
    /// </summary>
    public static class CombatUtils
    {
        /// <summary>
        /// Result of a line-of-sight check between attacker and target.
        /// </summary>
        public struct LineOfSightResult
        {
            public bool HasLineOfSight;
            public bool IsBlocked;
            public bool IsPartialCover;
            public CoverType CoverType;
            public Structure BlockingCover;
            public float Distance;
        }

        /// <summary>
        /// Check line of sight from attacker to target, detecting cover in between.
        /// Half cover only counts if near the target (they're using it for protection).
        /// </summary>
        public static LineOfSightResult CheckLineOfSight(Vector2 attackerPos, Vector2 targetPos, float coverProximityRadius = 1.5f)
        {
            var result = new LineOfSightResult
            {
                HasLineOfSight = true,
                IsBlocked = false,
                IsPartialCover = false,
                CoverType = CoverType.None,
                BlockingCover = null,
                Distance = Vector2.Distance(attackerPos, targetPos)
            };

            Vector2 direction = (targetPos - attackerPos).normalized;
            float distance = result.Distance;

            RaycastHit2D[] hits = Physics2D.RaycastAll(attackerPos, direction, distance);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.distance < 0.01f) continue;

                var structure = hit.collider.GetComponent<Structure>();
                if (structure == null) continue;

                float distFromAttacker = hit.distance;
                float distFromTarget = distance - distFromAttacker;

                if (structure.CoverType == CoverType.Half)
                {
                    bool nearAttacker = distFromAttacker < coverProximityRadius;
                    bool nearTarget = distFromTarget < coverProximityRadius;

                    if (nearAttacker)
                    {
                        continue; // Attacker peeks over their own cover
                    }
                    else if (nearTarget)
                    {
                        result.BlockingCover = structure;
                        result.CoverType = CoverType.Half;
                        result.IsPartialCover = true;
                        break;
                    }
                    // Middle of nowhere - ignore
                }
                else if (structure.CoverType == CoverType.Full)
                {
                    result.BlockingCover = structure;
                    result.CoverType = CoverType.Full;
                    result.IsBlocked = true;
                    result.HasLineOfSight = false;
                    break;
                }
            }

            return result;
        }

        private const float COVER_SEARCH_RADIUS = 12f;

        /// <summary>
        /// Helper class to track async path queries for position scoring.
        /// </summary>
        private class AsyncPositionQuery
        {
            public List<CoverResult> Positions;
            public Dictionary<Vector2, float> PathCosts;
            public int PendingCount;
            public System.Action<FightingPositionResult> Callback;

            // Cached scoring params
            public Vector2 UnitPos;
            public float WeaponRange;
            public Team UnitTeam;
            public Vector3? RallyPoint;
            public bool IsLeader;
            public CoverQuery CoverQuery;
            public GameObject ExcludeUnit;
            public int Tactics;
            public List<GameObject> KnownEnemies;
        }

        /// <summary>
        /// Async version - batches A* path queries and returns result via callback.
        /// Use this for accurate path cost scoring without blocking frames.
        /// </summary>
        public static void FindFightingPositionAsync(
            Vector2 unitPos,
            float weaponRange,
            CoverQuery coverQuery,
            GameObject excludeUnit,
            Team unitTeam,
            Vector3? rallyPoint,
            bool isLeader,
            int tactics,
            List<GameObject> knownEnemies,
            System.Action<FightingPositionResult> callback)
        {
            if (coverQuery == null)
            {
                callback?.Invoke(new FightingPositionResult { Found = false });
                return;
            }

            var positions = coverQuery.GetAllCoverPositions(unitPos, COVER_SEARCH_RADIUS, excludeUnit);

            if (positions.Count == 0)
            {
                callback?.Invoke(new FightingPositionResult { Found = false });
                return;
            }

            var query = new AsyncPositionQuery
            {
                Positions = positions,
                PathCosts = new Dictionary<Vector2, float>(),
                PendingCount = positions.Count,
                Callback = callback,
                UnitPos = unitPos,
                WeaponRange = weaponRange,
                UnitTeam = unitTeam,
                RallyPoint = rallyPoint,
                IsLeader = isLeader,
                CoverQuery = coverQuery,
                ExcludeUnit = excludeUnit,
                Tactics = tactics,
                KnownEnemies = knownEnemies
            };

            // Start all path queries async
            foreach (var coverPos in positions)
            {
                Vector2 dest = coverPos.WorldPosition;
                var path = ABPath.Construct(unitPos, dest, p => OnAsyncPathComplete(query, dest, p));
                AstarPath.StartPath(path);
            }
        }

        private static void OnAsyncPathComplete(AsyncPositionQuery query, Vector2 destination, Path path)
        {
            query.PathCosts[destination] = path.error ? -1f : path.GetTotalLength();
            query.PendingCount--;

            if (query.PendingCount <= 0)
            {
                // All paths calculated - score using the SAME positions we calculated paths for
                var result = FindFightingPosition(
                    query.UnitPos,
                    query.WeaponRange,
                    query.Positions,  // Pass pre-fetched positions to avoid mismatch
                    query.UnitTeam,
                    query.RallyPoint,
                    query.IsLeader,
                    query.Tactics,
                    query.KnownEnemies,
                    query.PathCosts);
                query.Callback?.Invoke(result);
            }
        }

        /// <summary>
        /// Find a fighting position - cover with LOS to at least one enemy.
        /// Checks cover against ALL visible enemies, not a single threat direction.
        /// </summary>
        /// <param name="pathCosts">Optional pre-computed A* path costs. If provided, uses these instead of Euclidean distance.</param>
        public static FightingPositionResult FindFightingPosition(
            Vector2 unitPos,
            float weaponRange,
            CoverQuery coverQuery,
            GameObject excludeUnit = null,
            Team unitTeam = Team.Neutral,
            Vector3? rallyPoint = null,
            bool isLeader = false,
            int tactics = 10,
            List<GameObject> knownEnemies = null,
            Dictionary<Vector2, float> pathCosts = null)
        {
            if (coverQuery == null)
            {
                return new FightingPositionResult { Found = false, Position = unitPos };
            }

            var coverPositions = coverQuery.GetAllCoverPositions(unitPos, COVER_SEARCH_RADIUS, excludeUnit);
            return FindFightingPosition(unitPos, weaponRange, coverPositions, unitTeam, rallyPoint, isLeader, tactics, knownEnemies, pathCosts);
        }

        /// <summary>
        /// Find a fighting position from pre-fetched cover positions.
        /// Used by async path scoring to ensure positions match path costs.
        /// </summary>
        public static FightingPositionResult FindFightingPosition(
            Vector2 unitPos,
            float weaponRange,
            List<CoverResult> coverPositions,
            Team unitTeam = Team.Neutral,
            Vector3? rallyPoint = null,
            bool isLeader = false,
            int tactics = 10,
            List<GameObject> knownEnemies = null,
            Dictionary<Vector2, float> pathCosts = null)
        {
            var result = new FightingPositionResult
            {
                Found = false,
                Position = unitPos,
                BestTarget = null,
                TargetCoverType = CoverType.None,
                OurCoverType = CoverType.None,
                Score = 0f
            };

            if (coverPositions == null || coverPositions.Count == 0) return result;

            var enemies = GetEnemyPositions(unitTeam);
            if (enemies.Count == 0) return result;

            float bestScore = float.MinValue;

            foreach (var coverPos in coverPositions)
            {
                Vector2 candidatePos = coverPos.WorldPosition;

                // Skip positions too close to enemies
                if (IsTooCloseToEnemies(candidatePos, enemies, 3f)) continue;

                // Find enemies we can shoot from this position and check cover against them
                GameObject bestTargetFromHere = null;
                CoverType bestTargetCover = CoverType.None;
                float targetScore = 0f;
                List<Vector2> shootableEnemies = new List<Vector2>();

                foreach (var enemyPos in enemies)
                {
                    float distToEnemy = Vector2.Distance(candidatePos, enemyPos);
                    if (distToEnemy > weaponRange) continue;

                    var los = CheckLineOfSight(candidatePos, enemyPos, 1.5f);

                    // Score this target
                    float thisTargetScore = 0f;
                    if (!los.IsBlocked)
                    {
                        thisTargetScore = 100f;
                        shootableEnemies.Add(enemyPos);
                    }
                    else if (los.CoverType == CoverType.Half)
                    {
                        thisTargetScore = 50f;
                        shootableEnemies.Add(enemyPos);
                    }
                    else
                    {
                        thisTargetScore = 10f;
                    }

                    thisTargetScore += (weaponRange - distToEnemy) / weaponRange * 20f;

                    if (thisTargetScore > targetScore)
                    {
                        targetScore = thisTargetScore;
                        bestTargetFromHere = FindEnemyAtPosition(enemyPos, unitTeam);
                        bestTargetCover = los.IsBlocked ? los.CoverType : CoverType.None;
                    }
                }

                // Skip if no shootable targets
                if (bestTargetFromHere == null) continue;

                // Score this position
                float positionScore = targetScore;

                // === COVER SCORING: Check protection FROM all enemies that can shoot us ===
                int fullCoverCount = 0;
                int halfCoverCount = 0;
                int exposedCount = 0;

                // Use all known enemies, checking if they can reach us
                if (knownEnemies != null && knownEnemies.Count > 0)
                {
                    foreach (var enemy in knownEnemies)
                    {
                        if (enemy == null) continue;
                        var targetable = enemy.GetComponent<ITargetable>();
                        if (targetable == null || targetable.IsDead) continue;

                        // Get enemy's weapon range from ITargetable
                        float enemyWeaponRange = targetable.WeaponRange;

                        // Apply tactics-based estimation uncertainty
                        // Guestimate = 5 - (Tactics / 5), minimum 0
                        // Low tactics = more uncertainty about enemy capabilities
                        int guestimate = Mathf.Max(0, 5 - (tactics / 5));
                        float estimatedRange = enemyWeaponRange;
                        if (guestimate > 0)
                        {
                            estimatedRange = enemyWeaponRange + Random.Range(-guestimate, guestimate + 1);
                        }

                        Vector3 enemyPos = enemy.transform.position;
                        float distToEnemy = Vector2.Distance(candidatePos, enemyPos);
                        if (distToEnemy > estimatedRange) continue; // Enemy can't reach us, skip

                        // Raycast FROM enemy TO our position - what cover blocks their shot?
                        var losFromEnemy = CheckLineOfSight(enemyPos, candidatePos);

                        if (losFromEnemy.IsBlocked)
                            fullCoverCount++;
                        else if (losFromEnemy.IsPartialCover)
                            halfCoverCount++;
                        else
                            exposedCount++;
                    }
                }

                // Determine overall cover type for display (worst case)
                CoverType ourCover = CoverType.None;
                int totalThreats = fullCoverCount + halfCoverCount + exposedCount;
                if (totalThreats > 0)
                {
                    if (exposedCount == 0 && halfCoverCount == 0)
                        ourCover = CoverType.Full;
                    else if (exposedCount == 0)
                        ourCover = CoverType.Half;
                }

                // Proportional cover bonus - reward positions that protect from MORE enemies
                // Full cover from enemy = +20, Half cover = +10, Exposed = -15
                float coverScore = (fullCoverCount * 20f) + (halfCoverCount * 10f) - (exposedCount * 15f);
                positionScore += coverScore;

                // Penalize high threat positions (don't walk into kill zones)
                // Threat 10 = dangerous (-100), 20 = deadly (-200). Must outweigh offensive bonuses.
                if (TileThreatMap.Instance != null)
                {
                    float threat = TileThreatMap.Instance.GetThreatAtWorld(candidatePos, unitTeam);
                    positionScore -= threat * 10f;
                }

                // CRITICAL: Penalize positions that move us CLOSER to enemies
                // Don't run towards enemies to get to "good" cover on the other side
                float currentMinEnemyDist = float.MaxValue;
                float candidateMinEnemyDist = float.MaxValue;
                foreach (var enemyPos in enemies)
                {
                    float distFromCurrent = Vector2.Distance(unitPos, enemyPos);
                    float distFromCandidate = Vector2.Distance(candidatePos, enemyPos);
                    if (distFromCurrent < currentMinEnemyDist) currentMinEnemyDist = distFromCurrent;
                    if (distFromCandidate < candidateMinEnemyDist) candidateMinEnemyDist = distFromCandidate;
                }

                // If this position is closer to enemies than we currently are, heavy penalty
                if (candidateMinEnemyDist < currentMinEnemyDist)
                {
                    float closerBy = currentMinEnemyDist - candidateMinEnemyDist;
                    positionScore -= closerBy * 20f;  // -20 per unit closer to enemies
                }

                // Prefer closer positions (use A* path cost if available, else Euclidean)
                float travelDist;
                if (pathCosts != null && pathCosts.TryGetValue(candidatePos, out float cost))
                {
                    if (cost < 0) continue;  // No valid path - skip position
                    travelDist = cost;
                }
                else
                {
                    travelDist = Vector2.Distance(unitPos, candidatePos);
                }
                positionScore -= travelDist * 5f;

                // Rally point scoring
                if (rallyPoint.HasValue)
                {
                    float distToRally = Vector3.Distance(candidatePos, rallyPoint.Value);
                    float idealRange = isLeader ? 5f : 8f;
                    float maxRange = isLeader ? 12f : 20f;
                    float maxBonus = isLeader ? 40f : 15f;
                    float overExtendPenalty = isLeader ? 3f : 0.5f;

                    if (distToRally <= idealRange)
                        positionScore += maxBonus;
                    else if (distToRally <= maxRange)
                        positionScore += maxBonus * (1f - ((distToRally - idealRange) / (maxRange - idealRange)));
                    else
                        positionScore -= (distToRally - maxRange) * overExtendPenalty;
                }

                if (positionScore > bestScore)
                {
                    bestScore = positionScore;
                    result.Found = true;
                    result.Position = candidatePos;
                    result.BestTarget = bestTargetFromHere;
                    result.TargetCoverType = bestTargetCover;
                    result.OurCoverType = ourCover;
                    result.Score = positionScore;
                }
            }

            // Debug: log what position was chosen and why
            if (result.Found)
            {
                float threat = TileThreatMap.Instance != null ? TileThreatMap.Instance.GetThreatAtWorld(result.Position, unitTeam) : 0f;
                float dist = Vector2.Distance(unitPos, result.Position);

                // Find closest enemy to show direction context
                float closestEnemyDist = float.MaxValue;
                Vector2 closestEnemyDir = Vector2.zero;
                foreach (var e in enemies)
                {
                    float d = Vector2.Distance(result.Position, e);
                    if (d < closestEnemyDist)
                    {
                        closestEnemyDist = d;
                        closestEnemyDir = (e - result.Position).normalized;
                    }
                }

                Debug.Log($"[FindFightingPosition] From {unitPos} chose {result.Position} (dist={dist:F1}) cover={result.OurCoverType} threat={threat:F1} score={result.Score:F1} enemies={enemies.Count}");
            }

            return result;
        }

        private static List<Vector2> GetEnemyPositions(Team myTeam)
        {
            var positions = new List<Vector2>();
            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == myTeam || targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;
                positions.Add(targetable.Transform.position);
            }

            return positions;
        }

        private static bool IsTooCloseToEnemies(Vector2 position, List<Vector2> enemies, float minDistance)
        {
            foreach (var enemy in enemies)
            {
                if (Vector2.Distance(position, enemy) < minDistance)
                    return true;
            }
            return false;
        }

        private static GameObject FindEnemyAtPosition(Vector2 position, Team myTeam)
        {
            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == myTeam || targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                if (Vector2.Distance(targetable.Transform.position, position) < 0.5f)
                    return targetable.Transform.gameObject;
            }
            return null;
        }
    }

    /// <summary>
    /// Result of FindFightingPosition search.
    /// </summary>
    public struct FightingPositionResult
    {
        public bool Found;
        public Vector2 Position;
        public GameObject BestTarget;
        public CoverType TargetCoverType;
        public CoverType OurCoverType;
        public float Score;
    }
}
