using UnityEngine;

namespace Starbelter.Tactics
{
    /// <summary>
    /// Base class for tactical unit states.
    /// Simple and focused - each state handles one behavior.
    /// </summary>
    public abstract class TacticalState
    {
        protected TacticalUnit unit;
        protected float stateEnterTime;

        /// <summary>
        /// Called when entering this state.
        /// </summary>
        public virtual void Enter(TacticalUnit unit)
        {
            this.unit = unit;
            this.stateEnterTime = Time.time;
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
        /// Check if minimum state time has passed (prevents flicker).
        /// </summary>
        protected bool CanTransition => TimeInState >= TacticalConstants.MinStateTime;
    }

    /// <summary>
    /// The four core states a tactical unit can be in.
    /// </summary>
    public enum TacticalStateType
    {
        Idle,       // No threats, holding position
        Combat,     // Engaged with enemy, shooting
        Moving,     // Relocating to new position
        Pinned      // Suppressed, can't act
    }
}
