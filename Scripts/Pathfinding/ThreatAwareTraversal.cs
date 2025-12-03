using Pathfinding;
using Starbelter.Combat;
using Starbelter.Core;
using UnityEngine;

namespace Starbelter.Pathfinding
{
    /// <summary>
    /// Placeholder for threat-aware pathfinding.
    /// TODO: Implement using newer A* Pathfinding Pro API (traversalConstraint/traversalCosts).
    /// For now, threat is factored into destination selection in CombatUtils.FindFightingPosition.
    /// </summary>
    public static class ThreatAwareTraversal
    {
        /// <summary>
        /// Get threat cost for a position. Can be used for path post-processing.
        /// </summary>
        public static float GetThreatCost(Vector3 position, Team team)
        {
            if (TileThreatMap.Instance == null) return 0f;
            return TileThreatMap.Instance.GetThreatAtWorld(position, team) * 500f;
        }
    }
}
