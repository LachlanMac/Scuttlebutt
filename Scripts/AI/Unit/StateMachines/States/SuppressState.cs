using UnityEngine;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Unit moves to a position where it can fire at a target's cover, then fires rapidly.
    /// Fires 3x faster than normal with reduced accuracy.
    /// </summary>
    public class SuppressState : UnitState
    {
        private enum SuppressPhase
        {
            SeekingPosition,  // Moving to a spot where we can suppress
            Suppressing       // Actively firing
        }

        private SuppressPhase phase;
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

        // Position seeking
        private float positionRetryTimer;
        private const float POSITION_RETRY_INTERVAL = 0.5f;
        private float giveUpTimer;
        private const float GIVE_UP_TIME = 3f;

        public SuppressState(GameObject target)
        {
            suppressTarget = target;
        }

        public override void Enter()
        {
            if (suppressTarget == null)
            {
                ChangeState<CombatState>();
                return;
            }

            suppressPosition = suppressTarget.transform.position;

            // Check if we can already suppress from here
            if (CanSuppressTarget())
            {
                StartSuppressing();
            }
            else
            {
                // Need to find a position to suppress from
                phase = SuppressPhase.SeekingPosition;
                giveUpTimer = GIVE_UP_TIME;
                positionRetryTimer = 0f;
                FindSuppressPosition();
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

            if (phase == SuppressPhase.SeekingPosition)
            {
                UpdateSeekingPosition();
            }
            else
            {
                UpdateSuppressing();
            }
        }

        private void UpdateSeekingPosition()
        {
            // Check if we've arrived and can now suppress
            if (!Movement.IsMoving)
            {
                if (CanSuppressTarget())
                {
                    StartSuppressing();
                    return;
                }

                // Still can't suppress - retry finding position
                positionRetryTimer -= Time.deltaTime;
                if (positionRetryTimer <= 0f)
                {
                    positionRetryTimer = POSITION_RETRY_INTERVAL;
                    FindSuppressPosition();
                }
            }

            // Give up after a while
            giveUpTimer -= Time.deltaTime;
            if (giveUpTimer <= 0f)
            {
                // Couldn't find suppress position - go to combat instead
                ChangeState<CombatState>();
                return;
            }
        }

        private void UpdateSuppressing()
        {
            suppressDuration += Time.deltaTime;

            // Check if target is now exposed - take a real shot
            var los = CombatUtils.CheckLineOfSight(
                controller.FirePosition,
                suppressPosition
            );

            if (!los.IsBlocked)
            {
                var combatState = new CombatState(suppressTarget);
                stateMachine.ChangeState(combatState);
                return;
            }

            // Check if we can still suppress
            if (!CanSuppressTarget())
            {
                // Lost angle - find new position
                phase = SuppressPhase.SeekingPosition;
                giveUpTimer = GIVE_UP_TIME;
                positionRetryTimer = 0f;
                FindSuppressPosition();
                return;
            }

            // Stop suppressing if taking too much fire
            if (ThreatManager != null && ThreatManager.GetTotalThreat() > 5f)
            {
                ChangeState<SeekCoverState>();
                return;
            }

            // Max suppression time reached
            if (suppressDuration > MAX_SUPPRESS_TIME)
            {
                var flankState = new FlankState(suppressTarget);
                stateMachine.ChangeState(flankState);
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
            phase = SuppressPhase.Suppressing;
            fireTimer = 0f;
            suppressDuration = 0f;
            targetCheckTimer = TARGET_CHECK_INTERVAL;
        }

        private void FindSuppressPosition()
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null) return;

            // Find a position that has:
            // 1. Cover from the target direction
            // 2. Clear LOS to the target's cover (not blocked by our own cover)
            var result = coverQuery.FindSuppressPosition(
                controller.transform.position,
                suppressPosition,
                controller.WeaponRange,
                controller.gameObject
            );

            if (result.HasValue)
            {
                Movement.MoveToTile(result.Value);
            }
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
                projectile.Fire(finalDirection, controller.Team);
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
