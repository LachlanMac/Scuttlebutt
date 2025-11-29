using UnityEngine;
using Starbelter.Core;
using Starbelter.Pathfinding;

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
            public bool HasLineOfSight;      // Can see target at all
            public bool IsBlocked;           // Full cover blocks the shot
            public bool IsPartialCover;      // Half cover in the way
            public CoverType CoverType;      // Type of cover protecting target
            public Structure BlockingCover;  // The structure providing cover (if any)
            public float Distance;           // Distance to target
        }

        /// <summary>
        /// Check line of sight from attacker to target, detecting cover in between.
        /// Half cover only counts if someone is using it (near attacker or near target).
        /// Half cover in the middle of nowhere is ignored.
        /// </summary>
        /// <param name="attackerPos">Position of the attacker</param>
        /// <param name="targetPos">Position of the target</param>
        /// <param name="coverProximityRadius">Half cover must be within this distance of attacker or target to count</param>
        /// <returns>LineOfSightResult with cover information</returns>
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

            // Raycast from attacker to target
            RaycastHit2D[] hits = Physics2D.RaycastAll(attackerPos, direction, distance);

            // Sort by distance (RaycastAll doesn't guarantee order)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                // Skip hits at distance 0 (inside collider at start)
                if (hit.distance < 0.01f) continue;

                var structure = hit.collider.GetComponent<Structure>();
                if (structure == null) continue;

                float distFromAttacker = hit.distance;
                float distFromTarget = distance - distFromAttacker;

                if (structure.CoverType == CoverType.Half)
                {
                    // Half cover only matters if someone is USING it:
                    // - Near attacker (they peek over it) - ignore it for shooting
                    // - Near target (they're protected by it) - counts as partial cover
                    // - In the middle of nowhere - ignore entirely

                    bool nearAttacker = distFromAttacker < coverProximityRadius;
                    bool nearTarget = distFromTarget < coverProximityRadius;

                    if (nearAttacker)
                    {
                        // Attacker's own cover - ignore (they peek over)
                        continue;
                    }
                    else if (nearTarget)
                    {
                        // Target's cover - counts as partial
                        result.BlockingCover = structure;
                        result.CoverType = CoverType.Half;
                        result.IsPartialCover = true;
                        break;
                    }
                    else
                    {
                        // Middle of nowhere - ignore
                        continue;
                    }
                }
                else if (structure.CoverType == CoverType.Full)
                {
                    // Full cover ALWAYS blocks
                    result.BlockingCover = structure;
                    result.CoverType = CoverType.Full;
                    result.IsBlocked = true;
                    result.HasLineOfSight = false;
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Calculate a target priority score. Higher = better target.
        /// </summary>
        /// <param name="attackerPos">Position of the attacker</param>
        /// <param name="targetPos">Position of the target</param>
        /// <param name="weaponRange">Maximum weapon range</param>
        /// <returns>Priority score (0 = don't engage, higher = better target)</returns>
        public static float CalculateTargetPriority(Vector2 attackerPos, Vector2 targetPos, float weaponRange)
        {
            var los = CheckLineOfSight(attackerPos, targetPos);

            // Out of range = no priority
            if (los.Distance > weaponRange) return 0f;

            // Full cover = very low priority (can't hit)
            if (los.IsBlocked) return 0.1f;

            // Base priority from distance (closer = higher priority)
            float distancePriority = 1f - (los.Distance / weaponRange);

            // Modify by cover
            float coverModifier = 1f;
            if (los.IsPartialCover)
            {
                coverModifier = 0.6f; // Half cover reduces priority
            }

            return distancePriority * coverModifier;
        }

        /// <summary>
        /// Check if a target is peeking (exposed from cover).
        /// </summary>
        public static bool IsTargetPeeking(GameObject target)
        {
            // Check if target has a UnitController and is in peeking state
            var unitController = target.GetComponent<AI.UnitController>();
            if (unitController != null)
            {
                return unitController.IsPeeking;
            }

            // Non-AI targets (like player) are always "exposed"
            return true;
        }

        /// <summary>
        /// Result of a flank position search.
        /// </summary>
        public struct FlankResult
        {
            public bool Found;
            public Vector2 Position;
            public float Score;           // Higher = better flank position
            public CoverType CoverAtPosition;
        }

        /// <summary>
        /// Find a position to flank a target - has LOS to target AND ideally cover.
        /// Positions must be >3 units from ALL enemies. If no safe cover exists,
        /// may return an open position at weapon range.
        /// </summary>
        /// <param name="attackerPos">Current attacker position</param>
        /// <param name="targetPos">Target to flank</param>
        /// <param name="weaponRange">Weapon range (used for open flanking)</param>
        /// <param name="coverQuery">CoverQuery instance for finding cover</param>
        /// <param name="excludeUnit">Unit to exclude from occupancy check (the flanker)</param>
        /// <param name="attackerTeam">Team of the attacker (to find enemies)</param>
        /// <returns>FlankResult with best position found</returns>
        public static FlankResult FindFlankPosition(Vector2 attackerPos, Vector2 targetPos, float weaponRange, CoverQuery coverQuery, GameObject excludeUnit = null, Team attackerTeam = Team.Neutral)
        {
            var result = new FlankResult
            {
                Found = false,
                Position = attackerPos,
                Score = 0f,
                CoverAtPosition = CoverType.None
            };

            if (coverQuery == null) return result;

            const float MIN_DISTANCE_FROM_ANY_ENEMY = 3f;

            // Get all enemies for proximity checks
            var enemies = GetEnemyPositions(attackerTeam);

            // First, try to find a covered flank position
            var coverPositions = coverQuery.GetAllCoverPositions(attackerPos, weaponRange, excludeUnit);

            float bestCoveredScore = 0f;
            FlankResult bestCoveredResult = result;

            foreach (var coverPos in coverPositions)
            {
                Vector2 candidatePos = coverPos.WorldPosition;

                // Skip if too close to current position
                if (Vector2.Distance(candidatePos, attackerPos) < 2f) continue;

                // Skip if too close to ANY enemy
                if (IsTooCloseToEnemies(candidatePos, enemies, MIN_DISTANCE_FROM_ANY_ENEMY)) continue;

                // Must have LOS to target
                var losToTarget = CheckLineOfSight(candidatePos, targetPos, 1.5f);
                if (losToTarget.IsBlocked) continue;

                // Must have cover
                bool hasCover = coverPos.CoverSources != null && coverPos.CoverSources.Count > 0;
                if (!hasCover) continue;

                // Score this position
                float score = ScoreFlankPosition(attackerPos, candidatePos, targetPos, coverPos, weaponRange, enemies);

                if (score > bestCoveredScore)
                {
                    bestCoveredScore = score;
                    bestCoveredResult.Found = true;
                    bestCoveredResult.Position = candidatePos;
                    bestCoveredResult.Score = score;
                    bestCoveredResult.CoverAtPosition = GetBestCoverType(coverPos);
                }
            }

            // If we found a covered position, use it
            if (bestCoveredResult.Found)
            {
                return bestCoveredResult;
            }

            // No covered flank found - try open positions at weapon range
            // This is riskier but may be the only option
            float bestOpenScore = 0f;
            FlankResult bestOpenResult = result;

            // Sample positions in a circle around the target at weapon range
            int sampleCount = 16;
            float optimalRange = weaponRange * 0.7f; // Slightly inside max range

            for (int i = 0; i < sampleCount; i++)
            {
                float angle = (i / (float)sampleCount) * 360f * Mathf.Deg2Rad;
                Vector2 candidatePos = targetPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * optimalRange;

                // Skip if too close to current position
                if (Vector2.Distance(candidatePos, attackerPos) < 2f) continue;

                // Skip if too close to ANY enemy
                if (IsTooCloseToEnemies(candidatePos, enemies, MIN_DISTANCE_FROM_ANY_ENEMY)) continue;

                // Must have LOS to target
                var losToTarget = CheckLineOfSight(candidatePos, targetPos, 1.5f);
                if (losToTarget.IsBlocked) continue;

                // Score open position (lower base score since no cover)
                float score = 0f;

                // Prefer closer to our current position
                float travelDist = Vector2.Distance(attackerPos, candidatePos);
                score += (weaponRange - travelDist) / weaponRange * 20f;

                // Prefer angle change (actual flanking)
                Vector2 currentDir = (targetPos - attackerPos).normalized;
                Vector2 newDir = (targetPos - candidatePos).normalized;
                float angleChange = Vector2.Angle(currentDir, newDir);
                score += (angleChange / 180f) * 15f;

                // Penalize based on exposure (how many enemies can see this spot)
                float exposurePenalty = CalculateExposure(candidatePos, enemies);
                score -= exposurePenalty * 10f;

                if (score > bestOpenScore)
                {
                    bestOpenScore = score;
                    bestOpenResult.Found = true;
                    bestOpenResult.Position = candidatePos;
                    bestOpenResult.Score = score;
                    bestOpenResult.CoverAtPosition = CoverType.None;
                }
            }

            // Only use open position if score is positive (not too exposed)
            if (bestOpenResult.Found && bestOpenResult.Score > 0f)
            {
                return bestOpenResult;
            }

            return result; // No valid flank position found
        }

        private static float ScoreFlankPosition(Vector2 attackerPos, Vector2 candidatePos, Vector2 targetPos,
            CoverResult coverPos, float weaponRange, System.Collections.Generic.List<Vector2> enemies)
        {
            float score = 0f;

            // Cover quality
            CoverType bestCoverType = GetBestCoverType(coverPos);
            if (bestCoverType == CoverType.Full)
                score += 50f;
            else if (bestCoverType == CoverType.Half)
                score += 25f;

            // Prefer closer positions (less travel)
            float travelDist = Vector2.Distance(attackerPos, candidatePos);
            score += (weaponRange - travelDist) / weaponRange * 30f;

            // Prefer angle change (actual flanking)
            Vector2 currentDir = (targetPos - attackerPos).normalized;
            Vector2 newDir = (targetPos - candidatePos).normalized;
            float angleChange = Vector2.Angle(currentDir, newDir);
            score += (angleChange / 180f) * 20f;

            // Penalize exposure
            float exposurePenalty = CalculateExposure(candidatePos, enemies);
            score -= exposurePenalty * 5f;

            return score;
        }

        private static CoverType GetBestCoverType(CoverResult coverPos)
        {
            CoverType bestCoverType = CoverType.None;
            if (coverPos.CoverSources != null)
            {
                foreach (var source in coverPos.CoverSources)
                {
                    if (source.Type == CoverType.Full)
                        bestCoverType = CoverType.Full;
                    else if (source.Type == CoverType.Half && bestCoverType != CoverType.Full)
                        bestCoverType = CoverType.Half;
                }
            }
            return bestCoverType;
        }

        private static System.Collections.Generic.List<Vector2> GetEnemyPositions(Team myTeam)
        {
            var positions = new System.Collections.Generic.List<Vector2>();

            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == myTeam) continue;
                if (targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                positions.Add(targetable.Transform.position);
            }

            return positions;
        }

        private static bool IsTooCloseToEnemies(Vector2 position, System.Collections.Generic.List<Vector2> enemies, float minDistance)
        {
            foreach (var enemy in enemies)
            {
                if (Vector2.Distance(position, enemy) < minDistance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Calculate how exposed a position is (0-1, higher = more exposed).
        /// Based on how many enemies have LOS to this position.
        /// </summary>
        private static float CalculateExposure(Vector2 position, System.Collections.Generic.List<Vector2> enemies)
        {
            if (enemies.Count == 0) return 0f;

            int exposedCount = 0;
            foreach (var enemy in enemies)
            {
                var los = CheckLineOfSight(enemy, position);
                if (!los.IsBlocked)
                    exposedCount++;
            }

            return (float)exposedCount / enemies.Count;
        }

        /// <summary>
        /// Determine if it's safe to attempt a flank based on threat levels.
        /// </summary>
        /// <param name="threatManager">Unit's threat manager</param>
        /// <param name="bravery">Unit's bravery stat (1-20)</param>
        /// <returns>True if flanking is advisable</returns>
        public static bool ShouldAttemptFlank(ThreatManager threatManager, int bravery = 10)
        {
            if (threatManager == null) return true; // No threat info, go for it

            // Get active threats
            var threats = threatManager.GetActiveThreats(0.5f);

            // Multiple active threat directions = too dangerous
            if (threats.Count > 1) return false;

            // High total threat = too dangerous (unless very brave)
            float totalThreat = threatManager.GetTotalThreat();
            float braveryThreshold = 5f + (bravery * 0.5f); // 5.5 to 15 based on bravery

            if (totalThreat > braveryThreshold) return false;

            return true;
        }

        /// <summary>
        /// Find a fighting position - a spot with cover from threats where we can shoot at enemies.
        /// Less aggressive than flanking - doesn't require bypassing enemy cover.
        /// </summary>
        /// <param name="unitPos">Current unit position</param>
        /// <param name="threatDirection">Direction threats are coming from</param>
        /// <param name="weaponRange">Unit's weapon range</param>
        /// <param name="coverQuery">Cover query instance</param>
        /// <param name="excludeUnit">Unit to exclude from occupancy checks</param>
        /// <param name="unitTeam">Unit's team</param>
        /// <returns>FightingPositionResult with position and best target info</returns>
        public static FightingPositionResult FindFightingPosition(
            Vector2 unitPos,
            Vector2 threatDirection,
            float weaponRange,
            Pathfinding.CoverQuery coverQuery,
            GameObject excludeUnit = null,
            Team unitTeam = Team.Neutral)
        {
            var result = new FightingPositionResult
            {
                Found = false,
                Position = unitPos,
                BestTarget = null,
                TargetCoverType = CoverType.None,
                Score = 0f
            };

            if (coverQuery == null) return result;

            // Get all enemies
            var enemies = GetEnemyPositions(unitTeam);
            if (enemies.Count == 0) return result;

            // Get all cover positions within range
            var coverPositions = coverQuery.GetAllCoverPositions(unitPos, weaponRange * 0.8f, excludeUnit);

            float bestScore = float.MinValue;

            foreach (var coverPos in coverPositions)
            {
                Vector2 candidatePos = coverPos.WorldPosition;

                // Skip positions too close to current position (not worth moving)
                if (Vector2.Distance(candidatePos, unitPos) < 1.5f) continue;

                // Skip positions too close to enemies
                if (IsTooCloseToEnemies(candidatePos, enemies, 3f)) continue;

                // Check if this position has cover facing the threat direction
                bool hasCoverFromThreat = false;
                CoverType coverType = CoverType.None;

                if (coverPos.CoverSources != null)
                {
                    foreach (var source in coverPos.CoverSources)
                    {
                        // Cover direction should align with threat direction
                        float alignment = Vector2.Dot(source.DirectionToCover, threatDirection);
                        if (alignment > 0.3f)
                        {
                            hasCoverFromThreat = true;
                            if (source.Type == CoverType.Full)
                                coverType = CoverType.Full;
                            else if (source.Type == CoverType.Half && coverType != CoverType.Full)
                                coverType = CoverType.Half;
                        }
                    }
                }

                if (!hasCoverFromThreat) continue;

                // Find best target we can shoot from this position
                GameObject bestTargetFromHere = null;
                CoverType bestTargetCover = CoverType.None;
                float targetScore = 0f;

                foreach (var enemyPos in enemies)
                {
                    float distToEnemy = Vector2.Distance(candidatePos, enemyPos);
                    if (distToEnemy > weaponRange) continue;

                    // Check LOS - we want to be able to shoot, but enemy may have cover
                    var los = CheckLineOfSight(candidatePos, enemyPos, 1.5f);

                    // Score this target - prefer exposed or half-cover targets
                    float thisTargetScore = 0f;

                    if (!los.IsBlocked)
                    {
                        // Clear shot!
                        thisTargetScore = 100f;
                    }
                    else if (los.CoverType == CoverType.Half)
                    {
                        // Half cover - still a decent shot
                        thisTargetScore = 50f;
                    }
                    else
                    {
                        // Full cover - can still suppress but not ideal
                        thisTargetScore = 10f;
                    }

                    // Prefer closer targets
                    thisTargetScore += (weaponRange - distToEnemy) / weaponRange * 20f;

                    if (thisTargetScore > targetScore)
                    {
                        targetScore = thisTargetScore;
                        bestTargetFromHere = FindEnemyAtPosition(enemyPos, unitTeam);
                        bestTargetCover = los.IsBlocked ? los.CoverType : CoverType.None;
                    }
                }

                // Skip positions with no shootable targets
                if (bestTargetFromHere == null) continue;

                // Score this position
                float positionScore = targetScore;

                // Bonus for our cover quality
                if (coverType == CoverType.Full)
                    positionScore += 40f;
                else if (coverType == CoverType.Half)
                    positionScore += 20f;

                // Prefer closer positions (less travel time)
                float travelDist = Vector2.Distance(unitPos, candidatePos);
                positionScore -= travelDist * 5f;

                if (positionScore > bestScore)
                {
                    bestScore = positionScore;
                    result.Found = true;
                    result.Position = candidatePos;
                    result.BestTarget = bestTargetFromHere;
                    result.TargetCoverType = bestTargetCover;
                    result.Score = positionScore;
                    result.OurCoverType = coverType;
                }
            }

            return result;
        }

        /// <summary>
        /// Find the enemy GameObject at a specific position.
        /// </summary>
        private static GameObject FindEnemyAtPosition(Vector2 position, Team myTeam)
        {
            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == myTeam) continue;
                if (targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                if (Vector2.Distance(targetable.Transform.position, position) < 0.5f)
                {
                    return targetable.Transform.gameObject;
                }
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
        public CoverType TargetCoverType;  // Cover the target has from our position
        public CoverType OurCoverType;     // Cover we have at this position
        public float Score;
    }
}
