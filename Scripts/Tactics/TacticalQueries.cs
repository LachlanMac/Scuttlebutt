using UnityEngine;
using System;
using System.Collections.Generic;
using Pathfinding;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.Tactics
{
    /// <summary>
    /// Result of a path query with all relevant scoring data.
    /// </summary>
    public struct PathResult
    {
        public Vector3 Destination;
        public List<Vector3> Waypoints;
        public float Distance;
        public float ThreatCost;
        public float TotalScore;
        public bool IsValid;

        public static PathResult Invalid => new PathResult { IsValid = false };
    }

    /// <summary>
    /// Result of scoring a potential destination tile.
    /// </summary>
    public struct TileScore
    {
        public Vector3 Position;
        public float Distance;
        public float ThreatCost;
        public float CoverQuality;
        public bool HasLOS;
        public float TotalScore;
        public PathResult Path;
    }

    /// <summary>
    /// Small, composable query functions for tactical decisions.
    /// Designed for async path scoring - request many, collect results, pick best.
    /// </summary>
    public static class TacticalQueries
    {
        // === LINE OF SIGHT ===

        /// <summary>
        /// Check if there's clear line of sight between two points.
        /// </summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to, LayerMask obstacleMask)
        {
            Vector2 direction = (to - from);
            float distance = direction.magnitude;
            return !Physics2D.Raycast(from, direction.normalized, distance, obstacleMask);
        }

        /// <summary>
        /// Check LOS to a target, using default obstacle layers.
        /// </summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            // Default: check against obstacles layer
            int obstacleMask = LayerMask.GetMask("Obstacles", "Cover");
            return HasLineOfSight(from, to, obstacleMask);
        }

        // === THREAT ===

        /// <summary>
        /// Get threat level at a world position for a team.
        /// </summary>
        public static float GetThreat(Vector3 position, Team team)
        {
            if (TileThreatMap.Instance == null) return 0f;
            return TileThreatMap.Instance.GetThreatAtWorld(position, team);
        }

        /// <summary>
        /// Calculate total threat along a path for a team.
        /// </summary>
        public static float GetThreatAlongPath(List<Vector3> waypoints, Team team)
        {
            if (TileThreatMap.Instance == null || waypoints == null || waypoints.Count == 0)
                return 0f;

            float totalThreat = 0f;
            foreach (var point in waypoints)
            {
                totalThreat += TileThreatMap.Instance.GetThreatAtWorld(point, team);
            }
            return totalThreat;
        }

        /// <summary>
        /// Check if position is in dangerous threat zone.
        /// </summary>
        public static bool IsInDanger(Vector3 position, Team team)
        {
            return GetThreat(position, team) >= TacticalConstants.ThreatDangerous;
        }

        /// <summary>
        /// Check if position is in deadly threat zone.
        /// </summary>
        public static bool IsDeadly(Vector3 position, Team team)
        {
            return GetThreat(position, team) >= TacticalConstants.ThreatDeadly;
        }

        // === COVER ===

        /// <summary>
        /// Get cover quality at a position relative to a threat direction.
        /// Returns 0-1 (0 = no cover, 0.5 = half, 1 = full).
        /// </summary>
        public static float GetCoverQuality(Vector3 position, Vector3 threatDirection)
        {
            if (CoverQuery.Instance == null) return 0f;

            var coverInfo = CoverQuery.Instance.GetCoverInfo(position, threatDirection);
            if (!coverInfo.hasCover) return 0f;

            return coverInfo.isFullCover ? 1f : 0.5f;
        }

        /// <summary>
        /// Find nearby cover positions within radius.
        /// </summary>
        public static List<Vector3> FindCoverPositions(Vector3 center, float radius, Vector3 threatDirection)
        {
            var results = new List<Vector3>();
            if (CoverQuery.Instance == null) return results;

            // Sample grid around center
            float step = 1f;
            for (float x = -radius; x <= radius; x += step)
            {
                for (float y = -radius; y <= radius; y += step)
                {
                    Vector3 samplePos = center + new Vector3(x, y, 0);
                    if (Vector3.Distance(center, samplePos) > radius) continue;

                    var coverInfo = CoverQuery.Instance.GetCoverInfo(samplePos, threatDirection);
                    if (coverInfo.hasCover)
                    {
                        results.Add(samplePos);
                    }
                }
            }

            return results;
        }

        // === DISTANCE ===

        /// <summary>
        /// Get straight-line distance between two points.
        /// </summary>
        public static float GetDistance(Vector3 from, Vector3 to)
        {
            return Vector3.Distance(from, to);
        }

        /// <summary>
        /// Check if within engagement range.
        /// </summary>
        public static bool IsInRange(Vector3 from, Vector3 to, float range)
        {
            return GetDistance(from, to) <= range;
        }

        // === PATH QUERIES (ASYNC) ===

        /// <summary>
        /// Request a path asynchronously. Calls onComplete when done.
        /// This is the core building block for scoring multiple destinations.
        /// </summary>
        public static void RequestPath(Vector3 from, Vector3 to, Team team, Action<PathResult> onComplete)
        {
            var path = ABPath.Construct(from, to);

            AstarPath.StartPath(path, (p) =>
            {
                if (p.error || p.vectorPath == null || p.vectorPath.Count == 0)
                {
                    onComplete?.Invoke(PathResult.Invalid);
                    return;
                }

                // Copy waypoints - the path's list may get recycled/pooled
                var waypoints = new List<Vector3>(p.vectorPath);

                var result = new PathResult
                {
                    Destination = to,
                    Waypoints = waypoints,
                    Distance = p.GetTotalLength(),
                    ThreatCost = GetThreatAlongPath(waypoints, team),
                    IsValid = true
                };

                // Calculate total score (lower is better)
                result.TotalScore = result.Distance * TacticalConstants.ScoreDistanceWeight
                                  + result.ThreatCost * TacticalConstants.ThreatPathWeight;

                onComplete?.Invoke(result);
            });
        }

        /// <summary>
        /// Request a path and block until complete.
        /// Use this only when you need the path immediately for movement.
        /// </summary>
        public static PathResult GetPathBlocking(Vector3 from, Vector3 to, Team team)
        {
            var path = ABPath.Construct(from, to);
            AstarPath.StartPath(path);
            path.BlockUntilCalculated();

            if (path.error || path.vectorPath == null || path.vectorPath.Count == 0)
            {
                return PathResult.Invalid;
            }

            // Copy waypoints - the path's list may get recycled/pooled
            var waypoints = new List<Vector3>(path.vectorPath);

            var result = new PathResult
            {
                Destination = to,
                Waypoints = waypoints,
                Distance = path.GetTotalLength(),
                ThreatCost = GetThreatAlongPath(waypoints, team),
                IsValid = true
            };

            result.TotalScore = result.Distance * TacticalConstants.ScoreDistanceWeight
                              + result.ThreatCost * TacticalConstants.ThreatPathWeight;

            return result;
        }

        // === TILE SCORING ===

        /// <summary>
        /// Score a destination tile considering all tactical factors.
        /// Higher score = better destination.
        /// </summary>
        public static float ScoreTile(
            Vector3 tilePos,
            Vector3 currentPos,
            Vector3? targetPos,
            Team team,
            PathResult path)
        {
            float score = 0f;

            // Distance component (prefer closer destinations)
            if (path.IsValid)
            {
                score -= path.Distance * TacticalConstants.ScoreDistanceWeight;
                score -= path.ThreatCost * Mathf.Abs(TacticalConstants.ScoreThreatWeight);
            }
            else
            {
                return float.MinValue; // Invalid path = worst score
            }

            // Threat at destination
            float destThreat = GetThreat(tilePos, team);
            score += destThreat * TacticalConstants.ScoreThreatWeight; // Negative weight

            // Cover quality (if we have a target to hide from)
            if (targetPos.HasValue)
            {
                Vector3 threatDir = (targetPos.Value - tilePos).normalized;
                float coverQuality = GetCoverQuality(tilePos, threatDir);
                score += coverQuality * TacticalConstants.ScoreCoverWeight;

                // Line of sight bonus (can we shoot back?)
                if (HasLineOfSight(tilePos, targetPos.Value))
                {
                    score += TacticalConstants.ScoreLOSWeight;
                }
            }

            return score;
        }

        // === BATCH SCORING ===

        /// <summary>
        /// Batch request paths to multiple destinations.
        /// Calls onAllComplete with sorted results (best first) when all paths are calculated.
        /// </summary>
        public static void ScoreDestinations(
            Vector3 from,
            List<Vector3> candidates,
            Vector3? target,
            Team team,
            Action<List<TileScore>> onAllComplete)
        {
            if (candidates == null || candidates.Count == 0)
            {
                onAllComplete?.Invoke(new List<TileScore>());
                return;
            }

            var results = new List<TileScore>();
            int pending = candidates.Count;

            foreach (var dest in candidates)
            {
                Vector3 capturedDest = dest; // Capture for closure

                RequestPath(from, capturedDest, team, (pathResult) =>
                {
                    var tileScore = new TileScore
                    {
                        Position = capturedDest,
                        Path = pathResult,
                        Distance = pathResult.IsValid ? pathResult.Distance : float.MaxValue,
                        ThreatCost = pathResult.IsValid ? pathResult.ThreatCost : float.MaxValue,
                        CoverQuality = target.HasValue
                            ? GetCoverQuality(capturedDest, (target.Value - capturedDest).normalized)
                            : 0f,
                        HasLOS = target.HasValue && HasLineOfSight(capturedDest, target.Value)
                    };

                    tileScore.TotalScore = ScoreTile(capturedDest, from, target, team, pathResult);
                    results.Add(tileScore);

                    pending--;
                    if (pending <= 0)
                    {
                        // Sort by score descending (best first)
                        results.Sort((a, b) => b.TotalScore.CompareTo(a.TotalScore));
                        onAllComplete?.Invoke(results);
                    }
                });
            }
        }

        // === TARGET SELECTION ===

        /// <summary>
        /// Find the closest enemy to a position.
        /// </summary>
        public static ITargetable FindClosestEnemy(Vector3 position, Team myTeam, float maxRange = float.MaxValue)
        {
            ITargetable closest = null;
            float closestDist = maxRange;

            var allTargets = GameObject.FindObjectsOfType<MonoBehaviour>();
            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == myTeam || targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                float dist = Vector3.Distance(position, targetable.Position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = targetable;
                }
            }

            return closest;
        }

        /// <summary>
        /// Find the closest visible enemy (has LOS).
        /// </summary>
        public static ITargetable FindClosestVisibleEnemy(Vector3 position, Team myTeam, float maxRange = float.MaxValue)
        {
            ITargetable closest = null;
            float closestDist = maxRange;

            var allTargets = GameObject.FindObjectsOfType<MonoBehaviour>();
            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == myTeam || targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                float dist = Vector3.Distance(position, targetable.Position);
                if (dist < closestDist && HasLineOfSight(position, targetable.Position))
                {
                    closestDist = dist;
                    closest = targetable;
                }
            }

            return closest;
        }

        /// <summary>
        /// Get all enemies within range.
        /// </summary>
        public static List<ITargetable> GetEnemiesInRange(Vector3 position, Team myTeam, float range)
        {
            var enemies = new List<ITargetable>();

            var allTargets = GameObject.FindObjectsOfType<MonoBehaviour>();
            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == myTeam || targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                if (Vector3.Distance(position, targetable.Position) <= range)
                {
                    enemies.Add(targetable);
                }
            }

            return enemies;
        }
    }
}
