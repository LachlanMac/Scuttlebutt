namespace Starbelter.AI
{
    /// <summary>
    /// Base class for all unit states.
    /// </summary>
    public abstract class UnitState
    {
        protected UnitController controller;
        protected UnitStateMachine stateMachine;

        // Convenience accessors
        protected UnitMovement Movement => controller.Movement;
        protected Combat.ThreatManager ThreatManager => controller.ThreatManager;

        /// <summary>
        /// Called by the state machine to inject dependencies.
        /// </summary>
        public void Initialize(UnitController controller, UnitStateMachine stateMachine)
        {
            this.controller = controller;
            this.stateMachine = stateMachine;
        }

        /// <summary>
        /// Called when entering this state.
        /// </summary>
        public virtual void Enter() { }

        /// <summary>
        /// Called every frame while in this state.
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// Called when exiting this state.
        /// </summary>
        public virtual void Exit() { }

        /// <summary>
        /// Helper to transition to another state.
        /// </summary>
        protected void ChangeState<T>() where T : UnitState, new()
        {
            stateMachine.ChangeState<T>();
        }
    }
}
