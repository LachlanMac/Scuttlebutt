using UnityEngine;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Unit seeks cover from the highest threat direction or a specific flank direction.
    /// </summary>
    public class SeekCoverState : UnitState
    {
        private bool hasCoverTarget;
        private float searchCooldown;
        private const float SEARCH_INTERVAL = 0.5f; // Re-evaluate cover every 0.5s

        // Optional: specific direction to seek cover from (used when flanked)
        private Vector2? overrideThreatDirection;

        /// <summary>
        /// Default constructor - will use ThreatManager's highest threat direction.
        /// </summary>
        public SeekCoverState()
        {
            overrideThreatDirection = null;
        }

        /// <summary>
        /// Constructor with specific threat direction (used when flanked).
        /// </summary>
        public SeekCoverState(Vector2 flankDirection)
        {
            overrideThreatDirection = flankDirection;
        }

        public override void Enter()
        {
            hasCoverTarget = false;
            searchCooldown = 0f;
            FindAndMoveToCover();
        }

        public override void Update()
        {
            // If we've arrived at cover, transition to a cover state
            if (hasCoverTarget && !Movement.IsMoving)
            {
                // TODO: Transition to InCoverState when implemented
                // For now, go back to idle
                ChangeState<IdleState>();
                return;
            }

            // Periodically re-evaluate if we should find new cover
            searchCooldown -= Time.deltaTime;
            if (searchCooldown <= 0f)
            {
                searchCooldown = SEARCH_INTERVAL;

                // If threat direction changed significantly, find new cover
                if (ShouldReevaluateCover())
                {
                    FindAndMoveToCover();
                }
            }

            // If no longer under fire and not moving to cover, return to idle
            if (!hasCoverTarget && ThreatManager != null && !ThreatManager.IsUnderFire())
            {
                ChangeState<IdleState>();
            }
        }

        private void FindAndMoveToCover()
        {
            // Use override direction if set (flanking), otherwise get from ThreatManager
            Vector2? threatDir = overrideThreatDirection;

            if (!threatDir.HasValue)
            {
                if (ThreatManager == null)
                {
                    hasCoverTarget = false;
                    return;
                }

                threatDir = ThreatManager.GetHighestThreatDirection();
                if (!threatDir.HasValue)
                {
                    hasCoverTarget = false;
                    return;
                }
            }

            // Convert threat direction to a world position to seek cover from
            Vector3 unitPos = controller.transform.position;
            Vector3 threatWorldPos = unitPos + new Vector3(threatDir.Value.x, threatDir.Value.y, 0) * 10f;

            // Find cover that protects from this threat
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null)
            {
                Debug.LogWarning("[SeekCoverState] CoverQuery not available");
                hasCoverTarget = false;
                return;
            }

            var coverResult = coverQuery.FindBestCover(unitPos, threatWorldPos);

            if (coverResult.HasValue)
            {
                Movement.MoveToTile(coverResult.Value.TilePosition);
                hasCoverTarget = true;
                Debug.Log($"[SeekCoverState] {controller.name} seeking cover at {coverResult.Value.TilePosition} from threat {threatDir.Value}");
            }
            else
            {
                Debug.LogWarning($"[SeekCoverState] {controller.name} found no cover from threat {threatDir.Value}");
                hasCoverTarget = false;
            }
        }

        private bool ShouldReevaluateCover()
        {
            // Could add logic here to check if threat direction changed significantly
            // For now, just re-evaluate if we're not already moving to cover
            return !Movement.IsMoving && ThreatManager != null && ThreatManager.IsUnderFire();
        }
    }
}
