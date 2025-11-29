namespace Starbelter.Core
{
    /// <summary>
    /// Combat posture affecting tactical decisions like cover selection and engagement range.
    /// </summary>
    public enum Posture
    {
        Defensive,  // Prefer full cover, stay at range
        Neutral,    // Let personality (bravery) decide
        Aggressive  // Accept half cover, push closer
    }
}
