using UnityEngine;
using System.Collections.Generic;
using Starbelter.Core;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Configuration data for a faction loaded from JSON.
    /// Defines how many stations/ships of each type to spawn.
    /// </summary>
    [System.Serializable]
    public class FactionConfig
    {
        public string factionId;
        public string displayName;

        public MilitaryConfig military;
        public EconomicConfig economic;
        public ScienceConfig science;
        public CivilianConfig civilian;
        public ShipConfig ships;
        public ExpansionConfig expansion;

        // Pirates only
        public int pirateHavens;

        [System.Serializable]
        public class MilitaryConfig
        {
            public int fleetHQ;
            public int bastions;
            public int bases;
            public int outposts;
            public int militaryShipyards;
            public int listeningPosts;

            public int TotalStations => fleetHQ + bastions + bases + outposts + militaryShipyards + listeningPosts;
        }

        [System.Serializable]
        public class EconomicConfig
        {
            public int miningStations;
            public int industrialStations;
            public int commercialHubs;
            public int civilianShipyards;

            public int TotalStations => miningStations + industrialStations + commercialHubs + civilianShipyards;
        }

        [System.Serializable]
        public class ScienceConfig
        {
            public int researchStations;
            public int observatories;

            public int TotalStations => researchStations + observatories;
        }

        [System.Serializable]
        public class CivilianConfig
        {
            public int orbitalHabitats;
            public int spaceports;

            public int TotalStations => orbitalHabitats + spaceports;
        }

        [System.Serializable]
        public class ShipConfig
        {
            public int battleships;
            public int cruisers;
            public int destroyers;
            public int frigates;
            public int corvettes;

            public int TotalShips => battleships + cruisers + destroyers + frigates + corvettes;
        }

        [System.Serializable]
        public class ExpansionConfig
        {
            public int[] homeSector;  // [x, y] coordinates
            public string expansionPattern; // "radial", "scattered", "parasitic", "random"
            public float aggressiveness;    // 0.0 to 1.0

            public Vector2Int HomeSectorCoord => homeSector != null && homeSector.Length >= 2
                ? new Vector2Int(homeSector[0], homeSector[1])
                : Vector2Int.zero;
        }

        /// <summary>
        /// Get total station count across all categories.
        /// </summary>
        public int TotalStations
        {
            get
            {
                int total = pirateHavens;
                if (military != null) total += military.TotalStations;
                if (economic != null) total += economic.TotalStations;
                if (science != null) total += science.TotalStations;
                if (civilian != null) total += civilian.TotalStations;
                return total;
            }
        }

        /// <summary>
        /// Get a flat list of (StationType, count) for iteration.
        /// </summary>
        public IEnumerable<(StationType type, int count)> GetAllStationCounts()
        {
            if (military != null)
            {
                if (military.fleetHQ > 0) yield return (StationType.FleetHQ, military.fleetHQ);
                if (military.bastions > 0) yield return (StationType.Bastion, military.bastions);
                if (military.bases > 0) yield return (StationType.Base, military.bases);
                if (military.outposts > 0) yield return (StationType.Outpost, military.outposts);
                if (military.militaryShipyards > 0) yield return (StationType.MilitaryShipyard, military.militaryShipyards);
                if (military.listeningPosts > 0) yield return (StationType.ListeningPost, military.listeningPosts);
            }

            if (economic != null)
            {
                if (economic.miningStations > 0) yield return (StationType.MiningStation, economic.miningStations);
                if (economic.industrialStations > 0) yield return (StationType.IndustrialStation, economic.industrialStations);
                if (economic.commercialHubs > 0) yield return (StationType.CommercialHub, economic.commercialHubs);
                if (economic.civilianShipyards > 0) yield return (StationType.CivilianShipyard, economic.civilianShipyards);
            }

            if (science != null)
            {
                if (science.researchStations > 0) yield return (StationType.ResearchStation, science.researchStations);
                if (science.observatories > 0) yield return (StationType.Observatory, science.observatories);
            }

            if (civilian != null)
            {
                if (civilian.orbitalHabitats > 0) yield return (StationType.OrbitalHabitat, civilian.orbitalHabitats);
                if (civilian.spaceports > 0) yield return (StationType.Spaceport, civilian.spaceports);
            }

            if (pirateHavens > 0) yield return (StationType.PirateHaven, pirateHavens);
        }

        /// <summary>
        /// Get a flat list of (ShipClass, count) for iteration.
        /// </summary>
        public IEnumerable<(ShipClass shipClass, int count)> GetAllShipCounts()
        {
            if (ships == null) yield break;

            if (ships.battleships > 0) yield return (ShipClass.Battleship, ships.battleships);
            if (ships.cruisers > 0) yield return (ShipClass.Cruiser, ships.cruisers);
            if (ships.destroyers > 0) yield return (ShipClass.Destroyer, ships.destroyers);
            if (ships.frigates > 0) yield return (ShipClass.Frigate, ships.frigates);
            if (ships.corvettes > 0) yield return (ShipClass.Corvette, ships.corvettes);
        }
    }

    /// <summary>
    /// Loads faction configurations from JSON files.
    /// </summary>
    public static class FactionConfigLoader
    {
        private static Dictionary<FactionId, FactionConfig> configs;
        private static bool isLoaded = false;

        public static FactionConfig GetConfig(FactionId faction)
        {
            EnsureLoaded();
            return configs.TryGetValue(faction, out var config) ? config : null;
        }

        public static IEnumerable<FactionConfig> AllConfigs
        {
            get
            {
                EnsureLoaded();
                return configs.Values;
            }
        }

        public static void Reload()
        {
            isLoaded = false;
            configs = null;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (isLoaded) return;
            LoadAllConfigs();
        }

        private static void LoadAllConfigs()
        {
            configs = new Dictionary<FactionId, FactionConfig>();

            string[] factionNames = { "Empire", "Federation", "Consortium", "Pirate", "Independent" };

            foreach (var name in factionNames)
            {
                var textAsset = Resources.Load<TextAsset>($"Data/Factions/{name}");
                if (textAsset == null)
                {
                    Debug.LogWarning($"[FactionConfigLoader] Could not load config for {name}");
                    continue;
                }

                var config = JsonUtility.FromJson<FactionConfig>(textAsset.text);
                if (config != null && System.Enum.TryParse<FactionId>(config.factionId, out var factionId))
                {
                    configs[factionId] = config;
                    Debug.Log($"[FactionConfigLoader] Loaded {name}: {config.TotalStations} stations, {config.ships?.TotalShips ?? 0} ships");
                }
            }

            isLoaded = true;
            Debug.Log($"[FactionConfigLoader] Loaded {configs.Count} faction configs");
        }
    }
}
