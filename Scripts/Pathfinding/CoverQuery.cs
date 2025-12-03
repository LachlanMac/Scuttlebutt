using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.AI;

namespace Starbelter.Pathfinding
{
    /// <summary>
    /// Utility class for querying cover positions relative to threats.
    /// Used by AI to find suitable cover positions.
    /// </summary>
    public class CoverQuery : MonoBehaviour
    {
        public static CoverQuery Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("Maximum distance to search for cover")]
        [SerializeField] private float maxSearchRadius = 15f;

        [Tooltip("Layer mask for cover raycast checks")]
        [SerializeField] private LayerMask coverRaycastMask;

        private CoverBaker coverBaker;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            coverBaker = CoverBaker.Instance;
            if (coverBaker == null)
            {
                Debug.LogError("[CoverQuery] CoverBaker instance not found!");
            }
        }

        /// <summary>
        /// Finds the best cover position relative to a threat.
        /// </summary>
        /// <param name="unitPosition">Current position of the unit seeking cover</param>
        /// <param name="threatPosition">Position of the threat to hide from</param>
        /// <param name="maxDistance">Maximum search distance (uses default if <= 0)</param>
        /// <returns>Best cover position, or null if none found</returns>
        public CoverResult? FindBestCover(Vector3 unitPosition, Vector3 threatPosition, float maxDistance = -1f)
        {
            return FindBestCover(unitPosition, threatPosition, CoverSearchParams.Default, maxDistance);
        }

        /// <summary>
        /// Finds the best cover position with tactical parameters (weapon range, aggression).
        /// </summary>
        public CoverResult? FindBestCover(Vector3 unitPosition, Vector3 threatPosition, CoverSearchParams searchParams, float maxDistance = -1f, GameObject excludeUnit = null)
        {
            if (coverBaker == null) return null;

            if (maxDistance <= 0) maxDistance = maxSearchRadius;

            var candidates = FindCoverCandidates(unitPosition, threatPosition, maxDistance, excludeUnit);

            string unitName = excludeUnit != null ? excludeUnit.name : "Unknown";

            if (candidates.Count == 0)
            {
                Debug.Log($"[CoverQuery] {unitName}: No cover candidates found within {maxDistance:F1} tiles");
                return null;
            }

            // Score each candidate with tactical considerations
            float bestScore = float.MinValue;
            CoverResult? best = null;
            string bestReason = "";

            // Track top candidates for logging
            var topCandidates = new List<(CoverResult cover, float score, string breakdown)>();

            foreach (var candidate in candidates)
            {
                var (score, breakdown) = ScoreCoverTacticallyWithBreakdown(candidate, unitPosition, threatPosition, searchParams);
                topCandidates.Add((candidate, score, breakdown));

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    bestReason = breakdown;
                }
            }

            // Log the decision
            if (best.HasValue)
            {
                Vector2 dirToThreat = ((Vector2)(threatPosition - unitPosition)).normalized;
                Vector2 dirToCover = ((Vector2)(best.Value.WorldPosition - unitPosition)).normalized;
                float angleFromThreat = Vector2.SignedAngle(dirToThreat, dirToCover);

                // Determine cover type at chosen position
                string coverTypeStr = "None";
                Vector2 threatDir = ((Vector2)(threatPosition - best.Value.WorldPosition)).normalized;
                foreach (var source in best.Value.CoverSources)
                {
                    float alignment = Vector2.Dot(source.DirectionToCover, threatDir);
                    float alignThreshold = (source.Type == CoverType.Full) ? 0.7f : 0.3f;
                    if (alignment > alignThreshold)
                    {
                        coverTypeStr = source.Type.ToString();
                        break;
                    }
                }

                // Log when choosing FULL cover - helps debug why units prefer it
                if (coverTypeStr == "Full")
                {
                    // Find best half cover alternative for comparison
                    float bestHalfScore = float.MinValue;
                    CoverResult? bestHalf = null;
                    string bestHalfBreakdown = "";

                    foreach (var (cover, score, breakdown) in topCandidates)
                    {
                        // Check if this candidate has aligned half cover
                        Vector2 candThreatDir = ((Vector2)(threatPosition - cover.WorldPosition)).normalized;
                        bool hasAlignedHalf = false;
                        foreach (var src in cover.CoverSources)
                        {
                            if (src.Type == CoverType.Half && Vector2.Dot(src.DirectionToCover, candThreatDir) > 0.3f)
                            {
                                hasAlignedHalf = true;
                                break;
                            }
                        }

                        if (hasAlignedHalf && score > bestHalfScore)
                        {
                            bestHalfScore = score;
                            bestHalf = cover;
                            bestHalfBreakdown = breakdown;
                        }
                    }

                    string halfComparison = bestHalf.HasValue
                        ? $"Best half cover: {bestHalf.Value.TilePosition} score={bestHalfScore:F1} ({bestHalfBreakdown})"
                        : "No half cover alternatives found";

                    string modeStr = searchParams.Mode == CoverMode.Fighting ? "FIGHTING" : "DEFENSIVE";
                    Debug.Log($"[CoverQuery] {unitName}: Chose FULL COVER at {best.Value.TilePosition} score={bestScore:F1} (mode={modeStr})\n" +
                              $"  Breakdown: {bestReason}\n" +
                              $"  {halfComparison}");
                }

                // Store debug info for gizmo drawing
                lastCoverSearchUnit = excludeUnit;
                lastChosenCover = best.Value.WorldPosition;
                lastThreatPosition = threatPosition;
                lastSearchTime = Time.time;
            }

            return best;
        }

        /// <summary>
        /// Finds the best cover position and returns it with its tactical score.
        /// Use this when you need to compare cover quality (e.g., should I move or stay?).
        /// </summary>
        public (CoverResult? cover, float score) FindBestCoverWithScore(Vector3 unitPosition, Vector3 threatPosition, CoverSearchParams searchParams, float maxDistance = -1f, GameObject excludeUnit = null)
        {
            if (coverBaker == null) return (null, float.MinValue);

            if (maxDistance <= 0) maxDistance = maxSearchRadius;

            var candidates = FindCoverCandidates(unitPosition, threatPosition, maxDistance, excludeUnit);

            if (candidates.Count == 0)
            {
                return (null, float.MinValue);
            }

            float bestScore = float.MinValue;
            CoverResult? best = null;

            foreach (var candidate in candidates)
            {
                var (score, _) = ScoreCoverTacticallyWithBreakdown(candidate, unitPosition, threatPosition, searchParams);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return (best, bestScore);
        }

        /// <summary>
        /// Score a specific position for cover quality.
        /// Used to evaluate current position vs potential new positions.
        /// </summary>
        public float ScorePositionForCover(Vector3 position, Vector3 threatPosition, CoverSearchParams searchParams)
        {
            if (coverBaker == null) return float.MinValue;

            var tilePos = coverBaker.WorldToTile(position);
            var coverSources = coverBaker.GetCoverAt(tilePos);

            // If no cover at exact tile, check adjacent tiles (unit might be between tiles)
            if (coverSources.Count == 0)
            {
                // Check tiles within 0.6 units (slightly more than half a tile)
                float searchRadius = 0.6f;
                var nearbyTiles = new Vector3Int[]
                {
                    tilePos + new Vector3Int(1, 0, 0),
                    tilePos + new Vector3Int(-1, 0, 0),
                    tilePos + new Vector3Int(0, 1, 0),
                    tilePos + new Vector3Int(0, -1, 0)
                };

                foreach (var nearbyTile in nearbyTiles)
                {
                    var nearbyWorld = coverBaker.TileToWorld(nearbyTile);
                    if (Vector3.Distance(position, nearbyWorld) < searchRadius)
                    {
                        var nearbySources = coverBaker.GetCoverAt(nearbyTile);
                        if (nearbySources.Count > 0)
                        {
                            tilePos = nearbyTile;
                            coverSources = nearbySources;
                            break;
                        }
                    }
                }
            }

            if (coverSources.Count == 0)
            {
                Debug.Log($"[CoverQuery] ScorePositionForCover: No cover found at {position} (tile {tilePos})");
                return float.MinValue; // No cover at this position
            }

            // Create a CoverResult for scoring
            var coverResult = new CoverResult
            {
                TilePosition = tilePos,
                WorldPosition = coverBaker.TileToWorld(tilePos),
                DistanceFromUnit = 0f, // We're already here
                Score = EvaluateCoverPosition(position, threatPosition, coverSources),
                CoverSources = coverSources
            };

            if (coverResult.Score <= 0)
            {
                // Cover doesn't align with current highest threat, but we're still AT cover
                // Return a low but valid score - don't return MinValue which causes panic-moving
                // The unit has SOME cover, just not facing the "right" direction
                // This prevents constant repositioning when threat direction changes
                Debug.Log($"[CoverQuery] ScorePositionForCover: Cover at {tilePos} doesn't protect from current threat (alignment={coverResult.Score:F2}), using base score");
                return 20f; // Low score but not MinValue - unit will move if better cover exists nearby
            }

            var (score, _) = ScoreCoverTacticallyWithBreakdown(coverResult, position, threatPosition, searchParams);
            return score;
        }

        // Debug visualization data
        private static GameObject lastCoverSearchUnit;
        private static Vector3 lastChosenCover;
        private static Vector3 lastThreatPosition;
        private static float lastSearchTime;

        /// <summary>
        /// Get the last cover search result for debug visualization.
        /// </summary>
        public static (GameObject unit, Vector3 cover, Vector3 threat, float time) GetLastSearchDebug()
        {
            return (lastCoverSearchUnit, lastChosenCover, lastThreatPosition, lastSearchTime);
        }

        /// <summary>
        /// Score a cover position based on tactical factors.
        /// </summary>
        private float ScoreCoverTactically(CoverResult cover, Vector3 unitPosition, Vector3 threatPosition, CoverSearchParams searchParams)
        {
            var (score, _) = ScoreCoverTacticallyWithBreakdown(cover, unitPosition, threatPosition, searchParams);
            return score;
        }

        /// <summary>
        /// Score a cover position and return breakdown for debugging.
        /// </summary>
        private (float score, string breakdown) ScoreCoverTacticallyWithBreakdown(CoverResult cover, Vector3 unitPosition, Vector3 threatPosition, CoverSearchParams searchParams)
        {
            var sb = new System.Text.StringBuilder();

            // === ALIGNMENT IS CRITICAL ===
            float alignment = cover.Score; // 0-1, how well cover faces threat

            if (alignment < 0.3f)
            {
                return (-100f, $"REJECTED: alignment={alignment:F2} < 0.3");
            }

            float alignmentScore = alignment * 50f;
            sb.Append($"align={alignmentScore:F0}");

            float score = alignmentScore;

            // === COVER TYPE SCORING ===
            Vector2 directionToThreat = ((Vector2)(threatPosition - cover.WorldPosition)).normalized;
            bool hasFullCover = false;
            bool hasHalfCover = false;

            foreach (var source in cover.CoverSources)
            {
                float sourceAlignment = Vector2.Dot(source.DirectionToCover, directionToThreat);

                // Full cover requires stricter alignment (0.7) - must be facing threat more directly
                // Half cover is more forgiving (0.3) - provides partial protection from wider angles
                float alignmentThreshold = (source.Type == CoverType.Full) ? 0.7f : 0.3f;

                if (sourceAlignment > alignmentThreshold)
                {
                    if (source.Type == CoverType.Full)
                        hasFullCover = true;
                    else if (source.Type == CoverType.Half)
                        hasHalfCover = true;
                }
            }

            if (!hasFullCover && !hasHalfCover)
            {
                return (-100f, $"REJECTED: no aligned cover (align={alignment:F2})");
            }

            float coverTypeScore = 0f;
            string coverTypeStr = "";
            if (hasHalfCover)
            {
                // Half cover: slightly preferred when aggressive (allows peeking)
                coverTypeScore = Mathf.Lerp(25f, 35f, searchParams.Aggression);
                coverTypeStr = "half";
            }
            else if (hasFullCover)
            {
                // Full cover: base score reduced (was 20-5, now 15-0)
                // Full cover restricts movement and limits offensive options
                coverTypeScore = Mathf.Lerp(15f, 0f, searchParams.Aggression);

                // Additional penalty for Aggressive posture - they actively avoid full cover
                if (searchParams.Posture == Posture.Aggressive)
                {
                    coverTypeScore -= 10f;
                }
                coverTypeStr = "full";
            }
            score += coverTypeScore;
            sb.Append($", {coverTypeStr}={coverTypeScore:F0}");

            // === TRAVEL DISTANCE ===
            float travelPenalty = cover.DistanceFromUnit * 8f;
            score -= travelPenalty;
            sb.Append($", travel=-{travelPenalty:F0}");

            // === RANGE TO THREAT ===
            float distToThreat = Vector3.Distance(cover.WorldPosition, threatPosition);
            float rangeScore = 0f;

            float optimalRangeMin = Mathf.Lerp(searchParams.WeaponRange * 0.6f, searchParams.WeaponRange * 0.3f, searchParams.Aggression);
            float optimalRangeMax = Mathf.Lerp(searchParams.WeaponRange * 0.9f, searchParams.WeaponRange * 0.6f, searchParams.Aggression);

            if (distToThreat < searchParams.MinEngageRange)
            {
                rangeScore = -(searchParams.MinEngageRange - distToThreat) * 15f;
                sb.Append($", tooClose={rangeScore:F0}");
            }
            else if (distToThreat > searchParams.WeaponRange)
            {
                rangeScore = -(distToThreat - searchParams.WeaponRange) * 20f;
                sb.Append($", outOfRange={rangeScore:F0}");
            }
            else if (distToThreat >= optimalRangeMin && distToThreat <= optimalRangeMax)
            {
                rangeScore = 10f;
                sb.Append($", optRange=+10");
            }
            score += rangeScore;

            // === NEARBY UNITS SCORE (allies +5, enemies -5 each within 3 units) ===
            float nearbyScore = CalculateNearbyUnitsScore(cover.WorldPosition, searchParams);
            if (nearbyScore != 0)
            {
                score += nearbyScore;
                sb.Append($", nearby={nearbyScore:+0;-0}");
            }

            // === LEADER PROXIMITY BONUS ===
            float leaderBonus = CalculateLeaderProximityBonus(cover.WorldPosition, searchParams);
            if (leaderBonus != 0)
            {
                score += leaderBonus;
                sb.Append($", leader={leaderBonus:F0}");
            }

            // === RALLY POINT PROXIMITY BONUS ===
            float rallyBonus = CalculateRallyPointBonus(cover.WorldPosition, searchParams);
            if (rallyBonus != 0)
            {
                score += rallyBonus;
                sb.Append($", rally={rallyBonus:F0}");
            }

            // === LOS AND ENGAGEMENT SCORING (Mode-dependent) ===
            var (losScore, losBreakdown, hasAnyLOS) = CalculateLOSScore(cover.WorldPosition, searchParams, hasFullCover);

            if (searchParams.Mode == CoverMode.Fighting)
            {
                // Fighting mode: REQUIRE LOS to at least one enemy
                if (!hasAnyLOS)
                {
                    return (-100f, $"REJECTED: Fighting mode - no LOS to any enemy. {sb}");
                }

                // In fighting mode, full cover without LOS should never happen (we just rejected it)
                // But full cover WITH LOS is less penalized than normal
                score += losScore;
                if (!string.IsNullOrEmpty(losBreakdown))
                {
                    sb.Append($", {losBreakdown}");
                }
            }
            else // Defensive mode
            {
                // Defensive mode: LOS is a bonus, not a requirement
                // Cover protection is weighted higher (already handled by cover type scoring)
                if (hasAnyLOS)
                {
                    // Bonus for having LOS even in defensive mode (can still shoot back)
                    score += losScore * 0.5f; // Reduced weight in defensive mode
                    if (!string.IsNullOrEmpty(losBreakdown))
                    {
                        sb.Append($", def_los={losScore * 0.5f:F0}");
                    }
                }
                else
                {
                    sb.Append($", noLOS(defensive)");
                }
            }

            // === DEFENSIVE COVER SCORING (protection FROM enemies) ===
            var (enemyCoverScore, enemyCoverBreakdown, enemiesInRange) = CalculateEnemyCoverScore(cover.WorldPosition, searchParams);

            if (searchParams.Mode == CoverMode.Fighting)
            {
                // Fighting mode: REQUIRE at least one enemy in range
                if (enemiesInRange == 0)
                {
                    return (-100f, $"REJECTED: Fighting mode - no enemies in range. {sb}");
                }

                score += enemyCoverScore;
                if (!string.IsNullOrEmpty(enemyCoverBreakdown))
                {
                    sb.Append($", {enemyCoverBreakdown}");
                }
            }
            else // Defensive/Retreat mode
            {
                // Defensive mode: enemy cover is still valuable but not required
                if (enemiesInRange > 0 && enemyCoverScore > 0)
                {
                    score += enemyCoverScore;
                    if (!string.IsNullOrEmpty(enemyCoverBreakdown))
                    {
                        sb.Append($", {enemyCoverBreakdown}");
                    }
                }
            }

            return (score, sb.ToString());
        }

        /// <summary>
        /// Calculate LOS score from a prospect cover position to known enemies.
        /// Returns (score, breakdown, hasAnyLOS).
        /// </summary>
        private (float score, string breakdown, bool hasAnyLOS) CalculateLOSScore(Vector3 coverPosition, CoverSearchParams searchParams, bool hasFullCover)
        {
            if (searchParams.KnownEnemies == null || searchParams.KnownEnemies.Count == 0)
            {
                // No enemies provided - can't check LOS, assume we have it
                return (0f, "", true);
            }

            float totalScore = 0f;
            int enemiesWithLOS = 0;
            int clearShots = 0;
            var breakdown = new System.Text.StringBuilder();

            foreach (var enemy in searchParams.KnownEnemies)
            {
                if (enemy == null) continue;

                var targetable = enemy.GetComponent<Core.ITargetable>();
                if (targetable == null || targetable.IsDead) continue;

                Vector3 enemyPos = enemy.transform.position;

                // Check LOS from prospect position to enemy
                var losResult = Combat.CombatUtils.CheckLineOfSight(coverPosition, enemyPos);

                if (!losResult.IsBlocked)
                {
                    enemiesWithLOS++;

                    // Check if it's a clear shot (enemy has no cover from our prospect position)
                    if (!losResult.IsPartialCover)
                    {
                        clearShots++;
                    }
                }
            }

            bool hasAnyLOS = enemiesWithLOS > 0;

            if (hasAnyLOS)
            {
                // Target availability bonus: +20 for having at least 1 target, +5 for each additional
                // This prevents massive scores just from seeing lots of enemies
                totalScore = 20f + (enemiesWithLOS - 1) * 5f;

                // Clear shot bonus: +10 for first clear shot, +3 for each additional
                if (clearShots > 0)
                {
                    totalScore += 10f + (clearShots - 1) * 3f;
                }

                breakdown.Append($"LOS={enemiesWithLOS}");
                if (clearShots > 0)
                {
                    breakdown.Append($",clear={clearShots}");
                }

                // In Fighting mode with full cover, we found LOS - that's good but reduce full cover penalty slightly
                // (full cover with LOS is better than full cover without)
                if (hasFullCover && searchParams.Mode == CoverMode.Fighting)
                {
                    totalScore += 5f; // Small bonus for full cover that actually has a shot
                    breakdown.Append(",fullw/LOS");
                }
            }

            return (totalScore, breakdown.ToString(), hasAnyLOS);
        }

        /// <summary>
        /// Calculate defensive cover score based on protection FROM enemies.
        /// Checks if enemies can shoot the unit at this cover position.
        /// Returns (score, breakdown, enemiesInRange). Score of -1 means no enemies in range (reject in Fighting mode).
        /// </summary>
        private (float score, string breakdown, int enemiesInRange) CalculateEnemyCoverScore(Vector3 coverPosition, CoverSearchParams searchParams)
        {
            if (searchParams.KnownEnemies == null || searchParams.KnownEnemies.Count == 0)
            {
                // No enemies provided - can't evaluate defensive cover
                return (-1f, "no_enemies", 0);
            }

            int enemiesInRange = 0;
            int enemiesCovered = 0;
            float totalScore = 0f;
            var breakdown = new System.Text.StringBuilder();

            foreach (var enemy in searchParams.KnownEnemies)
            {
                if (enemy == null) continue;

                var targetable = enemy.GetComponent<ITargetable>();
                if (targetable == null || targetable.IsDead) continue;

                // Get enemy's weapon range
                var enemyController = enemy.GetComponent<UnitController>();
                float enemyWeaponRange = enemyController != null ? enemyController.WeaponRange : 15f;

                // Apply tactics-based estimation uncertainty
                // Guestimate = 5 - (Tactics / 5), minimum 0
                // Higher tactics = more accurate range estimation
                int guestimate = Mathf.Max(0, 5 - (searchParams.Tactics / 5));
                float estimatedRange = enemyWeaponRange;
                if (guestimate > 0)
                {
                    estimatedRange = enemyWeaponRange + Random.Range(-guestimate, guestimate + 1);
                }

                // Check if enemy is within estimated range of cover position
                Vector3 enemyPos = enemy.transform.position;
                float distToEnemy = Vector3.Distance(coverPosition, enemyPos);
                if (distToEnemy > estimatedRange)
                {
                    // Enemy out of range, skip
                    continue;
                }

                enemiesInRange++;

                // Raycast FROM enemy TO cover position to check if cover protects us
                // CheckLineOfSight already handles half cover adjacency (only counts if near target)
                var losResult = CombatUtils.CheckLineOfSight(enemyPos, coverPosition);

                if (losResult.IsBlocked || losResult.IsPartialCover)
                {
                    // We have cover from this enemy!
                    enemiesCovered++;

                    // Full cover = +8, Half cover = +5
                    if (losResult.CoverType == CoverType.Full)
                    {
                        totalScore += 8f;
                    }
                    else if (losResult.CoverType == CoverType.Half)
                    {
                        totalScore += 5f;
                    }
                }
            }

            // No enemies in range - return -1 to signal rejection in Fighting mode
            if (enemiesInRange == 0)
            {
                return (-1f, "no_enemies_in_range", 0);
            }

            // Bonus: if ALL enemies in range are covered, multiply by 2
            if (enemiesCovered == enemiesInRange)
            {
                totalScore *= 2f;
                breakdown.Append($"cover={enemiesCovered}/{enemiesInRange}(ALL)x2");
            }
            else
            {
                breakdown.Append($"cover={enemiesCovered}/{enemiesInRange}");
            }

            return (totalScore, breakdown.ToString(), enemiesInRange);
        }

        /// <summary>
        /// Calculate bonus for staying close to the squad leader.
        /// Cover near the leader is preferred for squad cohesion.
        /// </summary>
        private float CalculateLeaderProximityBonus(Vector3 coverPosition, CoverSearchParams searchParams)
        {
            if (!searchParams.LeaderPosition.HasValue) return 0f;

            Vector3 leaderPos = searchParams.LeaderPosition.Value;
            float distToLeader = Vector3.Distance(coverPosition, leaderPos);

            const float idealRange = 4f;   // Ideal distance from leader
            const float maxRange = 10f;    // Beyond this, no bonus

            if (distToLeader <= idealRange)
            {
                // Very close to leader - big bonus
                return 30f;
            }
            else if (distToLeader <= maxRange)
            {
                // Nearby leader - scaled bonus
                float factor = 1f - ((distToLeader - idealRange) / (maxRange - idealRange));
                return 30f * factor;
            }

            // Too far from leader - no bonus (but no penalty either, they might need to flank)
            return 0f;
        }

        /// <summary>
        /// Calculate bonus for staying close to the squad rally point.
        /// This anchors the squad and prevents them from advancing too far.
        /// Leaders get a MUCH stronger bonus to keep them anchored.
        /// </summary>
        private float CalculateRallyPointBonus(Vector3 coverPosition, CoverSearchParams searchParams)
        {
            if (!searchParams.RallyPoint.HasValue) return 0f;

            Vector3 rallyPos = searchParams.RallyPoint.Value;
            float distToRally = Vector3.Distance(coverPosition, rallyPos);

            // Leaders have tighter constraints - they anchor the squad
            float idealRange = searchParams.IsLeader ? 5f : 8f;
            float maxRange = searchParams.IsLeader ? 12f : 20f;
            float maxBonus = searchParams.IsLeader ? 40f : 15f;  // Leaders get big bonus for staying put
            float overExtendPenalty = searchParams.IsLeader ? 3f : 0.5f;  // Leaders get harsh penalty for wandering

            if (distToRally <= idealRange)
            {
                // Near rally point - full bonus
                return maxBonus;
            }
            else if (distToRally <= maxRange)
            {
                // Further from rally - scaled bonus
                float factor = 1f - ((distToRally - idealRange) / (maxRange - idealRange));
                return maxBonus * factor;
            }

            // Very far from rally point - penalty to discourage overextending
            float overExtension = distToRally - maxRange;
            return -overExtension * overExtendPenalty;
        }

        /// <summary>
        /// Calculate score modifier based on nearby units.
        /// +5 for each ally within 3 units (safety in numbers)
        /// -15 for each enemy within 3 units (dangerous position)
        /// Leaders get double enemy penalty (they should stay back)
        /// Additional steep penalty for enemies extremely close (< 1.5 units)
        /// </summary>
        private float CalculateNearbyUnitsScore(Vector3 coverPosition, CoverSearchParams searchParams)
        {
            // Skip if no team specified
            if (searchParams.UnitTeam == Team.Neutral) return 0f;

            float totalScore = 0f;
            const float proximityRadius = 3f;
            const float dangerRadius = 1.5f; // Extra penalty for very close enemies

            // Leaders get double penalty for being near enemies - they should command from safety
            float enemyPenaltyMult = searchParams.IsLeader ? 2f : 1f;

            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                float dist = Vector3.Distance(coverPosition, targetable.Transform.position);

                if (dist < proximityRadius)
                {
                    if (targetable.Team == searchParams.UnitTeam)
                    {
                        // Ally nearby - bonus for squad cohesion
                        totalScore += 5f;
                    }
                    else
                    {
                        // Enemy nearby - penalty (increased from -5 to -15)
                        totalScore -= 15f * enemyPenaltyMult;

                        // Extra steep penalty for enemies very close (melee range)
                        if (dist < dangerRadius)
                        {
                            float dangerFactor = 1f - (dist / dangerRadius);
                            totalScore -= 30f * dangerFactor * enemyPenaltyMult;
                        }
                    }
                }
            }

            return totalScore;
        }

        /// <summary>
        /// Finds multiple cover positions, sorted by quality.
        /// </summary>
        public List<CoverResult> FindCoverOptions(Vector3 unitPosition, Vector3 threatPosition, int maxResults = 5, float maxDistance = -1f)
        {
            if (coverBaker == null) return new List<CoverResult>();

            if (maxDistance <= 0) maxDistance = maxSearchRadius;

            var candidates = FindCoverCandidates(unitPosition, threatPosition, maxDistance);

            return candidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.DistanceFromUnit)
                .Take(maxResults)
                .ToList();
        }

        /// <summary>
        /// Checks if a specific position provides cover against a threat.
        /// Performs a raycast to verify the cover is actually between unit and threat.
        /// </summary>
        public CoverCheckResult CheckCoverAt(Vector3 position, Vector3 threatPosition)
        {
            if (coverBaker == null)
            {
                return new CoverCheckResult { HasCover = false, Type = CoverType.None };
            }

            // Direction from position to threat
            Vector2 directionToThreat = ((Vector2)(threatPosition - position)).normalized;
            float distToThreat = Vector2.Distance(position, threatPosition);

            // Raycast from position toward threat to find actual cover
            // Cover must be within 1 unit (adjacent tile) to count as protecting us
            const float coverProximityRadius = 1.0f;

            RaycastHit2D[] hits = Physics2D.RaycastAll(position, directionToThreat, distToThreat);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.distance < 0.1f) continue; // Skip very close hits

                var structure = hit.collider.GetComponent<Structure>();
                if (structure == null) continue;

                // Only count this cover if it's close to our position
                // Cover that's far away is enemy cover or no-man's-land
                if (hit.distance > coverProximityRadius)
                {
                    // No cover protecting us from this direction
                    break;
                }

                // This cover is close to our position - it's protecting us
                return new CoverCheckResult
                {
                    HasCover = true,
                    Type = structure.CoverType,
                    CoverObject = hit.collider.gameObject
                };
            }

            // No cover found between us and threat
            return new CoverCheckResult { HasCover = false, Type = CoverType.None };
        }

        /// <summary>
        /// Finds cover positions that allow flanking (cover from threat but with line of sight).
        /// Useful for aggressive AI that wants to shoot while protected.
        /// </summary>
        public List<CoverResult> FindFlankingCover(Vector3 unitPosition, Vector3 threatPosition, float maxDistance = -1f)
        {
            if (coverBaker == null) return new List<CoverResult>();

            if (maxDistance <= 0) maxDistance = maxSearchRadius;

            var candidates = FindCoverCandidates(unitPosition, threatPosition, maxDistance);

            // Filter for positions that have cover but also have line of sight to threat
            var flankingPositions = new List<CoverResult>();

            foreach (var candidate in candidates)
            {
                // Check if we can see the threat from this position (no obstacles between)
                Vector2 dirToThreat = ((Vector2)(threatPosition - candidate.WorldPosition)).normalized;
                float distToThreat = Vector2.Distance(candidate.WorldPosition, threatPosition);

                var hit = Physics2D.Raycast(candidate.WorldPosition, dirToThreat, distToThreat, coverRaycastMask);

                // If raycast doesn't hit anything, or hits something far away, we have LOS
                if (hit.collider == null || hit.distance > distToThreat * 0.9f)
                {
                    flankingPositions.Add(candidate);
                }
            }

            return flankingPositions
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.DistanceFromUnit)
                .ToList();
        }

        /// <summary>
        /// Gets all cover positions within a radius, regardless of threat direction.
        /// Used for flank position searching.
        /// </summary>
        public List<CoverResult> GetAllCoverPositions(Vector2 centerPosition, float maxDistance, GameObject excludeUnit = null)
        {
            if (coverBaker == null) return new List<CoverResult>();

            var results = new List<CoverResult>();
            var allCover = coverBaker.GetAllCoverData();
            var tileOccupancy = TileOccupancy.Instance;

            foreach (var kvp in allCover)
            {
                var tilePos = kvp.Key;
                var worldPos = coverBaker.TileToWorld(tilePos);

                float distance = Vector2.Distance(centerPosition, (Vector2)worldPos);
                if (distance > maxDistance) continue;

                // Skip occupied or reserved tiles (but not our own position)
                if (tileOccupancy != null && !tileOccupancy.IsTileAvailable(tilePos, excludeUnit))
                {
                    continue;
                }

                results.Add(new CoverResult
                {
                    TilePosition = tilePos,
                    WorldPosition = worldPos,
                    DistanceFromUnit = distance,
                    Score = 1f, // Base score, will be evaluated by caller
                    CoverSources = kvp.Value
                });
            }

            return results;
        }

        private List<CoverResult> FindCoverCandidates(Vector3 unitPosition, Vector3 threatPosition, float maxDistance, GameObject excludeUnit = null)
        {
            var results = new List<CoverResult>();
            var allCover = coverBaker.GetAllCoverData();
            var tileOccupancy = TileOccupancy.Instance;

            Vector2 directionToThreat = ((Vector2)(threatPosition - unitPosition)).normalized;

            foreach (var kvp in allCover)
            {
                var tilePos = kvp.Key;
                var worldPos = coverBaker.TileToWorld(tilePos);

                float distanceFromUnit = Vector3.Distance(unitPosition, worldPos);
                if (distanceFromUnit > maxDistance) continue;

                // Include current tile (distance < 0.5) - don't skip it!
                // This allows units to evaluate if staying put is the best option

                // Skip occupied or reserved tiles (but allow our own tile)
                if (tileOccupancy != null && !tileOccupancy.IsTileAvailable(tilePos, excludeUnit))
                {
                    continue;
                }

                // Evaluate cover quality against threat
                float score = EvaluateCoverPosition(worldPos, threatPosition, kvp.Value);

                if (score > 0)
                {
                    results.Add(new CoverResult
                    {
                        TilePosition = tilePos,
                        WorldPosition = worldPos,
                        DistanceFromUnit = distanceFromUnit,
                        Score = score,
                        CoverSources = kvp.Value
                    });
                }
            }

            return results;
        }

        private float EvaluateCoverPosition(Vector3 coverPos, Vector3 threatPos, List<CoverSource> sources)
        {
            Vector2 directionToThreat = ((Vector2)(threatPos - coverPos)).normalized;
            float distToThreat = Vector2.Distance(coverPos, threatPos);

            // Raycast from candidate position toward threat to find actual cover
            // Cover must be within 1 unit (adjacent tile) to count as protecting us
            const float coverProximityRadius = 1.0f;

            RaycastHit2D[] hits = Physics2D.RaycastAll(coverPos, directionToThreat, distToThreat);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            CoverType verifiedCoverType = CoverType.None;
            float verifiedAlignment = 0f;

            foreach (var hit in hits)
            {
                if (hit.distance < 0.1f) continue; // Skip very close hits

                var structure = hit.collider.GetComponent<Structure>();
                if (structure == null) continue;

                // Only count this cover if it's close to our candidate position
                // Cover that's far away is either no-man's-land or enemy cover
                if (hit.distance > coverProximityRadius)
                {
                    // Cover is too far - not protecting this position
                    break;
                }

                // This cover is close to our position - it's protecting us
                // Check alignment with baked data for scoring
                foreach (var source in sources)
                {
                    float alignment = Vector2.Dot(source.DirectionToCover, directionToThreat);
                    if (alignment > verifiedAlignment)
                    {
                        verifiedAlignment = alignment;
                        if (structure.CoverType == CoverType.Full)
                            verifiedCoverType = CoverType.Full;
                        else if (structure.CoverType == CoverType.Half && verifiedCoverType != CoverType.Full)
                            verifiedCoverType = CoverType.Half;
                    }
                }
                break; // Found our cover, stop checking
            }

            // No verified cover protecting this position
            if (verifiedCoverType == CoverType.None || verifiedAlignment <= 0)
            {
                return 0f;
            }

            // Score based on verified cover
            float score = verifiedAlignment;
            if (verifiedCoverType == CoverType.Full)
                score *= 1.1f;

            return score;
        }

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;
        [SerializeField] private Transform debugThreat;

        private void OnDrawGizmos()
        {
            if (!showDebugGizmos || coverBaker == null || debugThreat == null) return;

            var options = FindCoverOptions(transform.position, debugThreat.position, 10);

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                float t = (float)i / options.Count;

                Gizmos.color = Color.Lerp(Color.green, Color.red, t);
                Gizmos.DrawWireCube(option.WorldPosition, Vector3.one * 0.8f);

                // Draw line to cover object
                foreach (var source in option.CoverSources)
                {
                    if (source.SourceObject != null)
                    {
                        Gizmos.color = source.Type == CoverType.Full ? Color.blue : Color.yellow;
                        Gizmos.DrawLine(option.WorldPosition, source.SourceObject.transform.position);
                    }
                }
            }
        }
