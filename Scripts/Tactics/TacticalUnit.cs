using UnityEngine;
using System.Collections.Generic;
using Pathfinding;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Tactics.States;

namespace Starbelter.Tactics
{
    /// <summary>
    /// Tactical AI brain for units.
    /// Manages state machine and exposes properties for states to use.
    /// Attach to any unit that needs tactical AI.
    /// </summary>
    [RequireComponent(typeof(Seeker))]
    public class TacticalUnit : MonoBehaviour
    {
        [Header("Team")]
        [SerializeField] private Team team = Team.Federation;

        [Header("Combat")]
        [SerializeField] private float effectiveRange = 12f;
        [SerializeField] private float fireRate = 1f;
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;

        // Components
        private Seeker seeker;
        private Rigidbody2D rb;

        // State machine
        private Dictionary<TacticalStateType, TacticalState> states;
        private TacticalState currentState;
        private TacticalStateType currentStateType;

        // Targeting
        private ITargetable currentTarget;

        // Movement
        private Vector3 destination;
        private List<Vector3> currentPath;
        private int pathIndex;
        private bool isMoving;
        private bool hasPendingDestination;

        // Combat
        private float lastFireTime;
        private float suppression;

        // Cover
        private bool isInCover;

        // Public properties for states
        public Vector3 Position => transform.position;
        public Team Team => team;
        public ITargetable CurrentTarget => currentTarget;
        public float EffectiveRange => effectiveRange;
        public float Suppression => suppression;
        public bool IsInCover => isInCover;
        public bool HasPendingDestination => hasPendingDestination;
        public bool HasArrivedAtDestination => isMoving && currentPath != null && pathIndex >= currentPath.Count;
        public bool CanShoot => Time.time - lastFireTime >= 1f / fireRate;
        public TacticalStateType CurrentStateType => currentStateType;

        private void Awake()
        {
            seeker = GetComponent<Seeker>();
            rb = GetComponent<Rigidbody2D>();

            InitializeStates();
        }

        private void Start()
        {
            ChangeState(TacticalStateType.Idle);
        }

        private void InitializeStates()
        {
            states = new Dictionary<TacticalStateType, TacticalState>
            {
                { TacticalStateType.Idle, new IdleState() },
                { TacticalStateType.Combat, new CombatState() },
                { TacticalStateType.Moving, new MovingState() },
                { TacticalStateType.Pinned, new PinnedState() }
            };
        }

        private void Update()
        {
            // Decay suppression
            if (suppression > 0)
            {
                suppression -= TacticalConstants.SuppressionDecay * Time.deltaTime;
                suppression = Mathf.Max(0, suppression);
            }

            // Update cover status
            UpdateCoverStatus();

            // Update current state
            currentState?.Update();

            // Handle movement
            if (isMoving)
            {
                FollowPath();
            }
        }

        // === STATE MANAGEMENT ===

        public void ChangeState(TacticalStateType newState)
        {
            if (currentState != null)
            {
                currentState.Exit();
            }

            currentStateType = newState;
            currentState = states[newState];
            currentState.Enter(this);
        }

        // === TARGETING ===

        public void SetTarget(ITargetable target)
        {
            currentTarget = target;
        }

        public void ClearTarget()
        {
            currentTarget = null;
        }

        // === MOVEMENT ===

        public void RequestCoverPosition()
        {
            Vector3? threatDir = currentTarget?.Position;
            Vector3 searchDir = threatDir.HasValue
                ? (threatDir.Value - Position).normalized
                : Vector3.zero;

            var coverPositions = TacticalQueries.FindCoverPositions(
                Position,
                TacticalConstants.CoverSearchRadius,
                searchDir);

            if (coverPositions.Count == 0)
            {
                hasPendingDestination = false;
                return;
            }

            // Score cover positions asynchronously
            TacticalQueries.ScoreDestinations(
                Position,
                coverPositions,
                threatDir,
                team,
                (scores) => OnCoverScored(scores));
        }

        private void OnCoverScored(List<TileScore> scores)
        {
            if (scores == null || scores.Count == 0)
            {
                hasPendingDestination = false;
                return;
            }

            // Best score is first (already sorted)
            destination = scores[0].Position;
            currentPath = scores[0].Path.Waypoints;
            pathIndex = 0;
            hasPendingDestination = true;
        }

