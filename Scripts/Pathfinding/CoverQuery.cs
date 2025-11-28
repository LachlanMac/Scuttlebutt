using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
            if (coverBaker == null) return null;

            if (maxDistance <= 0) maxDistance = maxSearchRadius;

            var candidates = FindCoverCandidates(unitPosition, threatPosition, maxDistance);

            if (candidates.Count == 0) return null;

            // Combined score: prioritize nearby cover that still faces the threat
            // Formula: score / (1 + distance * 0.2) - closer cover gets boosted
            var best = candidates
                .OrderByDescending(c => c.Score / (1f + c.DistanceFromUnit * 0.2f))
                .First();

            return best;
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
        /// Performs a raycast to verify the cover is between unit and threat.
        /// </summary>
        public CoverCheckResult CheckCoverAt(Vector3 position, Vector3 threatPosition)
        {
            if (coverBaker == null)
            {
                return new CoverCheckResult { HasCover = false, Type = CoverType.None };
            }

            var tilePos = coverBaker.WorldToTile(position);
            var coverSources = coverBaker.GetCoverAt(tilePos);

            if (coverSources.Count == 0)
            {
                return new CoverCheckResult { HasCover = false, Type = CoverType.None };
            }

            // Direction from position to threat
            Vector2 directionToThreat = ((Vector2)(threatPosition - position)).normalized;

            CoverType bestCoverType = CoverType.None;
            GameObject bestCoverObject = null;

            foreach (var source in coverSources)
            {
                // Check if this cover source is between us and the threat
                // The cover's direction should roughly oppose the threat direction
                float dot = Vector2.Dot(source.DirectionToCover, directionToThreat);

                // dot > 0 means cover is in the direction of the threat (good!)
                if (dot > Mathf.Cos(coverAngleTolerance * Mathf.Deg2Rad))
                {
                    // Cover direction aligns with threat - we're covered
                    // No raycast needed since we already know we're at a cover tile
                    if (source.Type == CoverType.Full || bestCoverType == CoverType.None)
                    {
                        bestCoverType = source.Type;
                        bestCoverObject = source.SourceObject;
                    }
                }
            }

            return new CoverCheckResult
            {
                HasCover = bestCoverType != CoverType.None,
                Type = bestCoverType,
                CoverObject = bestCoverObject
            };
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

        private List<CoverResult> FindCoverCandidates(Vector3 unitPosition, Vector3 threatPosition, float maxDistance)
        {
            var results = new List<CoverResult>();
            var allCover = coverBaker.GetAllCoverData();

            Vector2 directionToThreat = ((Vector2)(threatPosition - unitPosition)).normalized;

            foreach (var kvp in allCover)
            {
                var tilePos = kvp.Key;
                var worldPos = coverBaker.TileToWorld(tilePos);

                float distanceFromUnit = Vector3.Distance(unitPosition, worldPos);
                if (distanceFromUnit > maxDistance) continue;

                // Check if this position is occupied (will integrate with TileOccupancy later)
                // For now, skip positions too close to the unit's current position
                if (distanceFromUnit < 0.5f) continue;

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

            float bestScore = 0f;

            foreach (var source in sources)
            {
                // How well does this cover face the threat?
                float alignment = Vector2.Dot(source.DirectionToCover, directionToThreat);

                if (alignment > 0)
                {
                    // Base score from alignment (0 to 1)
                    float score = alignment;

                    // Small bonus for full cover (not enough to override distance)
                    if (source.Type == CoverType.Full)
                        score *= 1.1f;

                    bestScore = Mathf.Max(bestScore, score);
                }
            }

            return bestScore;
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
}
