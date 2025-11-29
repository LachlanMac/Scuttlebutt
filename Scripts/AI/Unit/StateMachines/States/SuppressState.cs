using UnityEngine;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Unit fires rapidly at a target's cover to keep them pinned.
    /// Only suppresses from current position - does NOT move.
    /// Fires 3x faster than normal with reduced accuracy.
    /// </summary>
    public class SuppressState : UnitState
    {
        private GameObject suppressTarget;
        private Vector2 suppressPosition;

        // Fire rate (3x faster than normal combat)
        private float fireTimer;
        private const float SUPPRESS_FIRE_INTERVAL = 0.33f; // ~3 shots per second

        // Accuracy penalty
        private const float SUPPRESS_SPREAD_MULTIPLIER = 3f;

        // Duration limits
        private float suppressDuration;
        private const float MAX_SUPPRESS_TIME = 5f;

        // Check for better options periodically
        private float targetCheckTimer;
        private const float TARGET_CHECK_INTERVAL = 0.5f;

        // Track if target was in full cover when we started
        // Only switch to combat if they were hidden and now exposed
        private bool targetWasInFullCover;

        public SuppressState(GameObject target)
        {
            suppressTarget = target;
        }

        public override void Enter()
        {
            if (suppressTarget == null)
            {
                Debug.Log($"[{controller.name}] SuppressState: No target, exiting");
                ChangeState<CombatState>();
                return;
            }

            suppressPosition = suppressTarget.transform.position;

            // Check if target is currently in full cover
            var los = CombatUtils.CheckLineOfSight(controller.FirePosition, suppressPosition);
            targetWasInFullCover = los.IsBlocked;

            // Only suppress if we can do it from current position
            // Moving to suppress is flanking behavior, not suppression
            if (CanSuppressTarget())
            {
                Debug.Log($"[{controller.name}] SuppressState: Can suppress from current position, starting (target in full cover: {targetWasInFullCover})");
                StartSuppressing();
            }
            else
            {
                // Can't suppress from here - go to overwatch instead
                Debug.Log($"[{controller.name}] SuppressState: Can't suppress from here, going to overwatch");
                var overwatchState = new OverwatchState(suppressTarget, fromFailedSuppression: true);
                stateMachine.ChangeState(overwatchState);
            }
        }

        public override void Update()
        {
            // Check if target is still valid
            if (suppressTarget == null || !suppressTarget.activeInHierarchy || IsSuppressTargetDead())
            {
                ChangeState<CombatState>();
                return;
            }

            suppressPosition = suppressTarget.transform.position;
            UpdateSuppressing();
        }

        private void UpdateSuppressing()
        {
            suppressDuration += Time.deltaTime;

            // Check if target is now exposed - but only react if they were in full cover before
            // Don't exit just because we started suppressing a target in half cover
            var los = CombatUtils.CheckLineOfSight(
                controller.FirePosition,
                suppressPosition
            );

            if (targetWasInFullCover && !los.IsBlocked)
            {
                // Target WAS in full cover but is now exposed - take a real shot!
                Debug.Log($"[{controller.name}] SuppressState: Target exposed from full cover, switching to combat");
                var combatState = new CombatState(suppressTarget);
                stateMachine.ChangeState(combatState);
                return;
            }

            // Check if we can still suppress (own cover might now block)
            if (!CanSuppressTarget())
            {
                // Lost angle - go to overwatch
                var overwatchState = new OverwatchState(suppressTarget, fromFailedSuppression: true);
                stateMachine.ChangeState(overwatchState);
                return;
            }

            // Stop suppressing if taking too much fire
            if (ThreatManager != null && ThreatManager.GetTotalThreat() > 30f)
            {
                ChangeState<SeekCoverState>();
                return;
            }

            // Max suppression time reached - go to overwatch
            if (suppressDuration > MAX_SUPPRESS_TIME)
            {
                var overwatchState = new OverwatchState(suppressTarget);
                stateMachine.ChangeState(overwatchState);
                return;
            }

            // Periodically check for exposed targets
            targetCheckTimer -= Time.deltaTime;
            if (targetCheckTimer <= 0f)
            {
                targetCheckTimer = TARGET_CHECK_INTERVAL;

                var exposedTarget = FindExposedTarget();
                if (exposedTarget != null)
                {
                    var combatState = new CombatState(exposedTarget);
                    stateMachine.ChangeState(combatState);
                    return;
                }
            }

            // Fire suppressive shots
            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f)
            {
                FireSuppressiveShot();
                fireTimer = SUPPRESS_FIRE_INTERVAL;
            }
        }

        private void StartSuppressing()
        {
            fireTimer = 0f;
            suppressDuration = 0f;
            targetCheckTimer = TARGET_CHECK_INTERVAL;
        }

        private bool IsSuppressTargetDead()
        {
            if (suppressTarget == null) return true;
            var targetable = suppressTarget.GetComponent<ITargetable>();
            return targetable != null && targetable.IsDead;
        }

        /// <summary>
        /// Check if we can actually shoot toward the target's cover.
        /// Returns false if our own cover or other obstacles block the path completely.
        /// </summary>
        private bool CanSuppressTarget()
        {
            if (suppressTarget == null) return false;

            Vector3 firePos = controller.FirePosition;
            Vector2 targetPos = suppressTarget.transform.position;

            // Do a raycast toward target - we need to hit THEIR cover, not ours
            Vector2 direction = (targetPos - (Vector2)firePos).normalized;
            float distance = Vector2.Distance(firePos, targetPos);

            // RaycastAll to find what's between us and target
            RaycastHit2D[] hits = Physics2D.RaycastAll(firePos, direction, distance);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.distance < 0.1f) continue; // Skip very close hits (own collider)

                var structure = hit.collider.GetComponent<Structure>();
                if (structure != null && structure.CoverType == CoverType.Full)
                {
                    // Found full cover - is it near us (blocking) or near target (their cover)?
                    float distToHit = hit.distance;
                    float distToTarget = distance;

                    // If the cover is in the first 30% of the distance, it's blocking us
                    // If it's in the last 70%, it's their cover (suppression target)
                    if (distToHit < distToTarget * 0.3f)
                    {
                        // Full cover too close to us - we're blocked
                        return false;
                    }
                    else
                    {
                        // Cover is near target - this is what we want to suppress
                        return true;
                    }
                }
            }

            // No full cover found - path is clear (target might be exposed or behind half cover)
            return true;
        }

        private void FireSuppressiveShot()
        {
            if (controller.ProjectilePrefab == null) return;

            Vector3 firePos = controller.FirePosition;
            Vector2 baseDirection = (suppressPosition - (Vector2)firePos).normalized;

            // Much wider spread for suppression
            float baseSpread = 5f * Mathf.Deg2Rad;
            float spread = baseSpread * SUPPRESS_SPREAD_MULTIPLIER;

            float angle = Mathf.Atan2(baseDirection.y, baseDirection.x);
            angle += Random.Range(-spread, spread);
            Vector2 finalDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            GameObject projectileObj = Object.Instantiate(
                controller.ProjectilePrefab,
                firePos,
                Quaternion.identity
            );

            var projectile = projectileObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Fire(finalDirection, controller.Team, controller.gameObject);
            }
            else
            {
                Object.Destroy(projectileObj);
            }
        }

        private GameObject FindExposedTarget()
        {
            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Transform == controller.transform) continue;
                if (targetable.Team == controller.Team) continue;
                if (targetable.IsDead) continue;

                // Skip the target we're currently suppressing - don't switch off for them
                if (suppressTarget != null && targetable.Transform.gameObject == suppressTarget) continue;

                float dist = Vector3.Distance(controller.transform.position, targetable.Transform.position);
                if (dist > controller.WeaponRange) continue;

                var los = CombatUtils.CheckLineOfSight(
                    controller.FirePosition,
                    targetable.Transform.position
                );

                if (!los.IsBlocked)
                {
                    return targetable.Transform.gameObject;
                }
            }

            return null;
        }
    }
}
