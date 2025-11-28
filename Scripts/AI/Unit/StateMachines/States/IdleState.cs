using UnityEngine;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Default idle state. Unit stands still and monitors for threats.
    /// </summary>
    public class IdleState : UnitState
    {
        public override void Enter()
        {
            // Stop any movement
            Movement.Stop();
        }

        public override void Update()
        {
            // Check for threats
            if (ThreatManager != null && ThreatManager.IsUnderFire())
            {
                // Check if we're already in cover from the threat
                if (IsInCoverFromThreat())
                {
                    // We're safe, stay idle
                    return;
                }

                ChangeState<SeekCoverState>();
            }
        }

        private bool IsInCoverFromThreat()
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null) return false;

            Vector2? threatDir = ThreatManager.GetHighestThreatDirection();
            if (!threatDir.HasValue) return false;

            // Convert threat direction to world position
            Vector3 unitPos = controller.transform.position;
            Vector3 threatWorldPos = unitPos + new Vector3(threatDir.Value.x, threatDir.Value.y, 0) * 10f;

            // Check if current position has cover against the threat
            var coverCheck = coverQuery.CheckCoverAt(unitPos, threatWorldPos);
            return coverCheck.HasCover;
        }
    }
}
