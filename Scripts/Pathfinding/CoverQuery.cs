using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Starbelter.Core;
using Starbelter.Combat;

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

        [Tooltip("Angle tolerance for considering cover 'good' against a threat (degrees)")]
        [SerializeField] private float coverAngleTolerance = 45f;

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

            if (candidates.Count == 0) return null;

            // Score each candidate with tactical considerations
            float bestScore = float.MinValue;
            CoverResult? best = null;

            foreach (var candidate in candidates)
            {
                float score = ScoreCoverTactically(candidate, unitPosition, threatPosition, searchParams);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Score a cover position based on tactical factors.
        /// </summary>
        private float ScoreCoverTactically(CoverResult cover, Vector3 unitPosition, Vector3 threatPosition, CoverSearchParams searchParams)
        {
            // === ALIGNMENT IS CRITICAL ===
            // Base score is 0-1 alignment, but we need it to matter a LOT
            // Cover that doesn't face the threat is nearly useless
            float alignment = cover.Score; // 0-1, how well cover faces threat

            // If alignment is poor (< 0.3), this cover is almost useless against this threat
            if (alignment < 0.3f)
            {
                return -100f; // Reject poorly aligned cover
            }

            // Start with alignment as major factor (0-50 points)
            float score = alignment * 50f;

            // === COVER TYPE SCORING ===
            // Determine best cover type at this position that faces the threat
            CoverType bestCoverType = CoverType.None;
            Vector2 directionToThreat = ((Vector2)(threatPosition - cover.WorldPosition)).normalized;

            foreach (var source in cover.CoverSources)
            {
                // Only count cover that actually faces the threat
                float sourceAlignment = Vector2.Dot(source.DirectionToCover, directionToThreat);
                if (sourceAlignment > 0.3f)
                {
                    if (source.Type == CoverType.Full)
                        bestCoverType = CoverType.Full;
                    else if (source.Type == CoverType.Half && bestCoverType != CoverType.Full)
                        bestCoverType = CoverType.Half;
                }
            }

            // Cover type bonus - but not so large it overrides proximity
            if (bestCoverType == CoverType.Full)
            {
                score += Mathf.Lerp(20f, 10f, searchParams.Aggression);
            }
            else if (bestCoverType == CoverType.Half)
            {
                score += Mathf.Lerp(10f, 20f, searchParams.Aggression);
            }
            else
            {
                // No aligned cover at this position
                return -100f;
            }

            // === TRAVEL DISTANCE - VERY IMPORTANT ===
            // Nearby cover is much better - don't run across the battlefield
            // Each tile of travel is a significant penalty
            score -= cover.DistanceFromUnit * 8f;

            // === RANGE TO THREAT ===
            float distToThreat = Vector3.Distance(cover.WorldPosition, threatPosition);

            // Optimal range depends on aggression
            float optimalRangeMin = Mathf.Lerp(searchParams.WeaponRange * 0.6f, searchParams.WeaponRange * 0.3f, searchParams.Aggression);
            float optimalRangeMax = Mathf.Lerp(searchParams.WeaponRange * 0.9f, searchParams.WeaponRange * 0.6f, searchParams.Aggression);

            if (distToThreat < searchParams.MinEngageRange)
            {
                // Too close - penalty
                score -= (searchParams.MinEngageRange - distToThreat) * 15f;
            }
            else if (distToThreat > searchParams.WeaponRange)
            {
                // Out of range - heavy penalty
                score -= (distToThreat - searchParams.WeaponRange) * 20f;
            }
            else if (distToThreat >= optimalRangeMin && distToThreat <= optimalRangeMax)
            {
                // In optimal range - small bonus
                score += 10f;
            }

            // === ENEMY PROXIMITY PENALTY ===
            // Check for nearby enemies - cover near multiple enemies is dangerous
            float enemyProximityPenalty = CalculateEnemyProximityPenalty(cover.WorldPosition, searchParams);
            score -= enemyProximityPenalty;

            // === LEADER PROXIMITY BONUS ===
            // Stay close to squad leader for cohesion
            float leaderBonus = CalculateLeaderProximityBonus(cover.WorldPosition, searchParams);
            score += leaderBonus;

            return score;
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
        /// Calculate penalty based on how many enemies are near the cover position.
        /// Being close to multiple enemies is very dangerous even with cover.
        /// </summary>
        private float CalculateEnemyProximityPenalty(Vector3 coverPosition, CoverSearchParams searchParams)
        {
            // Skip if no team specified
            if (searchParams.UnitTeam == Team.Neutral) return 0f;

            float totalPenalty = 0f;
            const float dangerRadius = 8f; // Enemies within this range are dangerous
            const float extremeDangerRadius = 4f; // Enemies this close are extremely dangerous

            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == searchParams.UnitTeam) continue; // Skip allies
                if (targetable.Team == Team.Neutral) continue; // Skip neutrals
                if (targetable.IsDead) continue;

                float dist = Vector3.Distance(coverPosition, targetable.Transform.position);

                if (dist < extremeDangerRadius)
                {
                    // Extremely close enemy - massive penalty, almost never go here
                    totalPenalty += 100f;
                }
                else if (dist < dangerRadius)
                {
                    // Nearby enemy - significant penalty that scales with proximity
                    // At 4 units: 40 penalty, at 8 units: 0 penalty
                    float proximityFactor = 1f - ((dist - extremeDangerRadius) / (dangerRadius - extremeDangerRadius));
                    totalPenalty += 40f * proximityFactor;
                }
            }

            return totalPenalty;
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
            // Only cover within this radius counts as "ours"
            const float coverProximityRadius = 1.5f;

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
                if (distance < 0.5f) continue; // Skip positions too close

                // Skip occupied or reserved tiles
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

                // Skip positions too close to the unit's current position
                if (distanceFromUnit < 0.5f) continue;

                // Skip occupied or reserved tiles
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
            // Only cover within this radius of the candidate position counts as "ours"
            const float coverProximityRadius = 1.5f;

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
    /// Parameters for tactical cover searching that considers combat engagement.
    /// </summary>
    public struct CoverSearchParams
    {
        public float WeaponRange;      // Max engagement range
        public float MinEngageRange;   // Minimum comfortable range (don't get too close)
        public float Aggression;       // 0-1, affects preferred distance and cover type
        public Team UnitTeam;          // Team of the unit seeking cover
        public Vector3? LeaderPosition; // Position of squad leader (null if no leader or is leader)

        /// <summary>
        /// Create default parameters (neutral).
        /// </summary>
        public static CoverSearchParams Default => new CoverSearchParams
        {
            WeaponRange = 15f,
            MinEngageRange = 4f,
            Aggression = 0.5f,
            UnitTeam = Team.Neutral,
            LeaderPosition = null
        };

        /// <summary>
        /// Create from posture and optional bravery (for Neutral posture).
        /// </summary>
        public static CoverSearchParams FromPosture(float weaponRange, Posture posture, int bravery = 10, Team team = Team.Neutral, Vector3? leaderPos = null)
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
                UnitTeam = team,
                LeaderPosition = leaderPos
            };
        }
    }
}
