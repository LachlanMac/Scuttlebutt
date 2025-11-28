using UnityEngine;
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
        /// </summary>
        /// <param name="attackerPos">Position of the attacker</param>
        /// <param name="targetPos">Position of the target</param>
        /// <param name="ignoreRadius">Ignore structures within this radius of attacker (own cover)</param>
        /// <returns>LineOfSightResult with cover information</returns>
        public static LineOfSightResult CheckLineOfSight(Vector2 attackerPos, Vector2 targetPos, float ignoreHalfCoverRadius = 1.5f)
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
            // Note: RaycastAll doesn't detect colliders the ray starts inside of
            RaycastHit2D[] hits = Physics2D.RaycastAll(attackerPos, direction, distance);

            // Sort by distance (RaycastAll doesn't guarantee order)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                // Skip hits at distance 0 (inside collider at start)
                if (hit.distance < 0.01f) continue;

                // Check if we hit a structure
                var structure = hit.collider.GetComponent<Structure>();
                if (structure != null)
                {
                    float distFromAttacker = hit.distance;

                    // Only ignore HALF cover near attacker (peeking over own cover)
                    // Full cover ALWAYS blocks, even at close range
                    if (structure.CoverType == CoverType.Half && distFromAttacker < ignoreHalfCoverRadius)
                    {
                        continue; // Skip half cover near attacker
                    }

                    // Found cover between attacker and target
                    result.BlockingCover = structure;
                    result.CoverType = structure.CoverType;

                    if (structure.CoverType == CoverType.Full)
                    {
                        result.IsBlocked = true;
                        result.HasLineOfSight = false;
                        Debug.Log($"[CheckLineOfSight] BLOCKED by {structure.name} at dist {distFromAttacker:F2} from attacker at {attackerPos}");
                    }
                    else if (structure.CoverType == CoverType.Half)
                    {
                        result.IsPartialCover = true;
                        // Half cover doesn't fully block - shot can still be attempted
                    }

                    // Use the first (closest) cover found
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
        /// Find a position to flank a target - has LOS to target AND cover from target.
        /// </summary>
        /// <param name="attackerPos">Current attacker position</param>
        /// <param name="targetPos">Target to flank</param>
        /// <param name="maxSearchRadius">Max distance to search for flank positions</param>
        /// <param name="coverQuery">CoverQuery instance for finding cover</param>
        /// <returns>FlankResult with best position found</returns>
        public static FlankResult FindFlankPosition(Vector2 attackerPos, Vector2 targetPos, float maxSearchRadius, CoverQuery coverQuery)
        {
            var result = new FlankResult
            {
                Found = false,
                Position = attackerPos,
                Score = 0f,
                CoverAtPosition = CoverType.None
            };

            if (coverQuery == null) return result;

            // Get all cover positions within search radius
            var coverPositions = coverQuery.GetAllCoverPositions(attackerPos, maxSearchRadius);

            float bestScore = 0f;

            Debug.Log($"[FindFlankPosition] Searching {coverPositions.Count} cover positions within {maxSearchRadius} of attacker");

            const float MIN_DISTANCE_FROM_TARGET = 3f; // Don't flank into melee range

            foreach (var coverPos in coverPositions)
            {
                Vector2 candidatePos = coverPos.WorldPosition;

                // Skip if too close to current position (not really flanking)
                if (Vector2.Distance(candidatePos, attackerPos) < 2f) continue;

                // Skip if too close to target (don't run into melee range)
                float distToTarget = Vector2.Distance(candidatePos, targetPos);
                if (distToTarget < MIN_DISTANCE_FROM_TARGET)
                {
                    Debug.Log($"[FindFlankPosition] Position {candidatePos} rejected - too close to target ({distToTarget:F1} < {MIN_DISTANCE_FROM_TARGET})");
                    continue;
                }

                // Check 1: Do I have LOS to target from this position?
                // Use larger ignore radius so we don't count the cover we're hiding behind as blocking our shot
                // Note: This checks from tile center - actual fire point may differ slightly
                var losToTarget = CheckLineOfSight(candidatePos, targetPos, 1.5f);
                if (losToTarget.IsBlocked)
                {
                    Debug.Log($"[FindFlankPosition] Position {candidatePos} rejected - no LOS to target (blocked by {losToTarget.BlockingCover?.name} at dist {losToTarget.Distance:F1})");
                    continue;
                }

                Debug.Log($"[FindFlankPosition] Position {candidatePos} has LOS to target at {targetPos}");

                // Check 2: Does this position have cover? (check the cover sources at this tile)
                bool hasCover = coverPos.CoverSources != null && coverPos.CoverSources.Count > 0;
                if (!hasCover)
                {
                    Debug.Log($"[FindFlankPosition] Position {candidatePos} rejected - no cover sources");
                    continue;
                }

                // Score this position
                float score = 0f;

                // Check cover quality at this position
                CoverType bestCoverType = CoverType.None;
                foreach (var source in coverPos.CoverSources)
                {
                    if (source.Type == CoverType.Full)
                        bestCoverType = CoverType.Full;
                    else if (source.Type == CoverType.Half && bestCoverType != CoverType.Full)
                        bestCoverType = CoverType.Half;
                }

                // Prefer full cover over half cover
                if (bestCoverType == CoverType.Full)
                    score += 50f;
                else if (bestCoverType == CoverType.Half)
                    score += 25f;

                // Prefer closer positions (less travel = less exposure)
                float travelDist = Vector2.Distance(attackerPos, candidatePos);
                score += (maxSearchRadius - travelDist) / maxSearchRadius * 30f;

                // Prefer positions that are more "flanking" (perpendicular to current angle)
                Vector2 currentDir = (targetPos - attackerPos).normalized;
                Vector2 newDir = (targetPos - candidatePos).normalized;
                float angleChange = Vector2.Angle(currentDir, newDir);
                score += (angleChange / 180f) * 20f; // More angle change = better flank

                Debug.Log($"[FindFlankPosition] Position {candidatePos} VALID - score {score}, cover {bestCoverType}");

                if (score > bestScore)
                {
                    bestScore = score;
                    result.Found = true;
                    result.Position = candidatePos;
                    result.Score = score;
                    result.CoverAtPosition = bestCoverType;
                }
            }

            return result;
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
    }
}
