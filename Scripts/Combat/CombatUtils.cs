using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Starbelter.Core;
using Starbelter.Pathfinding;

namespace Starbelter.Combat
{
    /// <summary>
    /// Utility methods for combat calculations.
    /// </summary>
    public static class CombatUtils
    {
        #region Threat Threshold Constants

        /// <summary>
        /// Base threat threshold for fleeing combat (used when completely exposed).
        /// Formula: BASE + (bravery * BRAVERY_MULTIPLIER)
        /// </summary>
        public const float FLEE_THREAT_BASE = 20f;
        public const float FLEE_THREAT_BRAVERY_MULT = 1f;

        /// <summary>
        /// Base threat threshold for aborting a reposition (higher - committed to move).
        /// Formula: BASE + (bravery * BRAVERY_MULTIPLIER)
        /// </summary>
        public const float REPOSITION_ABORT_THREAT_BASE = 30f;
        public const float REPOSITION_ABORT_BRAVERY_MULT = 1f;

        /// <summary>
        /// Base threat threshold for aborting a flank (lower - flanking is risky).
        /// Formula: BASE + (bravery * BRAVERY_MULTIPLIER)
        /// </summary>
        public const float FLANK_ABORT_THREAT_BASE = 3f;
        public const float FLANK_ABORT_BRAVERY_MULT = 0.3f;

        /// <summary>
        /// Base threat threshold for deciding whether to attempt a flank.
        /// Formula: BASE + (bravery * BRAVERY_MULTIPLIER)
        /// </summary>
        public const float FLANK_ATTEMPT_THREAT_BASE = 5f;
        public const float FLANK_ATTEMPT_BRAVERY_MULT = 0.5f;

        /// <summary>
        /// Threat threshold for exiting suppression state.
        /// </summary>
        public const float SUPPRESSION_ABORT_THREAT = 30f;

        /// <summary>
        /// Calculate threat threshold based on bravery and base values.
        /// </summary>
        public static float CalculateThreatThreshold(float baseThreshold, float braveryMultiplier, int bravery)
        {
            return baseThreshold + (bravery * braveryMultiplier);
        }

        #endregion

        #region Projectile Shooting

        /// <summary>
        /// Parameters for shooting a projectile.
        /// </summary>
        public struct ShootParams
        {
            public Vector3 FirePosition;
            public Vector2 TargetPosition;
            public float SpreadRadians;
            public Team Team;
            public GameObject SourceUnit;
            public GameObject ProjectilePrefab;
        }

        /// <summary>
        /// Fire a projectile from fire position toward target with specified spread.
        /// Handles direction calculation, spread, instantiation, and firing.
        /// </summary>
        /// <returns>The fired projectile, or null if spawn failed</returns>
        public static Projectile ShootProjectile(ShootParams shootParams)
        {
            if (shootParams.ProjectilePrefab == null) return null;

            Vector2 baseDirection = (shootParams.TargetPosition - (Vector2)shootParams.FirePosition).normalized;

            // Apply spread
            float angle = Mathf.Atan2(baseDirection.y, baseDirection.x);
            angle += Random.Range(-shootParams.SpreadRadians, shootParams.SpreadRadians);
            Vector2 finalDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            // Spawn projectile
            GameObject projectileObj = Object.Instantiate(
                shootParams.ProjectilePrefab,
                shootParams.FirePosition,
                Quaternion.identity
            );

            var projectile = projectileObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Fire(finalDirection, shootParams.Team, shootParams.SourceUnit);
                return projectile;
            }
            else
            {
                Object.Destroy(projectileObj);
                return null;
            }
        }

        /// <summary>
        /// Calculate spread angle in radians based on accuracy stat.
        /// Base spread of ~5 degrees, accuracy reduces it.
        /// </summary>
        /// <param name="accuracy">Accuracy stat (1-20)</param>
        /// <returns>Spread in radians</returns>
        public static float CalculateAccuracySpread(int accuracy)
        {
            float baseSpread = 5f * Mathf.Deg2Rad;
            float accuracyMult = 1f - Character.StatToMultiplier(accuracy);
            return baseSpread * (0.2f + accuracyMult * 1.5f); // 0.2x to 1.7x spread
        }

        #endregion

        #region Target Finding Utilities

        /// <summary>
        /// Scan for enemies and register them as threats with the threat manager.
        /// Does not return anything - just registers visible enemies.
        /// </summary>
        public static void ScanAndRegisterThreats(
            Vector2 unitPos,
            float weaponRange,
            Team myTeam,
            Transform excludeTransform,
            ThreatManager threatManager)
        {
            if (threatManager == null || myTeam == Team.Neutral) return;

            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            foreach (var target in allTargets)
            {
                if (excludeTransform != null && target.Transform == excludeTransform) continue;
                if (target.Team == myTeam) continue;
                if (target.Team == Team.Neutral) continue;
                if (target.IsDead) continue;

                float distance = Vector2.Distance(unitPos, target.Transform.position);
                if (distance <= weaponRange)
                {
                    threatManager.RegisterVisibleEnemy(target.Transform.position, 1f);
                }
            }
        }

        /// <summary>
        /// Check if a target GameObject is dead or invalid.
        /// </summary>
        public static bool IsTargetDead(GameObject target)
        {
            if (target == null) return true;
            if (!target.activeInHierarchy) return true;
            var targetable = target.GetComponent<ITargetable>();
            return targetable != null && targetable.IsDead;
        }

