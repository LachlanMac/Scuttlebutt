using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Interface for any entity that can be targeted in combat.
    /// Implemented by UnitController, PlayerController, etc.
    /// </summary>
    public interface ITargetable
    {
        Team Team { get; }
        Transform Transform { get; }
        bool IsDead { get; }
    }
}
