using UnityEngine;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Default idle state. Unit stands still, monitors for threats, and engages enemies.
    /// </summary>
    public class IdleState : UnitState
    {
        private float targetScanTimer;
        private const float TARGET_SCAN_INTERVAL = 0.5f;

        public override void Enter()
        {
            // Stop any movement
            Movement.Stop();

            // Scan immediately on enter to register any visible threats
            targetScanTimer = 0f;
            ScanForThreats();
        }

        private void ScanForThreats()
        {
            CombatUtils.ScanAndRegisterThreats(
                controller.transform.position,
                controller.WeaponRange,
                controller.Team,
                controller.transform,
                PerceptionManager
            );
        }

        public override void Update()
        {
            // Priority 1: React to incoming fire - seek cover if exposed
            if (PerceptionManager != null && PerceptionManager.IsUnderFire())
            {
                if (!IsInCoverFromThreat())
                {
                    ChangeState<SeekCoverState>();
                    return;
                }
            }

            // Priority 2: Look for enemies to engage
            targetScanTimer -= Time.deltaTime;
            if (targetScanTimer <= 0f)
            {
                targetScanTimer = TARGET_SCAN_INTERVAL;

                // Use perception system first (prefer perceived enemies)
                // Fall back to direct detection if no perceived enemies
                GameObject target = null;
                if (PerceptionManager != null && PerceptionManager.HasPerceivedEnemies())
                {
                    target = PerceptionManager.GetClosestVisibleEnemy();
                }

                // Fall back to direct detection if perception found nothing
                if (target == null)
                {
                    target = FindTarget();
                }

                if (target != null)
                {
                    // Enemy found - enter combat
                    ChangeState<CombatState>();
                    return;
                }
            }
        }

        private bool IsInCoverFromThreat()
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null) return false;

            Vector2? threatDir = PerceptionManager.GetHighestThreatDirection();
            if (!threatDir.HasValue) return false;

            Vector3 unitPos = controller.transform.position;
            Vector3 threatWorldPos = CombatUtils.ThreatDirectionToWorldPos(unitPos, threatDir.Value);

            var coverCheck = coverQuery.CheckCoverAt(unitPos, threatWorldPos);
            return coverCheck.HasCover;
        }

        private GameObject FindTarget()
        {
            return CombatUtils.FindBestTarget(
                controller.transform.position,
                controller.WeaponRange,
                controller.Team,
                controller.transform,
                PerceptionManager
            );
        }
    }
}
