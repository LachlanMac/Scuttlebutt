using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Aggressive advance to cover while laying down suppressive fire.
    /// Unit moves at half speed but fires continuously to create space.
    /// Used when units need to push forward under fire rather than retreat.
    /// </summary>
    public class AdvanceState : UnitState
    {
        // Movement
        private bool hasDestination;
        private float originalSpeed;
        private const float ADVANCE_SPEED_MULTIPLIER = 0.5f;

        // Suppressive fire while moving
        private float fireTimer;
        private const float ADVANCE_FIRE_INTERVAL = 0.5f; // Slower than suppress, faster than combat
        private const float ADVANCE_SPREAD_MULTIPLIER = 2.5f; // Less accurate while moving

        // Target tracking
        private GameObject fireTarget;
        private float targetCheckTimer;
        private const float TARGET_CHECK_INTERVAL = 0.3f;

        // Safety limits
        private float advanceDuration;
        private const float MAX_ADVANCE_TIME = 6f;

        // Cover search
        private float coverSearchTimer;
        private const float COVER_SEARCH_INTERVAL = 0.5f;
        private float giveUpTimer;
        private const float GIVE_UP_TIME = 3f;

        public AdvanceState()
        {
        }

        public override void Enter()
        {
            hasDestination = false;
            advanceDuration = 0f;
            fireTimer = 0f;
            targetCheckTimer = 0f;
            coverSearchTimer = 0f;
            giveUpTimer = GIVE_UP_TIME;

            // Slow down movement
            originalSpeed = Movement.MoveSpeed;
            Movement.MoveSpeed = originalSpeed * ADVANCE_SPEED_MULTIPLIER;

            // Find initial target
            FindFireTarget();

            // Start moving to cover
            if (!TryFindAdvanceCover())
            {
                // No cover found immediately, will retry in Update
                Debug.Log($"[{controller.name}] AdvanceState: No immediate cover, will search while moving");
            }
        }

        public override void Exit()
        {
            // Restore original speed
            Movement.MoveSpeed = originalSpeed;
        }

        public override void Update()
        {
            advanceDuration += Time.deltaTime;

            // Safety: max advance time
            if (advanceDuration > MAX_ADVANCE_TIME)
            {
                Debug.Log($"[{controller.name}] AdvanceState: Max time reached, transitioning");
                TransitionToCombat();
                return;
            }

            // Check if we've arrived at cover
            if (hasDestination && !Movement.IsMoving)
            {
                // Successfully advanced to cover - clear failure counter
                int unitId = controller.gameObject.GetInstanceID();
                CombatState.ClearCoverSeekFailures(unitId);

                Debug.Log($"[{controller.name}] AdvanceState: Arrived at cover");
                TransitionToCombat();
                return;
            }

            // If no destination yet, keep searching
            if (!hasDestination)
            {
                giveUpTimer -= Time.deltaTime;
                if (giveUpTimer <= 0f)
                {
                    // Advance also failed to find cover - record failure
                    int unitId = controller.gameObject.GetInstanceID();
                    CombatState.RecordCoverSeekFailure(unitId);

                    Debug.Log($"[{controller.name}] AdvanceState: Gave up finding cover");
                    TransitionToCombat();
                    return;
                }

                coverSearchTimer -= Time.deltaTime;
                if (coverSearchTimer <= 0f)
                {
                    coverSearchTimer = COVER_SEARCH_INTERVAL;
                    TryFindAdvanceCover();
                }
            }

            // Update fire target periodically
            targetCheckTimer -= Time.deltaTime;
            if (targetCheckTimer <= 0f)
            {
                targetCheckTimer = TARGET_CHECK_INTERVAL;
                FindFireTarget();
            }

            // Fire suppressive shots while advancing
            fireTimer -= Time.deltaTime;
            if (fireTimer <= 0f && fireTarget != null)
            {
                FireAdvancingShot();
                fireTimer = ADVANCE_FIRE_INTERVAL;
            }

            // Face toward target while moving
            if (fireTarget != null)
            {
                Movement.FaceToward(fireTarget.transform.position);
            }
        }

        /// <summary>
        /// Find cover to advance toward. Prefers cover that's FORWARD (toward enemies).
        /// </summary>
        private bool TryFindAdvanceCover()
        {
            var coverQuery = CoverQuery.Instance;
            if (coverQuery == null) return false;

            // Get threat direction - we want to advance TOWARD threats, not away
            Vector2? threatDir = null;
            if (PerceptionManager != null)
            {
                threatDir = PerceptionManager.GetHighestThreatDirection();
            }

            // If no threat, try to find nearest enemy
            if (!threatDir.HasValue)
            {
                var target = CombatUtils.FindBestTarget(
                    controller.transform.position,
                    controller.WeaponRange * 2f, // Search further
                    controller.Team,
                    controller.transform,
                    PerceptionManager
                );

                if (target != null)
                {
                    threatDir = ((Vector2)(target.transform.position - controller.transform.position)).normalized;
                }
            }

            if (!threatDir.HasValue)
            {
                return false;
            }

            Vector3 unitPos = controller.transform.position;
            Vector3 threatWorldPos = CombatUtils.ThreatDirectionToWorldPos(unitPos, threatDir.Value);

            // Use aggressive search params - prefer forward positions
            int bravery = controller.Character?.Bravery ?? 10;
            var searchParams = CoverSearchParams.FromPosture(
                controller.WeaponRange,
                Posture.Aggressive, // Always aggressive for advance
                bravery,
                controller.Team,
                controller.GetLeaderPosition(),
                controller.GetRallyPoint(),
                controller.IsSquadLeader
            );

            // Advancing = Fighting mode (must have LOS to enemies)
            var knownEnemies = GetKnownEnemies();
            searchParams = searchParams.WithMode(CoverMode.Fighting, knownEnemies);

            // Find cover - allow further search for advance
            var coverResult = coverQuery.FindBestCover(
                unitPos,
                threatWorldPos,
                searchParams,
                15f, // Allow further cover search
                controller.gameObject
            );

            if (coverResult.HasValue)
            {
                hasDestination = Movement.MoveToTile(coverResult.Value.TilePosition);
                if (hasDestination)
                {
                    Debug.Log($"[{controller.name}] AdvanceState: Moving to cover at {coverResult.Value.TilePosition}");
                }
                return hasDestination;
            }

            return false;
        }

        private void FindFireTarget()
        {
            // Find any visible enemy to shoot at while advancing
            fireTarget = CombatUtils.FindBestTarget(
                controller.transform.position,
                controller.WeaponRange,
                controller.Team,
                controller.transform,
                PerceptionManager
            );
        }

        private void FireAdvancingShot()
        {
            if (controller.ProjectilePrefab == null)
            {
                Debug.Log($"[{controller.name}] AdvanceState: No projectile prefab!");
                return;
            }
            if (fireTarget == null)
            {
                Debug.Log($"[{controller.name}] AdvanceState: No fire target found");
                return;
            }

            // Check we have some LOS (at least half cover, not full blocked)
            var los = CombatUtils.CheckLineOfSight(
                controller.FirePosition,
                fireTarget.transform.position
            );

            // Don't waste ammo on fully blocked targets
            if (los.IsBlocked)
            {
                Debug.Log($"[{controller.name}] AdvanceState: LOS blocked to {fireTarget.name}");
                return;
            }

            // Wider spread while moving
            int accuracy = controller.Character?.Accuracy ?? 10;
            float baseSpread = CombatUtils.CalculateAccuracySpread(accuracy);
            float movingSpread = baseSpread * ADVANCE_SPREAD_MULTIPLIER;

            var shootParams = new CombatUtils.ShootParams
            {
                FirePosition = controller.FirePosition,
                TargetPosition = fireTarget.transform.position,
                SpreadRadians = movingSpread,
                Team = controller.Team,
                SourceUnit = controller.gameObject,
                ProjectilePrefab = controller.ProjectilePrefab
            };

            var projectile = CombatUtils.ShootProjectile(shootParams);
            Debug.Log($"[{controller.name}] AdvanceState: FIRING at {fireTarget.name} while advancing!");

            // DEBUG: Purple projectile for advancing suppressive fire
            if (projectile != null)
            {
                var sr = projectile.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(0.7f, 0.2f, 1f); // Purple
            }
        }

        private void TransitionToCombat()
        {
            // Check if we're actually in cover now
            bool inCover = controller.IsInCover;
            var combatState = new CombatState(alreadyAtCover: inCover);
            stateMachine.ChangeState(combatState);
        }

        /// <summary>
        /// Get list of known enemy GameObjects within weapon range.
        /// Used for LOS checking in cover scoring.
        /// </summary>
        private List<GameObject> GetKnownEnemies()
        {
            var enemies = new List<GameObject>();
            var allTargets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

            foreach (var mb in allTargets)
            {
                var targetable = mb as ITargetable;
                if (targetable == null) continue;
                if (targetable.Team == controller.Team) continue;
                if (targetable.Team == Team.Neutral) continue;
                if (targetable.IsDead) continue;

                float dist = Vector2.Distance(controller.transform.position, targetable.Transform.position);
                if (dist <= controller.WeaponRange)
                {
                    enemies.Add(targetable.Transform.gameObject);
                }
            }

            return enemies;
        }
    }
}
