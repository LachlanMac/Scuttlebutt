using UnityEngine;

namespace Starbelter.AI
{
    /// <summary>
    /// Base class for unit states. Simple and focused.
    /// </summary>
    public abstract class UnitState
    {
        protected UnitController controller;
        protected float stateEnterTime;

        // Convenience accessors (only use after IsValid check)
        protected UnitMovement Movement => controller.Movement;
        protected Vector3 Position => controller.transform.position;

        /// <summary>
        /// Check if controller is still valid (not destroyed).
        /// Call this at the start of Update before doing anything.
        /// </summary>
        protected bool IsValid => controller != null && !controller.IsDead;

        /// <summary>
        /// Called to inject the controller reference.
        /// </summary>
        public void Initialize(UnitController controller)
        {
            this.controller = controller;
        }

        /// <summary>
        /// Called when entering this state.
        /// </summary>
        public virtual void Enter()
        {
            stateEnterTime = Time.time;
        }

        /// <summary>
        /// Called every frame while in this state.
        /// </summary>
        public abstract void Update();

        /// <summary>
        /// Called when exiting this state.
        /// </summary>
        public virtual void Exit() { }

        /// <summary>
        /// Time spent in current state.
        /// </summary>
        protected float TimeInState => Time.time - stateEnterTime;

        /// <summary>
        /// Check if minimum state time has passed.
        /// </summary>
        protected bool CanTransition => controller != null && controller.CanTransition;
    }
}
