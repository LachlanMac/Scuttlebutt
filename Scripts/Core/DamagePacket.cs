using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Data package containing all information about a damage event.
    /// </summary>
    public struct DamagePacket
    {
        public float Damage;
        public DamageType Type;
        public Vector2 HitPoint;
        public Vector2 Origin;
        public GameObject Source;

        public DamagePacket(float damage, DamageType type, Vector2 hitPoint, Vector2 origin, GameObject source)
        {
            Damage = damage;
            Type = type;
            HitPoint = hitPoint;
            Origin = origin;
            Source = source;

        }
    }
}
