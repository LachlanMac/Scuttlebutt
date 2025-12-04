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
            controller.SetDucked(true); // Cowering behind cover
        }

        public override void Exit()
        {
            base.Exit();
            controller.SetDucked(false); // Standing back up
        }

        private float debugLogTimer;

        public override void Update()
        {
            if (!IsValid) return;

            // Debug: log suppression and threat every 2 seconds
            debugLogTimer += Time.deltaTime;
            if (debugLogTimer >= 2f)
            {
                debugLogTimer = 0f;
                float threat = controller.GetThreatAtPosition(controller.transform.position);
                Debug.Log($"[{controller.name}] PINNED - Suppression: {controller.Suppression:F1}, Threat: {threat:F1}");
            }

            // Check if we can recover (suppression dropped to half threshold)
            if (controller.Suppression < SUPPRESSION_PIN_THRESHOLD * 0.5f)
            {
                Recover();
            }
        }

        private void Recover()
        {
            // Check for threats at weapon range
            var enemy = controller.FindClosestVisibleEnemy(controller.WeaponRange);

            Debug.Log($"[{controller.name}] Recovering from PINNED - VisibleEnemy={enemy != null}, IsInDanger={controller.IsInDanger()}");

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
                    Debug.Log($"[{controller.name}] No cover found, going to Ready despite danger");
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
