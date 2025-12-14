using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Manages faction infrastructure placement across the galaxy.
    /// Places stations in orbit around planets/moons based on faction configs.
    /// </summary>
    public static class FactionManager
    {
        private static System.Random rng;

        /// <summary>
        /// Place all faction infrastructure across the galaxy.
        /// Call this after natural POIs are generated.
        /// </summary>
        public static void PlaceAllFactionInfrastructure(GalaxyData galaxy, int seed)
        {
            rng = new System.Random(seed);

            // Clear tracking from any previous generation
            sectorOrbitalTrackers.Clear();
            sectorStationPositions.Clear();

            foreach (var config in FactionConfigLoader.AllConfigs)
            {
                if (!System.Enum.TryParse<FactionId>(config.factionId, out var factionId))
                    continue;

                Debug.Log($"[FactionManager] Placing infrastructure for {config.displayName}...");
                PlaceFactionInfrastructure(galaxy, factionId, config);
            }
        }

        private static void PlaceFactionInfrastructure(GalaxyData galaxy, FactionId factionId, FactionConfig config)
        {
            // Get sectors this faction can place stations in, sorted by distance from home
            var homeSector = config.expansion?.HomeSectorCoord ?? Vector2Int.zero;
            var availableSectors = GetFactionSectors(galaxy, factionId, config, homeSector);

            if (availableSectors.Count == 0)
            {
                Debug.LogWarning($"[FactionManager] No available sectors for {factionId}");
                return;
            }

            // Place stations by priority (military first in home/core, economic spread out)
            int stationsPlaced = 0;

            foreach (var (stationType, count) in config.GetAllStationCounts())
            {
                var targetSectors = GetTargetSectorsForStationType(availableSectors, stationType, homeSector);
                stationsPlaced += PlaceStationsOfType(galaxy, factionId, stationType, count, targetSectors);
            }

            Debug.Log($"[FactionManager] {factionId}: Placed {stationsPlaced} stations");
        }

        /// <summary>
        /// Get sectors available for a faction based on expansion pattern.
        /// </summary>
        private static List<Sector> GetFactionSectors(GalaxyData galaxy, FactionId factionId, FactionConfig config, Vector2Int homeSector)
        {
            var sectors = new List<Sector>();
            string pattern = config.expansion?.expansionPattern ?? "radial";

            // Calculate max expansion radius based on station count
            int maxRadius = Mathf.CeilToInt(Mathf.Sqrt(config.TotalStations / 5f)) + 1;

            for (int x = 0; x < GalaxyData.GALAXY_SIZE; x++)
            {
                for (int y = 0; y < GalaxyData.GALAXY_SIZE; y++)
                {
                    var sector = galaxy.GetSector(x, y);
                    if (sector == null) continue;

                    int distance = GalaxyData.SectorDistance(homeSector, new SectorCoord(x, y));

                    switch (pattern)
                    {
                        case "radial":
                            // Expand outward from home
                            if (distance <= maxRadius)
                                sectors.Add(sector);
                            break;

                        case "scattered":
                            // Consortium - trade routes, more spread out
                            if (distance <= maxRadius * 2 && rng.NextDouble() < 0.6)
                                sectors.Add(sector);
                            break;

                        case "parasitic":
                            // Pirates - spawn near other factions, not in empty space
                            if (sector.AllPOIs.Count > 2)
                                sectors.Add(sector);
                            break;

                        case "random":
                            // Free colonies - random placement
                            if (rng.NextDouble() < 0.3)
                                sectors.Add(sector);
                            break;
                    }
                }
            }

            // Sort by distance from home (closest first for military, furthest first for economic)
            sectors.Sort((a, b) =>
            {
                int distA = GalaxyData.SectorDistance(homeSector, a.galaxyCoord);
                int distB = GalaxyData.SectorDistance(homeSector, b.galaxyCoord);
                return distA.CompareTo(distB);
            });

            return sectors;
        }

        /// <summary>
        /// Get target sectors for a specific station type.
        /// Military stays close to home, economic spreads out.
        /// </summary>
        private static List<Sector> GetTargetSectorsForStationType(List<Sector> availableSectors, StationType stationType, Vector2Int homeSector)
        {
            if (availableSectors.Count == 0) return availableSectors;

            // Military stays in core sectors (first half), economic in outer (second half)
            bool isMilitary = stationType switch
            {
                StationType.FleetHQ => true,
                StationType.Bastion => true,
                StationType.Base => true,
                StationType.Outpost => true,
                StationType.MilitaryShipyard => true,
                StationType.ListeningPost => true,
                _ => false
            };

            if (isMilitary)
            {
                // FleetHQ goes in home sector only
                if (stationType == StationType.FleetHQ)
                {
                    return availableSectors.Where(s => s.galaxyCoord == homeSector).ToList();
                }

                // Other military in closer sectors
                int militaryLimit = Mathf.Max(1, availableSectors.Count / 2);
                return availableSectors.Take(militaryLimit).ToList();
            }

            // Economic/civilian spread throughout
            return availableSectors;
        }

        // Per-sector orbital trackers and placed station positions
        private static Dictionary<string, SectorOrbitalTracker> sectorOrbitalTrackers = new Dictionary<string, SectorOrbitalTracker>();
        private static Dictionary<string, List<Vector2>> sectorStationPositions = new Dictionary<string, List<Vector2>>();
        private const float MIN_STATION_DISTANCE = 500f; // Minimum distance between any two stations

        /// <summary>
        /// Place a number of stations of a given type across sectors.
        /// </summary>
        private static int PlaceStationsOfType(GalaxyData galaxy, FactionId factionId, StationType stationType, int count, List<Sector> targetSectors)
        {
            if (targetSectors.Count == 0 || count == 0) return 0;

            int placed = 0;
            int sectorIndex = 0;
            int attempts = 0;
            int maxAttempts = count * 10; // More attempts to find valid positions

            while (placed < count && attempts < maxAttempts)
            {
                attempts++;

                // Cycle through sectors
                var sector = targetSectors[sectorIndex % targetSectors.Count];
                sectorIndex++;

                // Ensure we have trackers for this sector
                EnsureSectorTrackers(sector);

                // Find a planet/moon to orbit
                var body = FindBodyForStation(sector, stationType);
                if (body == null) continue;

                // Create and place station with proper slot tracking
                var station = CreateStation(sector, factionId, stationType, body, placed);
                if (station == null) continue;

                if (IsValidStationPosition(sector, station.position))
                {
                    sector.AddPOIAtPosition(station, station.position);
                    sector.ProjectStationClaims(station);
                    sectorStationPositions[sector.id].Add(station.position);
                    placed++;
                }
            }

            if (placed < count)
            {
                Debug.LogWarning($"[FactionManager] Only placed {placed}/{count} {stationType} stations (attempts: {attempts})");
            }

            return placed;
        }

        private static void EnsureSectorTrackers(Sector sector)
        {
            if (!sectorOrbitalTrackers.ContainsKey(sector.id))
            {
                sectorOrbitalTrackers[sector.id] = new SectorOrbitalTracker();
            }
            if (!sectorStationPositions.ContainsKey(sector.id))
            {
                sectorStationPositions[sector.id] = new List<Vector2>();
            }
        }

        private static bool IsValidStationPosition(Sector sector, Vector2 position)
        {
            if (!sectorStationPositions.TryGetValue(sector.id, out var positions))
                return true;

            foreach (var existingPos in positions)
            {
                if (Vector2.Distance(position, existingPos) < MIN_STATION_DISTANCE)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Find a suitable body for a station (planet, moon, or asteroid belt).
        /// </summary>
        private static PointOfInterest FindBodyForStation(Sector sector, StationType stationType)
        {
            // Mining stations go in asteroid belts
            if (stationType == StationType.MiningStation)
            {
                var belts = new List<AsteroidBelt>();
                foreach (var poi in sector.AllPOIs)
                {
                    if (poi is AsteroidBelt belt)
                        belts.Add(belt);
                }
                if (belts.Count > 0)
                    return belts[rng.Next(belts.Count)];
                // Fallback to planets if no belts
            }

            var bodies = new List<PointOfInterest>();

            foreach (var poi in sector.AllPOIs)
            {
                if (poi is Planet planet)
                {
                    // Skip gas giants for most stations (except observatories)
                    if (planet.planetType == PlanetType.Gas && stationType != StationType.Observatory)
                        continue;
                    bodies.Add(planet);
                }
                else if (poi is Moon moon)
                {
                    bodies.Add(moon);
                }
            }

            if (bodies.Count == 0) return null;

            // Prefer planets for large stations, moons for small
            bool preferPlanet = stationType switch
            {
                StationType.FleetHQ => true,
                StationType.Bastion => true,
                StationType.Base => true,
                StationType.CommercialHub => true,
                StationType.Spaceport => true,
                StationType.OrbitalHabitat => true,
                _ => false
            };

            if (preferPlanet)
            {
                var planets = bodies.Where(b => b is Planet).ToList();
                if (planets.Count > 0)
                    return planets[rng.Next(planets.Count)];
            }

            return bodies[rng.Next(bodies.Count)];
        }

        /// <summary>
        /// Create a station at an orbital slot around a body (or inside an asteroid belt).
        /// </summary>
        private static Station CreateStation(Sector sector, FactionId factionId, StationType stationType, PointOfInterest body, int index)
        {
            Vector2 position;
            int slot = -1;

            // Handle asteroid belt placement differently
            if (body is AsteroidBelt belt)
            {
                // Place station at random position within the belt
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2);
                float distance = belt.innerRadius + (float)(rng.NextDouble() * (belt.outerRadius - belt.innerRadius));
                position = body.position + new Vector2(
                    Mathf.Cos(angle) * distance,
                    Mathf.Sin(angle) * distance
                );
            }
            else
            {
                // Get the orbital tracker for this sector
                var tracker = sectorOrbitalTrackers[sector.id];

                // Find an available slot
                slot = tracker.GetNextSlot(body.id);
                if (slot < 0)
                {
                    // All slots taken for this body
                    return null;
                }

                // Reserve the slot
                tracker.ReserveSlot(body.id, slot);

                // Get orbital position - scale based on actual body size
                float orbitDistance = GetScaledOrbitDistance(body, stationType);
                position = OrbitalSlots.GetSlotPosition(body.position, slot, orbitDistance);
            }

            // Generate station name
            string stationName = GenerateStationName(factionId, stationType, body.displayName, index);
            string stationId = $"{sector.id}_{factionId}_{stationType}_{index}";

            var station = new Station(stationId, stationName, stationType, factionId)
            {
                orbitingBodyId = body.id,
                orbitalSlot = slot,
                position = position
            };

            return station;
        }

        /// <summary>
        /// Get orbit distance scaled based on the actual size of the body.
        /// Stations orbit well outside the planet sprite to maintain spacing.
        /// </summary>
        private static float GetScaledOrbitDistance(PointOfInterest body, StationType stationType)
        {
            float bodySize = 50f; // Default

            // Get actual body size
            if (body is Planet planet)
            {
                bodySize = planet.size;
            }
            else if (body is Moon moon)
            {
                bodySize = moon.size;
            }

            // Base orbit = well outside the body sprite for proper spacing
            // With 8 slots and 500 unit min distance, we need ~600+ radius to fit stations
            float baseOrbit = (bodySize / 2f) + 400f;

            // Adjust by station type - vary the orbit rings
            return stationType switch
            {
                // Important stations in closer orbit ring
                StationType.FleetHQ => baseOrbit * 0.9f,
                StationType.Bastion => baseOrbit * 0.95f,
                StationType.CommercialHub => baseOrbit * 0.92f,
                StationType.Spaceport => baseOrbit * 0.95f,

                // Standard orbit
                StationType.Base => baseOrbit,
                StationType.OrbitalHabitat => baseOrbit,

                // Outer orbit ring
                StationType.IndustrialStation => baseOrbit * 1.2f,
                StationType.CivilianShipyard => baseOrbit * 1.15f,
                StationType.MilitaryShipyard => baseOrbit * 1.1f,

                // Far orbit
                StationType.ListeningPost => baseOrbit * 1.4f,
                StationType.Observatory => baseOrbit * 1.5f,
                StationType.ResearchStation => baseOrbit * 1.3f,

                _ => baseOrbit
            };
        }

        /// <summary>
        /// Generate a name for a station.
        /// </summary>
        private static string GenerateStationName(FactionId factionId, StationType stationType, string bodyName, int index)
        {
            string baseName = bodyName.Split(' ')[0]; // First word of body name

            return stationType switch
            {
                StationType.FleetHQ => $"{baseName} Fleet Command",
                StationType.Bastion => $"Bastion {baseName}-{index + 1}",
                StationType.Base => $"{baseName} Naval Base {index + 1}",
                StationType.Outpost => $"Outpost {baseName}-{index + 1}",
                StationType.MilitaryShipyard => $"{baseName} Military Shipyard",
                StationType.ListeningPost => $"LP-{baseName}-{index + 1}",

                StationType.MiningStation => $"{baseName} Mining Platform {index + 1}",
                StationType.IndustrialStation => $"{baseName} Industrial Complex {index + 1}",
                StationType.CommercialHub => $"{baseName} Trade Hub",
                StationType.CivilianShipyard => $"{baseName} Civilian Yards",

                StationType.ResearchStation => $"{baseName} Research Station",
                StationType.Observatory => $"{baseName} Observatory",

                StationType.OrbitalHabitat => $"{baseName} Orbital {index + 1}",
                StationType.Spaceport => $"{baseName} Spaceport",

                StationType.PirateHaven => $"The {baseName} Hideout",

                _ => $"{baseName} Station {index + 1}"
            };
        }
    }
}
