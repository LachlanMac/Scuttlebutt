using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Loads galaxy data from JSON files into memory at runtime.
    /// </summary>
    public static class GalaxyLoader
    {
        private static GalaxyData loadedGalaxy;
        private static bool isLoaded = false;

        /// <summary>
        /// Get the loaded galaxy. Loads from StreamingAssets if not already loaded.
        /// </summary>
        public static GalaxyData Galaxy
        {
            get
            {
                if (!isLoaded)
                    LoadGalaxy();
                return loadedGalaxy;
            }
        }

        /// <summary>
        /// Check if galaxy is loaded.
        /// </summary>
        public static bool IsLoaded => isLoaded;

        /// <summary>
        /// Load the galaxy from StreamingAssets/Galaxy/
        /// </summary>
        public static void LoadGalaxy()
        {
            string basePath = Path.Combine(Application.streamingAssetsPath, "Galaxy");
            LoadGalaxyFromPath(basePath);
        }

        /// <summary>
        /// Load the galaxy from a specific path.
        /// </summary>
        public static void LoadGalaxyFromPath(string basePath)
        {
            loadedGalaxy = new GalaxyData();

            // Load metadata
            string metadataPath = Path.Combine(basePath, "galaxy.json");
            if (!File.Exists(metadataPath))
            {
                Debug.LogError($"[GalaxyLoader] Galaxy metadata not found at {metadataPath}");
                isLoaded = true;
                return;
            }

            string metadataJson = File.ReadAllText(metadataPath);
            var metadata = JsonUtility.FromJson<GalaxyMetadata>(metadataJson);

            loadedGalaxy.galaxyName = metadata.galaxyName;
            loadedGalaxy.seed = metadata.seed;

            // Load homeworlds
            if (metadata.homeworlds != null)
            {
                foreach (var hw in metadata.homeworlds)
                {
                    if (System.Enum.TryParse<FactionId>(hw.faction, out var faction))
                    {
                        loadedGalaxy.homeworlds[faction] = new SectorCoord(hw.x, hw.y);
                    }
                }
            }

            Debug.Log($"[GalaxyLoader] Loading galaxy '{metadata.galaxyName}' (seed: {metadata.seed})");

            // Load all sectors
            string sectorsPath = Path.Combine(basePath, "sectors");
            int loadedCount = 0;

            for (int x = 0; x < GalaxyData.GALAXY_SIZE; x++)
            {
                for (int y = 0; y < GalaxyData.GALAXY_SIZE; y++)
                {
                    string filename = $"sector_{x}_{y}.json";
                    string sectorPath = Path.Combine(sectorsPath, filename);

                    if (File.Exists(sectorPath))
                    {
                        string sectorJson = File.ReadAllText(sectorPath);
                        var sector = DeserializeSector(sectorJson);
                        if (sector != null)
                        {
                            loadedGalaxy.SetSector(x, y, sector);
                            loadedCount++;
                        }
                    }
                }
            }

            isLoaded = true;
            Debug.Log($"[GalaxyLoader] Loaded {loadedCount} sectors");
        }

        /// <summary>
        /// Reload the galaxy (useful for hot-reloading in editor).
        /// </summary>
        public static void Reload()
        {
            isLoaded = false;
            loadedGalaxy = null;
            LoadGalaxy();
        }

        /// <summary>
        /// Unload the galaxy from memory.
        /// </summary>
        public static void Unload()
        {
            loadedGalaxy = null;
            isLoaded = false;
        }

        private static Sector DeserializeSector(string json)
        {
            var data = JsonUtility.FromJson<SectorData>(json);
            if (data == null) return null;

            // Parse enums
            System.Enum.TryParse<SectorType>(data.type, out var sectorType);
            System.Enum.TryParse<FactionId>(data.controlledBy, out var controlledBy);

            var sector = new Sector(
                data.id,
                data.displayName,
                sectorType,
                new Vector2Int(data.galaxyX, data.galaxyY)
            );
            sector.controlledBy = controlledBy;

            // Load POIs
            if (data.pois != null)
            {
                foreach (var poiData in data.pois)
                {
                    var poi = DeserializePOI(poiData);
                    if (poi != null)
                    {
                        var worldPos = new Vector2(poiData.positionX, poiData.positionY);
                        sector.AddPOIAtPosition(poi, worldPos);
                    }
                }
            }

            return sector;
        }

        private static PointOfInterest DeserializePOI(POIData data)
        {
            PointOfInterest poi = null;

            switch (data.poiType)
            {
                case "Planet":
                    System.Enum.TryParse<PlanetType>(data.subType, out var planetType);
                    poi = new Planet
                    {
                        id = data.id,
                        displayName = data.displayName,
                        planetType = planetType,
                        gravityWellRadius = data.gravityWellRadius > 0 ? data.gravityWellRadius : 5000f,
                        spriteName = data.spriteName,
                        size = data.size > 0 ? data.size : 60f
                    };
                    break;

                case "Moon":
                    System.Enum.TryParse<MoonType>(data.subType, out var moonType);
                    poi = new Moon
                    {
                        id = data.id,
                        displayName = data.displayName,
                        moonType = moonType,
                        parentPlanetId = data.parentId,
                        orbitRadius = data.orbitRadius,
                        spriteName = data.spriteName,
                        size = data.size > 0 ? data.size : 20f
                    };
                    break;

                case "AsteroidBelt":
                    System.Enum.TryParse<AsteroidDensity>(data.subType, out var density);
                    poi = new AsteroidBelt
                    {
                        id = data.id,
                        displayName = data.displayName,
                        density = density,
                        innerRadius = data.innerRadius,
                        outerRadius = data.outerRadius
                    };
                    break;

                case "Nebula":
                    System.Enum.TryParse<NebulaType>(data.subType, out var nebulaType);
                    poi = new Nebula
                    {
                        id = data.id,
                        displayName = data.displayName,
                        nebulaType = nebulaType,
                        radius = data.radius
                    };
                    break;

                case "Anomaly":
                    System.Enum.TryParse<AnomalyType>(data.subType, out var anomalyType);
                    poi = new Anomaly
                    {
                        id = data.id,
                        displayName = data.displayName,
                        anomalyType = anomalyType
                    };
                    break;

                case "Station":
                    System.Enum.TryParse<StationType>(data.subType, out var stationType);
                    System.Enum.TryParse<FactionId>(data.controlledBy, out var stationOwner);
                    var station = new Station
                    {
                        id = data.id,
                        displayName = data.displayName,
                        stationType = stationType,
                        orbitingBodyId = data.orbitingBodyId,
                        orbitalSlot = data.orbitalSlot
                    };
                    station.controlledBy = stationOwner;
                    station.ApplyTypeDefaults();
                    poi = station;
                    break;

                case "DebrisField":
                    poi = new DebrisField
                    {
                        id = data.id,
                        displayName = data.displayName,
                        radius = data.radius > 0 ? data.radius : 1000f
                    };
                    break;

                default:
                    Debug.LogWarning($"[GalaxyLoader] Unknown POI type: {data.poiType}");
                    break;
            }

            return poi;
        }

        #region JSON Data Classes (must match GalaxyGenerator)

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
