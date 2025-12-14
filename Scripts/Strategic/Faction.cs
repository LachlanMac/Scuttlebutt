using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Faction identifier enum.
    /// </summary>
    public enum FactionId
    {
        None,           // Unaligned/neutral
        Empire,         // Zulradden Empire - religious monarchy
        Federation,     // Yorennian Federation - democratic states
        Consortium,     // Trade Consortium - corporate power
        Pirate,         // Pirate Clans - raiders
        Independent,    // Free Colonies - unbound
        Alien           // Pu'luuk - mysterious organic aliens
    }

    /// <summary>
    /// A faction in the galaxy. Controls territory, builds infrastructure, fields ships.
    /// </summary>
    [System.Serializable]
    public class Faction
    {
        [Header("Identity")]
        public FactionId id;
        public string displayName;
        public string shortName;            // 3-4 letter abbreviation
        public string description;
        public Color factionColor;

        [Header("Characteristics")]
        public FactionType factionType;
        public bool hasStations = true;     // Pu'luuk have no stations
        public bool hasCivilians = true;    // Does this faction have civilian ships/population?

        [Header("Diplomacy")]
        public List<FactionId> atWarWith = new List<FactionId>();
        public List<FactionId> alliedWith = new List<FactionId>();

        public Faction(FactionId id, string name, string shortName, Color color)
        {
            this.id = id;
            this.displayName = name;
            this.shortName = shortName;
            this.factionColor = color;
        }

        public bool IsHostileTo(Faction other)
        {
            if (other == null) return false;
            return atWarWith.Contains(other.id);
        }

        public bool IsAlliedWith(Faction other)
        {
            if (other == null) return false;
            return alliedWith.Contains(other.id) || other.id == id;
        }

        public FactionRelation GetRelationTo(Faction other)
        {
            if (other == null) return FactionRelation.Neutral;
            if (other.id == id) return FactionRelation.Self;
            if (alliedWith.Contains(other.id)) return FactionRelation.Allied;
            if (atWarWith.Contains(other.id)) return FactionRelation.Hostile;
            return FactionRelation.Neutral;
        }
    }

    public enum FactionType
    {
        Government,     // Empire, Federation
        Corporate,      // Consortium
        Criminal,       // Pirates
        Independent,    // Free Colonies
        Alien           // Pu'luuk
    }

    public enum FactionRelation
    {
        Self,       // Same faction
        Allied,     // Friendly, will assist
        Neutral,    // No conflict
        Hostile     // At war
    }

    /// <summary>
    /// Static registry of all factions.
    /// </summary>
    public static class Factions
    {
        private static Dictionary<FactionId, Faction> factions = new Dictionary<FactionId, Faction>();
        private static bool initialized = false;

        // Quick access to factions
        public static Faction Empire { get; private set; }
        public static Faction Federation { get; private set; }
        public static Faction Consortium { get; private set; }
        public static Faction Pirate { get; private set; }
        public static Faction Independent { get; private set; }
        public static Faction Alien { get; private set; }

        public static FactionId PlayerFactionId { get; set; } = FactionId.Empire;

        public static void Initialize()
        {
            if (initialized) return;

            // Zulradden Empire - Religious monarchy, strict but fair
            // Player's faction (default)
            Empire = new Faction(
                FactionId.Empire,
                "Zulradden Empire",
                "ZE",
                new Color(0.8f, 0.2f, 0.2f)  // Deep red
            )
            {
                description = "A religious monarchy bound by ancient traditions. Strict laws but not tyrannical. Honor and faith guide their actions.",
                factionType = FactionType.Government,
                hasStations = true,
                hasCivilians = true
            };
            Register(Empire);

            // Yorennian Federation - Democratic collection of states
            Federation = new Faction(
                FactionId.Federation,
                "Yorennian Federation",
                "YF",
                new Color(0.2f, 0.5f, 1f)   // Blue
            )
            {
                description = "A democratic federation of independent states united by common values. Bureaucratic but free.",
                factionType = FactionType.Government,
                hasStations = true,
                hasCivilians = true
            };
            Register(Federation);

            // Trade Consortium - Corporate powerhouse
            Consortium = new Faction(
                FactionId.Consortium,
                "Trade Consortium",
                "TC",
                new Color(1f, 0.8f, 0.2f)   // Gold
            )
            {
                description = "A coalition of megacorporations. Profit is their only loyalty. They trade with everyone and ally with no one.",
                factionType = FactionType.Corporate,
                hasStations = true,
                hasCivilians = true
            };
            Register(Consortium);

            // Pirate Clans - Raiders and criminals
            Pirate = new Faction(
                FactionId.Pirate,
                "Pirate Clans",
                "PC",
                new Color(0.6f, 0.3f, 0.6f)  // Purple
            )
            {
                description = "A loose alliance of raiders, smugglers, and outcasts. They prey on shipping lanes and hide in the shadows.",
                factionType = FactionType.Criminal,
                hasStations = true,  // Hidden bases
                hasCivilians = false // No civilian population
            };
            Register(Pirate);

            // Free Colonies - Independent systems
            Independent = new Faction(
                FactionId.Independent,
                "Free Colonies",
                "FC",
                new Color(0.2f, 0.8f, 0.2f)  // Green
            )
            {
                description = "Independent worlds and stations bound to no one. They value freedom above all else.",
                factionType = FactionType.Independent,
                hasStations = true,
                hasCivilians = true
            };
            Register(Independent);

            // Pu'luuk - Mysterious organic aliens
            Alien = new Faction(
                FactionId.Alien,
                "Pu'luuk",
                "PL",
                new Color(0.4f, 0.1f, 0.4f)  // Dark purple/organic
            )
            {
                description = "Mysterious organic beings from beyond known space. Their caste-based society and living ships defy understanding.",
                factionType = FactionType.Alien,
                hasStations = false, // No stations - only ships
                hasCivilians = false // Alien biology, no "civilians"
            };
            Register(Alien);

            // Set up diplomacy
            SetupDiplomacy();

            initialized = true;
            Debug.Log("[Factions] Initialized 6 factions");
        }

        private static void SetupDiplomacy()
        {
            // Empire vs Federation - The main war
            Empire.atWarWith.Add(FactionId.Federation);
            Federation.atWarWith.Add(FactionId.Empire);

            // Pirates hostile to everyone (except each other)
            Pirate.atWarWith.Add(FactionId.Empire);
            Pirate.atWarWith.Add(FactionId.Federation);
            Pirate.atWarWith.Add(FactionId.Consortium);
            Pirate.atWarWith.Add(FactionId.Independent);
            // Everyone vs Pirates
            Empire.atWarWith.Add(FactionId.Pirate);
            Federation.atWarWith.Add(FactionId.Pirate);
            Consortium.atWarWith.Add(FactionId.Pirate);
            Independent.atWarWith.Add(FactionId.Pirate);

            // Pu'luuk - hostile to all humans
            Alien.atWarWith.Add(FactionId.Empire);
            Alien.atWarWith.Add(FactionId.Federation);
            Alien.atWarWith.Add(FactionId.Consortium);
            Alien.atWarWith.Add(FactionId.Pirate);
            Alien.atWarWith.Add(FactionId.Independent);
            // Everyone vs Aliens
            Empire.atWarWith.Add(FactionId.Alien);
            Federation.atWarWith.Add(FactionId.Alien);
            Consortium.atWarWith.Add(FactionId.Alien);
            Pirate.atWarWith.Add(FactionId.Alien);
            Independent.atWarWith.Add(FactionId.Alien);

            // Consortium is neutral with Empire and Federation (trades with both)
            // (no entries means neutral)

            // Independent is neutral with everyone except pirates/aliens
            // (already handled above)
        }

        public static void Register(Faction faction)
        {
            factions[faction.id] = faction;
        }

        public static Faction Get(FactionId id)
        {
            Initialize();
            return factions.TryGetValue(id, out var faction) ? faction : null;
        }

        public static Faction GetPlayerFaction()
        {
            Initialize();
            return Get(PlayerFactionId);
        }

        public static IEnumerable<Faction> GetAll()
        {
            Initialize();
            return factions.Values;
        }

        /// <summary>
        /// Get all factions that have stations (for infrastructure generation).
        /// </summary>
        public static IEnumerable<Faction> GetStationBuildingFactions()
        {
            Initialize();
            foreach (var faction in factions.Values)
            {
                if (faction.hasStations)
                    yield return faction;
            }
        }
    }
}
