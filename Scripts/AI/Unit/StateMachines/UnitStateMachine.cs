using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// State machine for unit AI. Manages state transitions and updates.
    /// </summary>
    public class UnitStateMachine
    {
        private UnitController controller;
        private UnitState currentState;

        public string CurrentStateName => currentState?.GetType().Name ?? "None";
        public UnitState CurrentState => currentState;

        public UnitStateMachine(UnitController controller)
        {
            this.controller = controller;

            // Start in Idle state by default
            ChangeState<IdleState>();
        }

        public void Update()
        {
            currentState?.Update();
        }

        /// <summary>
        /// Transition to a new state.
        /// </summary>
        public void ChangeState<T>() where T : UnitState, new()
        {
            // Exit current state
            currentState?.Exit();

            // Create and enter new state
            currentState = new T();
            currentState.Initialize(controller, this);
            currentState.Enter();

            Debug.Log($"[UnitStateMachine] {controller.name} → {CurrentStateName}");
        }

        /// <summary>
        /// Transition to a new state instance (for states with constructor params).
        /// </summary>
        public void ChangeState(UnitState newState)
        {
            // Exit current state
            currentState?.Exit();

            // Enter new state
            currentState = newState;
            currentState.Initialize(controller, this);
            currentState.Enter();

            Debug.Log($"[UnitStateMachine] {controller.name} → {CurrentStateName}");
        }
    }
}
