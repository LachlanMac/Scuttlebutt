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
    public class UnitController : MonoBehaviour
    {
        [Header("Team")]
        [SerializeField] private Team team = Team.Ally;

        [Header("Character")]
        [SerializeField] private Character character;

        [Header("References")]
        [SerializeField] private ThreatManager threatManager;

        // Components
        private UnitMovement movement;
        private UnitHealth unitHealth;
        private UnitStateMachine stateMachine;

        // Public accessors for states
        public Team Team => team;
        public Character Character => character;
        public UnitMovement Movement => movement;
        public UnitHealth Health => unitHealth;
        public ThreatManager ThreatManager => threatManager;

        /// <summary>
        /// Is the unit currently in cover from the highest threat direction?
        /// </summary>
        public bool IsInCover
        {
            get
            {
                var coverQuery = CoverQuery.Instance;
                if (coverQuery == null || threatManager == null) return false;

                var threatDir = threatManager.GetHighestThreatDirection();
                if (!threatDir.HasValue) return false;

                Vector3 threatWorldPos = transform.position + new Vector3(threatDir.Value.x, threatDir.Value.y, 0) * 10f;
                var coverCheck = coverQuery.CheckCoverAt(transform.position, threatWorldPos);
                return coverCheck.HasCover;
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
            }
        }

        private void OnDestroy()
        {
            if (unitHealth != null)
            {
                unitHealth.OnDamageTaken -= OnDamageTaken;
                unitHealth.OnFlanked -= OnFlanked;
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
            Debug.Log($"[UnitController] {name} flanked from {flankDirection}! Seeking new cover.");

            // Force seek new cover that protects from the flanking direction
            // Pass the flank direction so SeekCoverState can find appropriate cover
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
        }
#endif
    }
}
