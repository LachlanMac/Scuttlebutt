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
            // Don't update if unit is dead
            if (controller.IsDead) return;

            currentState?.Update();
        }

        /// <summary>
        /// Transition to a new state.
        /// </summary>
        public void ChangeState<T>() where T : UnitState, new()
        {
            // Don't change state if unit is dead
            if (controller.IsDead) return;

            // Exit current state
            currentState?.Exit();

            // Create and enter new state
            currentState = new T();
            currentState.Initialize(controller, this);
            currentState.Enter();
        }

        /// <summary>
        /// Transition to a new state instance (for states with constructor params).
        /// </summary>
        public void ChangeState(UnitState newState)
        {
            // Don't change state if unit is dead
            if (controller.IsDead) return;

            // Exit current state
            currentState?.Exit();

            // Enter new state
            currentState = newState;
            currentState.Initialize(controller, this);
            currentState.Enter();
        }
    }
}
