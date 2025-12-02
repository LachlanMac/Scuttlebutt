namespace Starbelter.Core
{
    /// <summary>
    /// Faction allegiances in the Starbelter universe.
    /// Federation: The established democratic alliance - disciplined, defensive doctrine.
    /// Empire: The expansionist imperial forces - aggressive, overwhelming firepower.
    /// </summary>
    public enum Team
    {
        Neutral,
        Federation,  // Player faction (blue) - formerly "Ally"
        Empire       // Enemy faction (red) - formerly "Enemy"
    }
}
