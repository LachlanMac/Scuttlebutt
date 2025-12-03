using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// Pinned state - suppressed and unable to act effectively.
    /// Waits for suppression to decay before recovering.
    /// </summary>
    public class PinnedState : UnitState
    {
        private const float SUPPRESSION_PIN_THRESHOLD = 80f;

        public override void Enter()
        {
            base.Enter();
            controller.InterruptMovement();
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Check if we can recover (suppression dropped to half threshold)
            if (controller.Suppression < SUPPRESSION_PIN_THRESHOLD * 0.5f)
            {
                Recover();
            }
        }

        private void Recover()
        {
            // Check for threats
            var enemy = controller.FindClosestVisibleEnemy(controller.WeaponRange);

            if (enemy != null)
            {
                controller.SetTarget(enemy);
                controller.ChangeState(UnitStateType.Combat);
            }
            else if (controller.IsInDanger())
            {
                // Still in danger, find cover
                controller.RequestCoverPosition();
                if (controller.HasPendingDestination)
                {
                    controller.ChangeState(UnitStateType.Moving);
                }
                else
                {
                    controller.ChangeState(UnitStateType.Ready);
                }
            }
            else
            {
                controller.ChangeState(UnitStateType.Ready);
            }
        }
    }
}
