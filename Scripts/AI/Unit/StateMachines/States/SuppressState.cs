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
            if (suppressTarget == null)
            {
                Debug.Log($"[{controller.name}] SuppressState: Target is null, exiting");
                ChangeState<CombatState>();
                return;
            }
            if (!suppressTarget.activeInHierarchy)
            {
                Debug.Log($"[{controller.name}] SuppressState: Target inactive, exiting");
                ChangeState<CombatState>();
                return;
            }
            if (IsSuppressTargetDead())
            {
                Debug.Log($"[{controller.name}] SuppressState: Target is dead, exiting");
                ChangeState<CombatState>();
                return;
            }

            suppressPosition = suppressTarget.transform.position;
            UpdateSuppressing();
        }

        private void UpdateSuppressing()
        {
            suppressDuration += Time.deltaTime;

            // Update suppress position to track target movement
            if (suppressTarget != null)
            {
                suppressPosition = suppressTarget.transform.position;
            }

            // Check if target is now exposed - but only react if they were in full cover before
            // Don't exit just because we started suppressing a target in half cover
            // Also require they stay exposed for a moment (not just peeking)
            var los = CombatUtils.CheckLineOfSight(
                controller.FirePosition,
                suppressPosition
            );

            if (targetWasInFullCover && !los.IsBlocked)
            {
                // Target WAS in full cover but is now exposed
                // Only switch if we've been suppressing for at least a moment
                // (prevents instant exit due to LOS check timing)
                if (suppressDuration > 0.5f)
                {
                    Debug.Log($"[{controller.name}] SuppressState: Target exposed from full cover, switching to combat");
                    var combatState = new CombatState(suppressTarget, immediate: false);
                    stateMachine.ChangeState(combatState);
                    return;
                }
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
            if (PerceptionManager != null && PerceptionManager.GetTotalThreat() > CombatUtils.SUPPRESSION_ABORT_THREAT)
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

            // Don't check for other exposed targets during suppression
            // The whole point is to stay focused on keeping the target pinned
            // We only exit if: target dies, target exposes from full cover, we take too much fire, or max time reached

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
            return CombatUtils.IsTargetDead(suppressTarget);
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

            // Much wider spread for suppression (3x normal)
            float baseSpread = 5f * Mathf.Deg2Rad;
            float suppressSpread = baseSpread * SUPPRESS_SPREAD_MULTIPLIER;

            var shootParams = new CombatUtils.ShootParams
            {
                FirePosition = controller.FirePosition,
                TargetPosition = suppressPosition,
                SpreadRadians = suppressSpread,
                Team = controller.Team,
                SourceUnit = controller.gameObject,
                ProjectilePrefab = controller.ProjectilePrefab
            };

            var projectile = CombatUtils.ShootProjectile(shootParams);

            // DEBUG: Purple projectile for suppression shots
            if (projectile != null)
            {
                var sr = projectile.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(0.7f, 0.2f, 1f); // Purple
            }
        }

        private GameObject FindExposedTarget()
        {
            return CombatUtils.FindExposedTarget(
                controller.FirePosition,
                controller.WeaponRange,
                controller.Team,
                controller.transform,
                suppressTarget  // Exclude current suppress target
            );
        }
    }
}
