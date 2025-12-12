using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Interface for any projectile, missile, or weapon that can deal damage in space.
    /// </summary>
    public interface ISpaceWeapon
    {
        float Damage { get; }
        DamageType DamageType { get; }
        Vector2 Origin { get; }
    }
}
