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
        private float giveUpTimer;
        private const float SEARCH_INTERVAL = 0.5f;
        private const float GIVE_UP_TIME = 2f;

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
            giveUpTimer = GIVE_UP_TIME;

            // If already moving to cover, just wait for arrival
            if (Movement.IsMoving)
            {
                hasCoverTarget = true;
                return;
            }

            // Try to find and move to cover
            bool startedMoving = FindAndMoveToCover();

            // If we didn't start moving, we're probably already at cover
            if (!startedMoving && controller.IsInCover)
            {
                var combatState = new CombatState(alreadyAtCover: true);
                stateMachine.ChangeState(combatState);
            }
        }

        public override void Update()
        {
            // If we've arrived at cover, transition to combat
            if (hasCoverTarget && !Movement.IsMoving)
            {
                var combatState = new CombatState(alreadyAtCover: true);
                stateMachine.ChangeState(combatState);
                return;
            }

            // Periodically re-evaluate if we should find new cover
            searchCooldown -= Time.deltaTime;
            if (searchCooldown <= 0f)
            {
                searchCooldown = SEARCH_INTERVAL;

                // Try to find cover if we don't have a target yet
                if (!hasCoverTarget)
                {
                    FindAndMoveToCover();
                }
                // Or if threat direction changed significantly
                else if (ShouldReevaluateCover())
                {
                    FindAndMoveToCover();
                }
            }

            // Give up timer - don't immediately bail, wait for path throttle to clear
            if (!hasCoverTarget)
            {
                giveUpTimer -= Time.deltaTime;
                if (giveUpTimer <= 0f)
                {
                    // Couldn't find cover after waiting, go to combat anyway (fight in the open)
                    var combatState = new CombatState(alreadyAtCover: true); // Prevent re-seeking
                    stateMachine.ChangeState(combatState);
                }
            }
        }

        /// <summary>
        /// Find and move to cover. Returns true if movement was started.
        /// </summary>
        private bool FindAndMoveToCover()
        {
            // Use override direction if set (flanking), otherwise get from ThreatManager
            Vector2? threatDir = overrideThreatDirection;

            if (!threatDir.HasValue)
            {
                if (ThreatManager == null)
                {
                    hasCoverTarget = false;
                    return false;
                }

                threatDir = ThreatManager.GetHighestThreatDirection();
                if (!threatDir.HasValue)
                {
                    hasCoverTarget = false;
                    return false;
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
                return false;
            }

            // Use tactical search with posture
            int bravery = controller.Character?.Bravery ?? 10;
            var leaderPos = controller.GetLeaderPosition();
            var searchParams = CoverSearchParams.FromPosture(controller.WeaponRange, controller.Posture, bravery, controller.Team, leaderPos);

            var coverResult = coverQuery.FindBestCover(unitPos, threatWorldPos, searchParams, -1f, controller.gameObject);

            if (coverResult.HasValue)
            {
                // Only set hasCoverTarget if movement actually started
                hasCoverTarget = Movement.MoveToTile(coverResult.Value.TilePosition);
                return hasCoverTarget;
            }
            else
            {
                hasCoverTarget = false;
                return false;
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
