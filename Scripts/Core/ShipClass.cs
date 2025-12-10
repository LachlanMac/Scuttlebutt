namespace Starbelter.Core
{
    /// <summary>
    /// Classification of ships by size and role.
    /// </summary>
    public enum ShipClass
    {
        Corvette,       // 15-20 crew, patrol/escort
        Frigate,        // 30-40 crew, light combat
        Destroyer,      // 60-80 crew, fleet escort
        Cruiser,        // 100-140 crew, independent ops
        Battleship      // 150-200 crew, capital ship
    }

    /// <summary>
    /// Extension methods for ShipClass.
    /// </summary>
    public static class ShipClassExtensions
    {
        public static int MinCrew(this ShipClass shipClass)
        {
            return shipClass switch
            {
                ShipClass.Corvette => 15,
                ShipClass.Frigate => 30,
                ShipClass.Destroyer => 60,
                ShipClass.Cruiser => 100,
                ShipClass.Battleship => 150,
                _ => 0
            };
        }

        public static int MaxCrew(this ShipClass shipClass)
        {
            return shipClass switch
            {
                ShipClass.Corvette => 20,
                ShipClass.Frigate => 40,
                ShipClass.Destroyer => 80,
                ShipClass.Cruiser => 140,
                ShipClass.Battleship => 200,
                _ => 0
            };
        }

        public static string DisplayName(this ShipClass shipClass)
        {
            return shipClass switch
            {
                ShipClass.Corvette => "Corvette",
                ShipClass.Frigate => "Frigate",
                ShipClass.Destroyer => "Destroyer",
                ShipClass.Cruiser => "Cruiser",
                ShipClass.Battleship => "Battleship",
                _ => "Unknown"
            };
        }
    }
}