        /// <summary>
        /// Convert a threat direction to a world position for cover calculations.
        /// </summary>
        public static Vector3 ThreatDirectionToWorldPos(Vector3 unitPos, Vector2 threatDirection, float distance = 10f)
        {
            return unitPos + new Vector3(threatDirection.x, threatDirection.y, 0) * distance;
        }

        /// <summary>
        /// Find the best enemy target based on priority scoring.
        /// </summary>
        /// <param name="attackerPos">Position of the attacker</param>
        /// <param name="weaponRange">Maximum weapon range</param>
        /// <param name="myTeam">Attacker's team</param>
        /// <param name="excludeTransform">Transform to exclude (usually self)</param>
        /// <param name="threatManager">Optional threat manager to register visible enemies</param>
        /// <returns>Best target GameObject, or null if none found</returns>
        public static GameObject FindBestTarget(
            Vector2 attackerPos,
            float weaponRange,
            Team myTeam,
            Transform excludeTransform = null,
            ThreatManager threatManager = null)
        {
            if (myTeam == Team.Neutral) return null;

            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            float bestPriority = 0f;
            GameObject bestTarget = null;

            foreach (var target in allTargets)
            {
                if (excludeTransform != null && target.Transform == excludeTransform) continue;
                if (target.Team == myTeam) continue;
                if (target.Team == Team.Neutral) continue;
                if (target.IsDead) continue;

                float distance = Vector2.Distance(attackerPos, target.Transform.position);

                // Register visible enemies within weapon range as threats
                if (distance <= weaponRange && threatManager != null)
                {
                    threatManager.RegisterVisibleEnemy(target.Transform.position, 1f);
                }

                float priority = CalculateTargetPriority(attackerPos, target.Transform.position, weaponRange);

                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestTarget = target.Transform.gameObject;
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// Find the best enemy target and return both target and priority.
        /// </summary>
        public static (GameObject target, float priority) FindBestTargetWithPriority(
            Vector2 attackerPos,
            float weaponRange,
            Team myTeam,
            Transform excludeTransform = null,
            ThreatManager threatManager = null)
        {
            if (myTeam == Team.Neutral) return (null, 0f);

            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            float bestPriority = 0f;
            GameObject bestTarget = null;

            foreach (var target in allTargets)
            {
                if (excludeTransform != null && target.Transform == excludeTransform) continue;
                if (target.Team == myTeam) continue;
                if (target.Team == Team.Neutral) continue;
                if (target.IsDead) continue;

                float distance = Vector2.Distance(attackerPos, target.Transform.position);

                if (distance <= weaponRange && threatManager != null)
                {
                    threatManager.RegisterVisibleEnemy(target.Transform.position, 1f);
                }

                float priority = CalculateTargetPriority(attackerPos, target.Transform.position, weaponRange);

                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestTarget = target.Transform.gameObject;
                }
            }

            return (bestTarget, bestPriority);
        }

        /// <summary>
        /// Find an exposed enemy target (not in full cover).
        /// </summary>
        /// <param name="attackerPos">Position of the attacker (use FirePosition for accuracy)</param>
        /// <param name="weaponRange">Maximum weapon range</param>
        /// <param name="myTeam">Attacker's team</param>
        /// <param name="excludeTransform">Transform to exclude (usually self)</param>
        /// <param name="excludeTarget">Additional target to exclude (e.g., current suppress target)</param>
        /// <returns>Exposed target GameObject, or null if none found</returns>
        public static GameObject FindExposedTarget(
            Vector2 attackerPos,
            float weaponRange,
            Team myTeam,
            Transform excludeTransform = null,
            GameObject excludeTarget = null)
        {
            if (myTeam == Team.Neutral) return null;

            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            foreach (var target in allTargets)
            {
                if (excludeTransform != null && target.Transform == excludeTransform) continue;
                if (target.Team == myTeam) continue;
                if (target.Team == Team.Neutral) continue;
                if (target.IsDead) continue;
                if (excludeTarget != null && target.Transform.gameObject == excludeTarget) continue;

                float dist = Vector2.Distance(attackerPos, target.Transform.position);
                if (dist > weaponRange) continue;

                var los = CheckLineOfSight(attackerPos, target.Transform.position);
                if (!los.IsBlocked)
                {
                    return target.Transform.gameObject;
                }
            }

            return null;
        }

        #endregion

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
            float braveryThreshold = CalculateThreatThreshold(
                FLANK_ATTEMPT_THREAT_BASE, FLANK_ATTEMPT_BRAVERY_MULT, bravery);

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
            Team unitTeam = Team.Neutral,
            Vector3? rallyPoint = null,
            bool isLeader = false)
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

                // Rally point scoring - keep units anchored, especially leaders
                if (rallyPoint.HasValue)
                {
                    float distToRally = Vector3.Distance(candidatePos, rallyPoint.Value);
                    float idealRange = isLeader ? 5f : 8f;
                    float maxRange = isLeader ? 12f : 20f;
                    float maxBonus = isLeader ? 40f : 15f;
                    float overExtendPenalty = isLeader ? 3f : 0.5f;

                    if (distToRally <= idealRange)
                    {
                        positionScore += maxBonus;
                    }
                    else if (distToRally <= maxRange)
                    {
                        float factor = 1f - ((distToRally - idealRange) / (maxRange - idealRange));
                        positionScore += maxBonus * factor;
                    }
                    else
                    {
                        float overExtension = distToRally - maxRange;
                        positionScore -= overExtension * overExtendPenalty;
                    }
                }

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