        public void RequestAdvancePosition()
        {
            if (currentTarget == null)
            {
                hasPendingDestination = false;
                return;
            }

            // Move toward target, but try to stay in cover
            Vector3 targetPos = currentTarget.Position;
            Vector3 direction = (targetPos - Position).normalized;
            Vector3 advancePos = Position + direction * TacticalConstants.EngageRangeRifle * 0.5f;

            // Generate candidates around advance position
            var candidates = new List<Vector3>();
            for (float angle = 0; angle < 360; angle += 45)
            {
                float rad = angle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0) * 3f;
                candidates.Add(advancePos + offset);
            }
            candidates.Add(advancePos);

            TacticalQueries.ScoreDestinations(
                Position,
                candidates,
                targetPos,
                team,
                (scores) => OnAdvanceScored(scores));
        }

        private void OnAdvanceScored(List<TileScore> scores)
        {
            if (scores == null || scores.Count == 0)
            {
                hasPendingDestination = false;
                return;
            }

            destination = scores[0].Position;
            currentPath = scores[0].Path.Waypoints;
            pathIndex = 0;
            hasPendingDestination = true;
        }

        public void StartMoving()
        {
            if (!hasPendingDestination || currentPath == null)
            {
                // No path - get one
                var pathResult = TacticalQueries.GetPathBlocking(Position, destination, team);
                if (pathResult.IsValid)
                {
                    currentPath = pathResult.Waypoints;
                    pathIndex = 0;
                }
            }

            isMoving = true;
            hasPendingDestination = false;
        }

        public void StopMoving()
        {
            isMoving = false;
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
            }
        }

        public void RecalculatePath()
        {
            if (destination == Vector3.zero) return;

            var pathResult = TacticalQueries.GetPathBlocking(Position, destination, team);
            if (pathResult.IsValid)
            {
                currentPath = pathResult.Waypoints;
                pathIndex = 0;
            }
        }

        private void FollowPath()
        {
            if (currentPath == null || pathIndex >= currentPath.Count)
            {
                return;
            }

            Vector3 target = currentPath[pathIndex];
            Vector3 direction = (target - Position);
            float distance = direction.magnitude;

            if (distance < TacticalConstants.ArrivalThreshold)
            {
                pathIndex++;
                return;
            }

            // Move toward waypoint
            Vector2 moveDir = direction.normalized;
            if (rb != null)
            {
                rb.linearVelocity = moveDir * moveSpeed;
            }
            else
            {
                transform.position += (Vector3)(moveDir * moveSpeed * Time.deltaTime);
            }
        }

        // === COMBAT ===

        public void FireAtTarget()
        {
            if (currentTarget == null || projectilePrefab == null) return;

            Vector3 spawnPos = firePoint != null ? firePoint.position : Position;
            Vector2 direction = (currentTarget.Position - spawnPos).normalized;

            GameObject projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
            var projectile = projObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Fire(direction, team, gameObject);
            }

            lastFireTime = Time.time;
        }

        // === SUPPRESSION ===

        public void ApplySuppression(float amount = 20f)
        {
            suppression += amount;
            suppression = Mathf.Min(suppression, 100f);
        }

        // === COVER ===

        private void UpdateCoverStatus()
        {
            Vector3 threatDir = Vector3.zero;
            if (currentTarget != null)
            {
                threatDir = (currentTarget.Position - Position).normalized;
            }

            float coverQuality = TacticalQueries.GetCoverQuality(Position, threatDir);
            isInCover = coverQuality > 0f;
        }

        // === PUBLIC SETTERS ===

        public void SetTeam(Team newTeam)
        {
            team = newTeam;
        }

        public void SetMoveSpeed(float speed)
        {
            moveSpeed = speed;
        }

        public void SetEffectiveRange(float range)
        {
            effectiveRange = range;
        }

        public void SetFireRate(float rate)
        {
            fireRate = rate;
        }

        public void SetProjectilePrefab(GameObject prefab)
        {
            projectilePrefab = prefab;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw current path
            if (currentPath != null && currentPath.Count > 0)
            {
                Gizmos.color = Color.cyan;
                for (int i = pathIndex; i < currentPath.Count - 1; i++)
                {
                    Gizmos.DrawLine(currentPath[i], currentPath[i + 1]);
                }

                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(destination, 0.5f);
            }

            // Draw target line
            if (currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(Position, currentTarget.Position);
            }

            // Draw effective range
            Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
            Gizmos.DrawWireSphere(Position, effectiveRange);
        }
#endif
    }
}
