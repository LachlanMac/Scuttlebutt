using UnityEngine;
using System.Linq;
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

        /// <summary>
        /// Scans for enemies and registers them as threats without transitioning states.
        /// </summary>
        private void ScanForThreats()
        {
            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            float weaponRange = controller.WeaponRange;

            foreach (var target in allTargets)
            {
                if (target.Transform == controller.transform) continue;
                if (target.Team == controller.Team) continue;
                if (controller.Team == Team.Neutral) continue;
                if (target.IsDead) continue;

                float distance = Vector2.Distance(controller.transform.position, target.Transform.position);
                if (distance <= weaponRange && ThreatManager != null)
                {
                    ThreatManager.RegisterVisibleEnemy(target.Transform.position, 1f);
                }
            }
        }

        public override void Update()
        {
            // Priority 1: React to incoming fire - seek cover if exposed
            if (ThreatManager != null && ThreatManager.IsUnderFire())
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

                GameObject target = FindTarget();
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

            Vector2? threatDir = ThreatManager.GetHighestThreatDirection();
            if (!threatDir.HasValue) return false;

            // Convert threat direction to world position
            Vector3 unitPos = controller.transform.position;
            Vector3 threatWorldPos = unitPos + new Vector3(threatDir.Value.x, threatDir.Value.y, 0) * 10f;

            // Check if current position has cover against the threat
            var coverCheck = coverQuery.CheckCoverAt(unitPos, threatWorldPos);
            return coverCheck.HasCover;
        }

        private GameObject FindTarget()
        {
            // Find best enemy target using priority scoring
            ITargetable[] allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .OfType<ITargetable>()
                .ToArray();

            float weaponRange = controller.WeaponRange;
            float bestPriority = 0f;
            GameObject bestTarget = null;

            foreach (var target in allTargets)
            {
                // Skip self
                if (target.Transform == controller.transform) continue;

                // Skip same team (and neutrals don't fight)
                if (target.Team == controller.Team) continue;
                if (controller.Team == Team.Neutral) continue;

                // Skip dead targets
                if (target.IsDead) continue;

                float distance = Vector2.Distance(controller.transform.position, target.Transform.position);

                // Register visible enemies within weapon range as threats
                if (distance <= weaponRange && ThreatManager != null)
                {
                    ThreatManager.RegisterVisibleEnemy(target.Transform.position, 1f);
                }

                // Calculate priority based on distance and cover
                float priority = CombatUtils.CalculateTargetPriority(
                    controller.transform.position,
                    target.Transform.position,
                    weaponRange
                );

                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestTarget = target.Transform.gameObject;
                }
            }

            return bestTarget;
        }
    }
}