#endif
    }

    public struct CoverResult
    {
        public Vector3Int TilePosition;
        public Vector3 WorldPosition;
        public float DistanceFromUnit;
        public float Score; // Higher is better
        public List<CoverSource> CoverSources;
    }

    public struct CoverCheckResult
    {
        public bool HasCover;
        public CoverType Type;
        public GameObject CoverObject;
    }

    /// <summary>
    /// Mode for cover searching - affects whether LOS to enemies is required.
    /// </summary>
    public enum CoverMode
    {
        /// <summary>
        /// Fighting mode: REQUIRES LOS to at least one enemy.
        /// Prefers half cover. Big bonus for clear shots.
        /// Use for normal combat positioning.
        /// </summary>
        Fighting,

        /// <summary>
        /// Defensive mode: Does NOT require LOS (allows retreat behind walls).
        /// Still prefers positions with LOS, but cover protection weighted higher.
        /// Use when retreating, overwhelmed, or critically injured.
        /// </summary>
        Defensive
    }

    /// <summary>
    /// Parameters for tactical cover searching that considers combat engagement.
    /// </summary>
    public struct CoverSearchParams
    {
        public float WeaponRange;       // Max engagement range
        public float MinEngageRange;    // Minimum comfortable range (don't get too close)
        public float Aggression;        // 0-1, affects preferred distance and cover type
        public Posture Posture;         // Actual posture enum for nuanced cover decisions
        public Team UnitTeam;           // Team of the unit seeking cover
        public Vector3? LeaderPosition; // Position of squad leader (null if no leader or is leader)
        public Vector3? RallyPoint;     // Squad rally point - units prefer positions near here
        public bool IsLeader;           // True if this unit is the squad leader
        public CoverMode Mode;          // Fighting (requires LOS) or Defensive (allows retreat)
        public List<GameObject> KnownEnemies; // Enemies to check LOS against
        public int Tactics;             // Unit's Tactics stat (1-20) for estimating enemy ranges

        /// <summary>
        /// Create default parameters (neutral, fighting mode).
        /// </summary>
        public static CoverSearchParams Default => new CoverSearchParams
        {
            WeaponRange = 15f,
            MinEngageRange = 4f,
            Aggression = 0.5f,
            Posture = Posture.Neutral,
            UnitTeam = Team.Neutral,
            LeaderPosition = null,
            RallyPoint = null,
            IsLeader = false,
            Mode = CoverMode.Fighting,
            KnownEnemies = null,
            Tactics = 10
        };

        /// <summary>
        /// Create from posture and optional bravery (for Neutral posture).
        /// Defaults to Fighting mode - use WithMode() to change.
        /// </summary>
        public static CoverSearchParams FromPosture(float weaponRange, Posture posture, int bravery = 10, int tactics = 10, Team team = Team.Neutral, Vector3? leaderPos = null, Vector3? rallyPoint = null, bool isLeader = false)
        {
            float aggression = posture switch
            {
                Posture.Defensive => 0f,
                Posture.Aggressive => 1f,
                Posture.Neutral => bravery / 20f, // Let bravery decide
                _ => 0f
            };

            return new CoverSearchParams
            {
                WeaponRange = weaponRange,
                MinEngageRange = 4f,
                Aggression = aggression,
                Posture = posture,
                UnitTeam = team,
                LeaderPosition = leaderPos,
                RallyPoint = rallyPoint,
                IsLeader = isLeader,
                Mode = CoverMode.Fighting,
                KnownEnemies = null,
                Tactics = tactics
            };
        }

        /// <summary>
        /// Return a copy with the specified mode and known enemies.
        /// </summary>
        public CoverSearchParams WithMode(CoverMode mode, List<GameObject> knownEnemies)
        {
            var copy = this;
            copy.Mode = mode;
            copy.KnownEnemies = knownEnemies;
            return copy;
        }
    }
}
