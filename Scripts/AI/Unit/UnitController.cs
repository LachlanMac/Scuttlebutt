using UnityEngine;
using Starbelter.Core;
using Starbelter.Combat;
using Starbelter.Pathfinding;

namespace Starbelter.AI
{
    /// <summary>
    /// Main AI controller for a unit. Manages the state machine and provides
    /// access to unit components for states to use.
    /// </summary>
    [RequireComponent(typeof(UnitMovement))]
    public class UnitController : MonoBehaviour, Core.ITargetable
    {
        // ITargetable implementation
        Team Core.ITargetable.Team => team;
        public Transform Transform => transform;
        public bool IsDead => unitHealth != null && unitHealth.IsDead;

        [Header("Team")]
        [SerializeField] private Team team = Team.Ally;

        [Header("Character")]
        [SerializeField] private Character character;

        [Header("References")]
        [SerializeField] private ThreatManager threatManager;

        [Header("Weapon")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float weaponRange = 15f;

        [Header("Tactics")]
        [SerializeField] private Posture posture = Posture.Neutral;
        [Tooltip("Threat level above which unit becomes defensive")]
        [SerializeField] private float defensiveThreshold = 5f;

        // Components
        private UnitMovement movement;
        private UnitHealth unitHealth;
        private UnitStateMachine stateMachine;

        // Squad
        private SquadController squad;
        private bool isSquadLeader;

        // Public accessors for states
        public Team Team => team;
        public SquadController Squad => squad;
        public bool IsSquadLeader => isSquadLeader;
        public Character Character => character;
        public UnitMovement Movement => movement;
        public UnitHealth Health => unitHealth;
        public ThreatManager ThreatManager => threatManager;
        public GameObject ProjectilePrefab => projectilePrefab;
        public Transform FirePoint => firePoint;
        public float WeaponRange => weaponRange;

        /// <summary>
        /// Get effective posture, factoring in threat level, health, and leader status.
        /// Leaders play more defensively. High threat + low health = forced defensive.
        /// </summary>
        public Posture Posture
        {
            get
            {
                // If already set to defensive, stay defensive
                if (posture == Posture.Defensive) return Posture.Defensive;

                // Squad leaders are always at least neutral (never aggressive)
                if (isSquadLeader && posture == Posture.Aggressive)
                {
                    return Posture.Neutral;
                }

                // Calculate effective threat: threat * (1 + (1 - healthPercent))
                // At full health: threat * 1.0
                // At half health: threat * 1.5
                // At 10% health: threat * 1.9
                float effectiveThreat = GetEffectiveThreat();

                // Leaders become defensive at lower threat thresholds
                float threshold = isSquadLeader ? defensiveThreshold * 0.7f : defensiveThreshold;

                if (effectiveThreat > threshold)
                {
                    return Posture.Defensive;
                }

                return posture;
            }
        }

        /// <summary>
        /// Get threat level multiplied by health vulnerability.
        /// </summary>
        public float GetEffectiveThreat()
        {
            if (threatManager == null) return 0f;

            float baseThreat = threatManager.GetTotalThreat();
            float healthPercent = unitHealth != null ? unitHealth.HealthPercent : 1f;
            float healthMultiplier = 1f + (1f - healthPercent);

            return baseThreat * healthMultiplier;
        }

        // Suppression tracking
        private float suppressedUntil;
        private const float SUPPRESSION_DURATION = 1.5f;

        // Flank response tracking - prevent repeated reactions to flanking
        private float lastFlankResponseTime;
        private const float FLANK_RESPONSE_COOLDOWN = 3f;

        /// <summary>
        /// Is this unit currently being suppressed (projectiles hitting nearby cover)?
        /// </summary>
        public bool IsSuppressed => Time.time < suppressedUntil;

        /// <summary>
        /// Called when a projectile hits cover near this unit.
        /// </summary>
        public void ApplySuppression()
        {
            suppressedUntil = Time.time + SUPPRESSION_DURATION;
        }

        /// <summary>
        /// The actual position projectiles will fire from.
        /// Use this for LOS checks to ensure consistency with actual shots.
        /// </summary>
        public Vector3 FirePosition => firePoint != null ? firePoint.position : transform.position;

        /// <summary>
        /// Is the unit currently in cover from the highest threat direction?
        /// </summary>
        public bool IsInCover
        {
            get
            {
                var coverQuery = CoverQuery.Instance;
                if (coverQuery == null) return false;

                // Try threat direction first
                if (threatManager != null)
                {
                    var threatDir = threatManager.GetHighestThreatDirection();
                    if (threatDir.HasValue)
                    {
                        Vector3 threatWorldPos = transform.position + new Vector3(threatDir.Value.x, threatDir.Value.y, 0) * 10f;
                        var coverCheck = coverQuery.CheckCoverAt(transform.position, threatWorldPos);
                        return coverCheck.HasCover;
                    }
                }

                // No active threat direction - not in cover (need to find cover based on enemies)
                return false;
            }
        }

        /// <summary>
        /// Is the unit currently peeking (exposed while shooting)?
        /// Derived from CombatState phase.
        /// </summary>
        public bool IsPeeking
        {
            get
            {
                var combatState = stateMachine.CurrentState as CombatState;
                if (combatState == null) return false;

                var phase = combatState.CurrentPhase;
                return phase == CombatState.CombatPhase.Standing ||
                       phase == CombatState.CombatPhase.Aiming ||
                       phase == CombatState.CombatPhase.Shooting;
            }
        }

        private void Awake()
        {
            movement = GetComponent<UnitMovement>();
            unitHealth = GetComponentInChildren<UnitHealth>();

            // Auto-find ThreatManager in children if not assigned
            if (threatManager == null)
            {
                threatManager = GetComponentInChildren<ThreatManager>();
            }

            // Sync team to ThreatManager
            if (threatManager != null)
            {
                threatManager.MyTeam = team;
            }

            // Initialize default character if none assigned
            if (character == null)
            {
                character = new Character();
            }

            // Initialize state machine
            stateMachine = new UnitStateMachine(this);
        }

        private void Start()
        {
            // Subscribe to health events
            if (unitHealth != null)
            {
                unitHealth.OnDamageTaken += OnDamageTaken;
                unitHealth.OnFlanked += OnFlanked;
                unitHealth.OnDeath += OnDeath;
            }

            // Apply team color on start (for units placed in scene)
            UpdateTeamColor();
        }

        private void OnDestroy()
        {
            if (unitHealth != null)
            {
                unitHealth.OnDamageTaken -= OnDamageTaken;
                unitHealth.OnFlanked -= OnFlanked;
                unitHealth.OnDeath -= OnDeath;
            }
        }

        private void OnDeath()
        {
            // Notify squad if this was the leader
            if (isSquadLeader && squad != null)
            {
                squad.OnLeaderDied();
            }
        }

        private void Update()
        {
            stateMachine.Update();
        }

        private void OnDamageTaken(float damage)
        {
            // Notify combat state to interrupt shooting
            var combatState = stateMachine.CurrentState as CombatState;
            combatState?.OnDamageTaken();
        }

        private void OnFlanked(Vector2 flankDirection)
        {
            // Don't interrupt if already seeking cover
            if (stateMachine.CurrentState is SeekCoverState)
            {
                return;
            }

            // Don't react to flanking too frequently - prevents jittering
            if (Time.time - lastFlankResponseTime < FLANK_RESPONSE_COOLDOWN)
            {
                Debug.Log($"[{name}]  OnFlanked: Cooldown active, ignoring ({Time.time - lastFlankResponseTime:F1}s since last)");
                return;
            }
            lastFlankResponseTime = Time.time;

            Debug.Log($"[{name}] OnFlanked: Seeking cover from direction {flankDirection}");
            // Force seek new cover that protects from the flanking direction
            var seekCoverState = new SeekCoverState(flankDirection);
            stateMachine.ChangeState(seekCoverState);
        }

        /// <summary>
        /// Change the unit's team at runtime.
        /// </summary>
        public void SetTeam(Team newTeam)
        {
            team = newTeam;
            if (threatManager != null)
            {
                threatManager.MyTeam = newTeam;
            }
            UpdateTeamColor();
        }

        /// <summary>
        /// Assign this unit to a squad and optionally make it the leader.
        /// </summary>
        public void SetSquad(SquadController newSquad, bool isLeader = false)
        {
            squad = newSquad;
            isSquadLeader = isLeader;
        }

        /// <summary>
        /// Get the squad leader's position, or null if no squad/leader.
        /// </summary>
        public Vector3? GetLeaderPosition()
        {
            if (squad == null) return null;
            if (isSquadLeader) return null; // Leader doesn't follow itself
            return squad.GetLeaderPosition();
        }

        /// <summary>
        /// Get the squad rally point, or null if no squad.
        /// </summary>
        public Vector3? GetRallyPoint()
        {
            if (squad == null) return null;
            return squad.RallyPointPosition;
        }

        /// <summary>
        /// Updates the sprite color based on team.
        /// </summary>
        private void UpdateTeamColor()
        {
            var spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer == null) return;

            spriteRenderer.color = team switch
            {
                Team.Ally => new Color(0.5f, 0.7f, 1f),    // Light blue
                Team.Enemy => new Color(1f, 0.5f, 0.5f),  // Light red
                Team.Neutral => Color.white,
                _ => Color.white
            };
        }

