using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Generates a new galaxy and writes it to JSON files.
    /// Run once to create the galaxy, then load via GalaxyLoader at runtime.
    /// </summary>
    public static class GalaxyGenerator
    {
        // Hardcoded homeworld sector names
        private static readonly Dictionary<FactionId, string> HomeworldNames = new Dictionary<FactionId, string>
        {
            { FactionId.Empire, "Zulrad System" },
            { FactionId.Federation, "Yorenn Prime" },
            { FactionId.Consortium, "Nexus Hub" },
            { FactionId.Pirate, "Tortuga Reach" },
            { FactionId.Independent, "New Haven" }
        };

        // Homeworld positions - Empire center, Federation corner
        private static readonly Dictionary<FactionId, Vector2Int> HomeworldPositions = new Dictionary<FactionId, Vector2Int>
        {
            { FactionId.Empire, new Vector2Int(5, 5) },      // Center
            { FactionId.Federation, new Vector2Int(2, 2) },  // Northwest corner-ish
            { FactionId.Consortium, new Vector2Int(7, 2) },  // Northeast
            { FactionId.Pirate, new Vector2Int(8, 7) },      // Southeast-ish
            { FactionId.Independent, new Vector2Int(2, 7) }  // Southwest-ish
        };

        private static List<string> sectorNames;
        private static int nameIndex;
        private static System.Random rng;

        // Sprite lists (loaded from Resources folders)
        private static List<string> gasSprites = new List<string>();
        private static List<string> habitableSprites = new List<string>();
        private static List<string> uninhabitableSprites = new List<string>();

        /// <summary>
        /// Generate a complete galaxy and write to JSON files.
        /// </summary>
        public static void GenerateGalaxy(int seed, string outputPath)
        {
            rng = new System.Random(seed);
            nameIndex = 0;

            // Load sector names
            LoadSectorNames();

            // Load available planet sprites
            LoadPlanetSprites();

            // Initialize planet name generator
            PlanetNameGenerator.Initialize(seed);

            var galaxy = new GalaxyData
            {
                galaxyName = "Starbelter Galaxy",
                seed = seed
            };

            Debug.Log($"[GalaxyGenerator] Generating galaxy with seed {seed}...");

            // Generate all sectors
            for (int x = 0; x < GalaxyData.GALAXY_SIZE; x++)
            {
                for (int y = 0; y < GalaxyData.GALAXY_SIZE; y++)
                {
                    var coord = new Vector2Int(x, y);
                    var sector = GenerateSector(coord);
                    galaxy.SetSector(x, y, sector);
                }
            }

            // Set homeworld references
            foreach (var kvp in HomeworldPositions)
            {
                galaxy.homeworlds[kvp.Key] = kvp.Value;
            }

            // Place faction infrastructure (stations, etc.)
            Debug.Log("[GalaxyGenerator] Placing faction infrastructure...");
            FactionManager.PlaceAllFactionInfrastructure(galaxy, seed);

            // Generate territory maps
            Debug.Log("[GalaxyGenerator] Generating territory maps...");
            TerritoryMapGenerator.GenerateTerritoryMap(galaxy, outputPath);
            TerritoryMapGenerator.GenerateTerritoryMapHiRes(galaxy, outputPath, 10); // 1000x1000

            // Write to files
            WriteGalaxyToJson(galaxy, outputPath);

            Debug.Log($"[GalaxyGenerator] Galaxy generation complete! {GalaxyData.GALAXY_SIZE * GalaxyData.GALAXY_SIZE} sectors created.");
        }

        private static void LoadSectorNames()
        {
            sectorNames = new List<string>();

            var textAsset = Resources.Load<TextAsset>("Data/SectorNames");
            if (textAsset != null)
            {
                var lines = textAsset.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("#"))
                    {
                        sectorNames.Add(trimmed);
                    }
                }
            }

            // Shuffle the names
            for (int i = sectorNames.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (sectorNames[i], sectorNames[j]) = (sectorNames[j], sectorNames[i]);
            }

            Debug.Log($"[GalaxyGenerator] Loaded {sectorNames.Count} sector names");
        }

        private static void LoadPlanetSprites()
        {
            gasSprites.Clear();
            habitableSprites.Clear();
            uninhabitableSprites.Clear();

            // Load all sprites from each category folder
            LoadSpritesFromFolder("Planets/Gas", gasSprites);
            LoadSpritesFromFolder("Planets/Habitable", habitableSprites);
            LoadSpritesFromFolder("Planets/Uninhabitable", uninhabitableSprites);

            Debug.Log($"[GalaxyGenerator] Loaded sprites - Gas: {gasSprites.Count}, Habitable: {habitableSprites.Count}, Uninhabitable: {uninhabitableSprites.Count}");
        }

        private static void LoadSpritesFromFolder(string folderPath, List<string> targetList)
        {
            // Load all sprites from the folder
            var sprites = Resources.LoadAll<Sprite>(folderPath);
            foreach (var sprite in sprites)
            {
                targetList.Add(sprite.name);
            }
        }

        private static string GetRandomSprite(PlanetType planetType)
        {
            List<string> spriteList = planetType switch
            {
                PlanetType.Gas => gasSprites,
                PlanetType.Terran => habitableSprites,
                PlanetType.Ocean => habitableSprites,
                _ => uninhabitableSprites // Desert, Ice, Barren, Volcanic
            };

            if (spriteList.Count == 0) return null;
            return spriteList[rng.Next(spriteList.Count)];
        }

        private static string GetRandomMoonSprite()
        {
            // Moons pull from either habitable or uninhabitable
            var combined = new List<string>();
            combined.AddRange(habitableSprites);
            combined.AddRange(uninhabitableSprites);

            if (combined.Count == 0) return null;
            return combined[rng.Next(combined.Count)];
        }

        private static float GetRandomPlanetSize(PlanetType planetType)
        {
            return planetType switch
            {
                PlanetType.Gas => 80f + (float)(rng.NextDouble() * 20f),  // 80-100
                _ => 40f + (float)(rng.NextDouble() * 40f)                 // 40-80
            };
        }

        private static float GetRandomMoonSize()
        {
            return 20f + (float)(rng.NextDouble() * 10f); // 20-30
        }

        private static string GetSpriteCategory(PlanetType planetType)
        {
            return planetType switch
            {
                PlanetType.Gas => "Gas",
                PlanetType.Terran => "Habitable",
                PlanetType.Ocean => "Habitable",
                _ => "Uninhabitable"
            };
        }

        private static string GetMoonSpriteCategory(string spriteName)
        {
            // Check which folder the sprite came from
            if (habitableSprites.Contains(spriteName)) return "Habitable";
            return "Uninhabitable";
        }

        private static string GetNextSectorName()
        {
            if (sectorNames == null || sectorNames.Count == 0)
                return $"Sector {nameIndex++}";

            if (nameIndex >= sectorNames.Count)
                return $"Unnamed Sector {nameIndex++ - sectorNames.Count}";

            return sectorNames[nameIndex++];
        }

        private static Sector GenerateSector(Vector2Int coord)
        {
            // Check if this is a homeworld sector
            string sectorName = null;
            FactionId controller = FactionId.None;
            SectorType sectorType = SectorType.Frontier;

            foreach (var kvp in HomeworldPositions)
            {
                if (kvp.Value == coord)
                {
                    sectorName = HomeworldNames[kvp.Key];
                    controller = kvp.Key;
                    sectorType = SectorType.Home;
                    break;
                }
            }

            // If not a homeworld, use random name
            if (sectorName == null)
            {
                sectorName = GetNextSectorName();
                sectorType = DetermineSectorType(coord);
            }

            string sectorId = $"sector_{coord.x}_{coord.y}";
            var sector = new Sector(sectorId, sectorName, sectorType, coord);
            sector.controlledBy = controller;

            // Generate natural POIs
            GenerateNaturalPOIs(sector, coord);

            return sector;
        }

        private static SectorType DetermineSectorType(Vector2Int coord)
        {
            // Distance from center
            float distFromCenter = Vector2.Distance(coord, new Vector2(4.5f, 4.5f));

            // Corners and edges are more likely to be deep space/frontier
            if (distFromCenter > 5f)
                return SectorType.DeepSpace;
            if (distFromCenter > 3f)
                return rng.NextDouble() < 0.6 ? SectorType.Frontier : SectorType.Core;

            return rng.NextDouble() < 0.7 ? SectorType.Core : SectorType.Frontier;
        }

        private static void GenerateNaturalPOIs(Sector sector, Vector2Int galaxyCoord)
        {
            // 0-4 planets per sector
            int planetCount = rng.Next(0, 5);

            // Track used chunks to avoid overlap
            var usedChunks = new HashSet<Vector2Int>();

            // Generate planets (spread out with larger buffer)
            for (int i = 0; i < planetCount; i++)
            {
                var chunk = GetRandomUnusedChunk(usedChunks, 3); // Keep 3 chunk buffer for more spread
                if (chunk.x < 0) break; // No more space

                var planet = GeneratePlanet(sector, i, chunk);
                sector.AddPOI(planet, chunk);
                usedChunks.Add(chunk);

                // Maybe add moons (45% chance per non-barren planet, 1-2 moons max)
                if (planet.planetType != PlanetType.Barren && rng.NextDouble() < 0.45)
                {
                    int moonCount = rng.Next(1, 3); // 1-2 moons
                    var moonPositions = new List<Vector2>();
                    float minMoonDistance = 1500f; // Minimum distance between moons

                    for (int m = 0; m < moonCount; m++)
                    {
                        var moon = GenerateMoon(planet, m, moonCount);

                        // Find a position that doesn't overlap with other moons
                        Vector2 moonPos = Vector2.zero;
                        bool validPosition = false;

                        for (int attempt = 0; attempt < 10; attempt++)
                        {
                            float orbitAngle = (float)(rng.NextDouble() * Mathf.PI * 2);
                            Vector2 moonOffset = new Vector2(
                                Mathf.Cos(orbitAngle) * moon.orbitRadius,
                                Mathf.Sin(orbitAngle) * moon.orbitRadius
                            );
                            moonPos = planet.position + moonOffset;

                            // Check distance from other moons
                            validPosition = true;
                            foreach (var existingMoonPos in moonPositions)
                            {
                                if (Vector2.Distance(moonPos, existingMoonPos) < minMoonDistance)
                                {
                                    validPosition = false;
                                    break;
                                }
                            }

                            if (validPosition) break;
                        }

                        if (validPosition)
                        {
                            moonPositions.Add(moonPos);
                            sector.AddPOIAtPosition(moon, moonPos);
                        }
                    }
                }
            }

            // 1-3 asteroid belts per sector
            int beltCount = rng.Next(1, 4);
            for (int i = 0; i < beltCount; i++)
            {
                var chunk = GetRandomUnusedChunk(usedChunks, 1);
                if (chunk.x < 0) break;

                var belt = GenerateAsteroidBelt(sector, i, chunk);
                sector.AddPOI(belt, chunk);
                usedChunks.Add(chunk);
            }

            // 0-2 nebulae (20% of sectors have one)
            if (rng.NextDouble() < 0.2)
            {
                int nebulaCount = rng.Next(1, 3);
                for (int i = 0; i < nebulaCount; i++)
                {
                    var chunk = GetRandomUnusedChunk(usedChunks, 2);
                    if (chunk.x < 0) break;

                    var nebula = GenerateNebula(sector, i, chunk);
                    sector.AddPOI(nebula, chunk);
                    usedChunks.Add(chunk);
                }
            }

            // 0-1 anomaly (10% chance)
            if (rng.NextDouble() < 0.1)
            {
                var chunk = GetRandomUnusedChunk(usedChunks, 1);
                if (chunk.x >= 0)
                {
                    var anomaly = GenerateAnomaly(sector, chunk);
                    sector.AddPOI(anomaly, chunk);
                }
            }
        }

        private static Vector2Int GetRandomUnusedChunk(HashSet<Vector2Int> used, int minDistanceFromOthers)
        {
            // Try to find an unused chunk spread across the full sector
            for (int attempts = 0; attempts < 50; attempts++)
            {
                int x = rng.Next(0, Sector.CHUNKS_PER_AXIS);
                int y = rng.Next(0, Sector.CHUNKS_PER_AXIS);
                var chunk = new Vector2Int(x, y);

                // Check distance from other POIs
                bool tooClose = false;
                foreach (var usedChunk in used)
                {
                    int dx = Mathf.Abs(chunk.x - usedChunk.x);
                    int dy = Mathf.Abs(chunk.y - usedChunk.y);
                    // Use Manhattan-ish distance - must be far enough in BOTH dimensions
                    if (dx < minDistanceFromOthers && dy < minDistanceFromOthers)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                    return chunk;
            }

            return new Vector2Int(-1, -1); // No space found
        }

        private static Planet GeneratePlanet(Sector sector, int index, Vector2Int chunk)
        {
            var types = new[] { PlanetType.Terran, PlanetType.Desert, PlanetType.Ice, PlanetType.Ocean, PlanetType.Barren, PlanetType.Volcanic, PlanetType.Gas };
            var weights = new[] { 0.15f, 0.15f, 0.15f, 0.1f, 0.25f, 0.1f, 0.1f }; // Barren most common

            var planetType = WeightedRandom(types, weights);

            // Generate planet name (fallback to sector-based naming)
            string suffix = GetRomanNumeral(index + 1);
            string baseName = sector.displayName.Replace(" System", "").Replace(" Prime", "").Replace(" Reach", "").Replace(" Hub", "").Replace(" Haven", "");
            string fallbackName = $"{baseName} {suffix}";
            string planetName = PlanetNameGenerator.GenerateName(fallbackName);

            // Assign visual properties
            string spriteName = GetRandomSprite(planetType);
            float size = GetRandomPlanetSize(planetType);

            var planet = new Planet
            {
                id = $"{sector.id}_planet_{index}",
                displayName = planetName,
                planetType = planetType,
                gravityWellRadius = planetType == PlanetType.Gas ? 8000f : 5000f,
                spriteName = spriteName,
                size = size
            };

            return planet;
        }

        private static Moon GenerateMoon(Planet parent, int index, int totalMoonCount)
        {
            var types = new[] { MoonType.Rocky, MoonType.Ice, MoonType.Volcanic, MoonType.Metallic };
            var moonType = types[rng.Next(types.Length)];

            // Generate moon name (may also rename parent planet for Major/Minor pairs)
            string moonName = GenerateMoonName(parent, index, totalMoonCount);

            // Assign visual properties (moons use habitable/uninhabitable sprites at smaller scale)
            string spriteName = GetRandomMoonSprite();
            float size = GetRandomMoonSize();

            var moon = new Moon
            {
                id = $"{parent.id}_moon_{index}",
                displayName = moonName,
                moonType = moonType,
                parentPlanetId = parent.id,
                orbitRadius = 2000f + (float)(rng.NextDouble() * 3000f), // 2000-5000 from planet
                spriteName = spriteName,
                size = size
            };

            return moon;
        }

        private static string GenerateMoonName(Planet parent, int index, int totalMoonCount)
        {
            // Single moon: 10% chance for Major/Minor pair
            if (totalMoonCount == 1 && rng.NextDouble() < 0.10)
            {
                // Rename the planet to "X Major" and moon to "X Minor"
                string baseName = parent.displayName;
                parent.displayName = $"{baseName} Major";
                return $"{baseName} Minor";
            }

            // Otherwise, generate a unique name for the moon
            return PlanetNameGenerator.GenerateName($"{parent.displayName} Moon {index + 1}");
        }

        private static AsteroidBelt GenerateAsteroidBelt(Sector sector, int index, Vector2Int chunk)
        {
            var densities = new[] { AsteroidDensity.Sparse, AsteroidDensity.Medium, AsteroidDensity.Dense };
            var weights = new[] { 0.4f, 0.4f, 0.2f };

            var belt = new AsteroidBelt
            {
                id = $"{sector.id}_belt_{index}",
                displayName = $"{sector.displayName} Belt {(char)('A' + index)}",
                density = WeightedRandom(densities, weights),
                innerRadius = 3000f + (float)(rng.NextDouble() * 2000f),
                outerRadius = 7000f + (float)(rng.NextDouble() * 3000f)
            };

            return belt;
        }

        private static Nebula GenerateNebula(Sector sector, int index, Vector2Int chunk)
        {
            var types = new[] { NebulaType.Emission, NebulaType.Reflection, NebulaType.Dark, NebulaType.Planetary };
            string[] prefixes = { "Crimson", "Azure", "Violet", "Emerald", "Golden", "Obsidian", "Silver", "Amber" };

            var nebula = new Nebula
            {
                id = $"{sector.id}_nebula_{index}",
                displayName = $"{prefixes[rng.Next(prefixes.Length)]} Nebula",
                nebulaType = types[rng.Next(types.Length)],
                radius = 8000f + (float)(rng.NextDouble() * 7000f) // 8000-15000
            };

            return nebula;
        }

        private static Anomaly GenerateAnomaly(Sector sector, Vector2Int chunk)
        {
            var types = new[] { AnomalyType.GravityWell, AnomalyType.TemporalRift, AnomalyType.EnergySignature, AnomalyType.Unknown };
            string[] names = { "Spatial Distortion", "Energy Anomaly", "Unidentified Signal", "Gravitational Anomaly", "Temporal Echo" };

            var anomaly = new Anomaly
            {
                id = $"{sector.id}_anomaly",
                displayName = names[rng.Next(names.Length)],
                anomalyType = types[rng.Next(types.Length)]
            };

            return anomaly;
        }

        private static T WeightedRandom<T>(T[] items, float[] weights)
        {
            float total = 0f;
            foreach (var w in weights) total += w;

            float roll = (float)(rng.NextDouble() * total);
            float cumulative = 0f;

            for (int i = 0; i < items.Length; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative)
                    return items[i];
            }

            return items[items.Length - 1];
        }

        private static string GetRomanNumeral(int num)
        {
            return num switch
            {
                1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
                6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X",
                _ => num.ToString()
            };
        }

        #region JSON Serialization

        private static void WriteGalaxyToJson(GalaxyData galaxy, string outputPath)
        {
            // Ensure directory exists
            string sectorsPath = Path.Combine(outputPath, "sectors");
            Directory.CreateDirectory(sectorsPath);

            // Write galaxy metadata
            var metadata = new GalaxyMetadata
            {
                galaxyName = galaxy.galaxyName,
                seed = galaxy.seed,
                galaxySize = GalaxyData.GALAXY_SIZE,
                chunkSize = (int)Sector.CHUNK_SIZE,
                chunksPerSector = Sector.CHUNKS_PER_AXIS
            };

            // Convert homeworlds
            metadata.homeworlds = new List<HomeworldEntry>();
            foreach (var kvp in galaxy.homeworlds)
            {
                metadata.homeworlds.Add(new HomeworldEntry
                {
                    faction = kvp.Key.ToString(),
                    x = kvp.Value.x,
                    y = kvp.Value.y
                });
            }

            string metadataJson = JsonUtility.ToJson(metadata, true);
            File.WriteAllText(Path.Combine(outputPath, "galaxy.json"), metadataJson);

            // Write each sector
            foreach (var sector in galaxy.AllSectors)
            {
                var sectorData = SerializeSector(sector);
                string sectorJson = JsonUtility.ToJson(sectorData, true);
                string filename = $"sector_{sector.galaxyCoord.x}_{sector.galaxyCoord.y}.json";
                File.WriteAllText(Path.Combine(sectorsPath, filename), sectorJson);
            }

            Debug.Log($"[GalaxyGenerator] Wrote galaxy files to {outputPath}");
        }

        private static SectorData SerializeSector(Sector sector)
        {
            var data = new SectorData
            {
                id = sector.id,
                displayName = sector.displayName,
                type = sector.type.ToString(),
                controlledBy = sector.controlledBy.ToString(),
                galaxyX = sector.galaxyCoord.x,
                galaxyY = sector.galaxyCoord.y,
                pois = new List<POIData>()
            };

            foreach (var poi in sector.AllPOIs)
            {
                data.pois.Add(SerializePOI(poi));
            }

            return data;
        }

        private static POIData SerializePOI(PointOfInterest poi)
        {
            var data = new POIData
            {
                id = poi.id,
                displayName = poi.displayName,
                poiType = poi.GetType().Name,
                chunkX = poi.chunkCoord.x,
                chunkY = poi.chunkCoord.y,
                positionX = poi.position.x,
                positionY = poi.position.y
            };

            // Type-specific data
            switch (poi)
            {
                case Planet planet:
                    data.subType = planet.planetType.ToString();
                    data.gravityWellRadius = planet.gravityWellRadius;
                    data.spriteName = planet.spriteName;
                    data.size = planet.size;
                    break;
                case Moon moon:
                    data.subType = moon.moonType.ToString();
                    data.parentId = moon.parentPlanetId;
                    data.orbitRadius = moon.orbitRadius;
                    data.spriteName = moon.spriteName;
                    data.size = moon.size;
                    break;
                case AsteroidBelt belt:
                    data.subType = belt.density.ToString();
                    data.innerRadius = belt.innerRadius;
                    data.outerRadius = belt.outerRadius;
                    break;
                case Nebula nebula:
                    data.subType = nebula.nebulaType.ToString();
                    data.radius = nebula.radius;
                    break;
                case Anomaly anomaly:
                    data.subType = anomaly.anomalyType.ToString();
                    break;
                case Station station:
                    data.subType = station.stationType.ToString();
                    data.controlledBy = station.controlledBy.ToString();
                    data.orbitingBodyId = station.orbitingBodyId;
                    data.orbitalSlot = station.orbitalSlot;
                    break;
            }

            return data;
        }

        #endregion

        #region JSON Data Classes

        [System.Serializable]
        private class GalaxyMetadata
        {
            public string galaxyName;
            public int seed;
            public int galaxySize;
            public int chunkSize;
            public int chunksPerSector;
            public List<HomeworldEntry> homeworlds;
        }

        [System.Serializable]
        private class HomeworldEntry
        {
            public string faction;
            public int x;
            public int y;
        }

        [System.Serializable]
        private class SectorData
        {
            public string id;
            public string displayName;
            public string type;
            public string controlledBy;
            public int galaxyX;
            public int galaxyY;
            public List<POIData> pois;
        }

        [System.Serializable]
        private class POIData
        {
            public string id;
            public string displayName;
            public string poiType;
            public int chunkX;
            public int chunkY;
            public float positionX;
            public float positionY;

            // Type-specific fields
            public string subType;
            public string parentId;
            public float orbitRadius;
            public float innerRadius;
            public float outerRadius;
            public float radius;
            public float gravityWellRadius;
            public string controlledBy;
            public string orbitingBodyId;
            public int orbitalSlot;

            // Visual properties
            public string spriteName;
            public float size;
        }

        #endregion
    }
}
