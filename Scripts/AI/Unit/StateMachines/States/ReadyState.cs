using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// Ready state - no immediate threats, holding position with high alertness.
    /// Scans for enemies and transitions to Combat when found.
    /// Units in Ready state get 1.5x perception bonus.
    /// </summary>
    public class ReadyState : UnitState
    {
        private float lastScanTime;
        private float lastDangerCheckTime;
        private const float SCAN_INTERVAL = 0.5f;
        private const float DANGER_CHECK_INTERVAL = 2f;

        public override void Enter()
        {
            base.Enter();
            lastScanTime = 0f;
            lastDangerCheckTime = 0f;

            // Debug: why are we entering Ready?
            float suppression = controller.Suppression;
            float threat = controller.GetThreatAtPosition(controller.transform.position);
            var enemy = controller.FindClosestVisibleEnemy(controller.PerceptionRange);
            Debug.Log($"[{controller.name}] Entering READY - Suppression={suppression:F0}, Threat={threat:F1}, VisibleEnemy={enemy != null}");
        }

        public override void Update()
        {
            if (!IsValid) return;

            // Good time to reload if needed - no threats around
            if (controller.NeedsReload || controller.ShouldTacticalReload)
            {
                controller.ChangeState(UnitStateType.Reloading);
                return;
            }

            // Periodic scan for enemies
            if (Time.time - lastScanTime >= SCAN_INTERVAL)
            {
                lastScanTime = Time.time;
                ScanForThreats();
            }

            // Check if we're in a dangerous tile (with cooldown to prevent spam)
            if (Time.time - lastDangerCheckTime >= DANGER_CHECK_INTERVAL)
            {
                if (controller.IsInDanger())
                {
                    lastDangerCheckTime = Time.time;
                    controller.RequestCoverPosition();
                    if (controller.HasPendingDestination)
                    {
                        controller.ChangeState(UnitStateType.Moving);
                    }
                }
            }
        }

        private void ScanForThreats()
        {
            // Scan at perception range, not weapon range - you can see farther than you can shoot
            var enemy = controller.FindClosestVisibleEnemy(controller.PerceptionRange);

            if (enemy != null)
            {
                // Alert squad about first contact
                controller.AlertSquadFirstContact(enemy.Position);

                controller.SetTarget(enemy);

                // CRITICAL: If not in cover, find a fighting position first!
                // Don't just stand in the open and start shooting
                if (!controller.IsInCover)
                {
                    controller.RequestFightingPosition();
                    // Enter combat - async callback will trigger Moving if position found
                    controller.ChangeState(UnitStateType.Combat);
                    return;
                }

                // We're in cover - check if we can engage from here or need to advance
                float distance = Vector3.Distance(Position, enemy.Position);
                if (distance <= controller.WeaponRange)
                {
                    controller.ChangeState(UnitStateType.Combat);
                }
                else
                {
                    // Enemy spotted but out of range - find fighting position
                    // NOTE: RequestFightingPosition is ASYNC - it will trigger state change via callback
                    controller.RequestFightingPosition();
                    // Enter combat now; if a better position is found, callback will switch to Moving
                    controller.ChangeState(UnitStateType.Combat);
                }
            }
            else if (controller.Squad != null && controller.Squad.HasBeenEngaged)
            {
                // Squad is engaged but we can't see anyone - find a fighting position
                // NOTE: RequestFightingPosition is ASYNC - callback will trigger Moving if position found
                controller.RequestFightingPosition();
            }
        }
    }
}