        /// <summary>
        /// Assign a character to this unit (call before or during spawn).
        /// </summary>
        public void SetCharacter(Character newCharacter)
        {
            character = newCharacter;
        }

        /// <summary>
        /// Request a state transition.
        /// </summary>
        public void ChangeState<T>() where T : UnitState, new()
        {
            stateMachine.ChangeState<T>();
        }

        /// <summary>
        /// Get the current state type name (for debugging).
        /// </summary>
        public string GetCurrentStateName()
        {
            return stateMachine.CurrentStateName;
        }

        /// <summary>
        /// Command this unit to suppress a specific target.
        /// Called by SquadController for coordinated suppression.
        /// </summary>
        public void CommandSuppress(GameObject target)
        {
            if (target == null || IsDead) return;

            Debug.Log($"[{name}] CommandSuppress: Ordered to suppress {target.name}");
            var suppressState = new SuppressState(target);
            stateMachine.ChangeState(suppressState);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Show current state name above unit
            if (stateMachine != null)
            {
                UnityEditor.Handles.Label(
                    transform.position + Vector3.up * 1.5f,
                    $"State: {stateMachine.CurrentStateName}"
                );
            }

            // Draw threat directions from ThreatManager
            if (threatManager != null)
            {
                var threats = threatManager.GetActiveThreats(0.5f);
                foreach (var threat in threats)
                {
                    // Red line toward highest threat direction
                    Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
                    Vector3 threatWorldPos = transform.position + new Vector3(threat.Direction.x, threat.Direction.y, 0) * 5f;
                    Gizmos.DrawLine(transform.position, threatWorldPos);

                    // Arrow head
                    Vector3 arrowDir = (threatWorldPos - transform.position).normalized;
                    Vector3 arrowRight = Vector3.Cross(arrowDir, Vector3.forward) * 0.3f;
                    Gizmos.DrawLine(threatWorldPos, threatWorldPos - arrowDir * 0.5f + arrowRight);
                    Gizmos.DrawLine(threatWorldPos, threatWorldPos - arrowDir * 0.5f - arrowRight);

                    // Label threat level
                    UnityEditor.Handles.Label(threatWorldPos + Vector3.up * 0.3f, $"T:{threat.ThreatLevel:F1}");
                }
            }

            // Draw last cover search result if it was for this unit
            var (searchUnit, coverPos, threatPos, searchTime) = CoverQuery.GetLastSearchDebug();
            if (searchUnit == gameObject && Time.time - searchTime < 3f)
            {
                // Green line to chosen cover
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, coverPos);
                Gizmos.DrawWireCube(coverPos, Vector3.one * 0.6f);

                // Yellow line from cover to threat
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(coverPos, threatPos);

                // Label
                UnityEditor.Handles.Label(coverPos + Vector3.up * 0.5f, "Chosen Cover");
            }
        }
#endif
    }
}
