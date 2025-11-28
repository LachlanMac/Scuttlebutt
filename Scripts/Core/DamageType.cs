namespace Starbelter.Core
{
    /// <summary>
    /// Types of damage that can be dealt by weapons and projectiles.
    /// </summary>
    public enum DamageType
    {
        Physical,   // Kinetic rounds, shrapnel
        Heat,       // Fire, lasers, plasma
        Energy,     // Electricity, lightning
        Ion         // EMP, disruption (effective vs shields/electronics)
    }
}
