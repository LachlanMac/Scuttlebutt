using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Base class for all points of interest in a sector.
    /// POIs are data objects - the actual GameObjects are spawned by SectorManager.
    ///
    /// TWO CATEGORIES:
    /// - Natural POIs: Generated during world creation (Planet, Moon, AsteroidBelt, Nebula)
    /// - Infrastructure POIs: Placed by FactionManager after world gen (Station)
    /// </summary>
    [System.Serializable]
    public abstract class PointOfInterest
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        public POIType poiType;

        [Header("Location")]
        public Vector2 position;            // World position in sector
        public Vector2Int chunkCoord;       // Which chunk this POI is in

        [Header("Ownership")]
        public FactionId controlledBy;      // For infrastructure; natural POIs use None

        // Runtime reference
        [System.NonSerialized] public Sector sector;
        [System.NonSerialized] public GameObject spawnedObject;

        public PointOfInterest(string id, string name, POIType type)
        {
            this.id = id;
            this.displayName = name;
            this.poiType = type;
            this.controlledBy = FactionId.None;
        }

        /// <summary>
        /// Is this a natural feature (vs faction infrastructure)?
        /// </summary>
        public bool IsNatural => poiType switch
        {
            POIType.Planet => true,
            POIType.Moon => true,
            POIType.AsteroidBelt => true,
            POIType.Nebula => true,
            POIType.Anomaly => true,
            _ => false
        };

        /// <summary>
        /// Called when this POI is spawned into the scene.
        /// </summary>
        public virtual void OnSpawned(GameObject obj)
        {
            spawnedObject = obj;
        }

        /// <summary>
        /// Called when this POI is despawned.
        /// </summary>
        public virtual void OnDespawned()
        {
            spawnedObject = null;
        }

        /// <summary>
        /// Get the prefab path/name for spawning this POI.
        /// </summary>
        public abstract string GetPrefabPath();
    }

    public enum POIType
    {
        // Natural features (world generation)
        Planet,
        Moon,
        AsteroidBelt,
        Nebula,
        Anomaly,

        // Infrastructure (faction placement)
        Station,
        Debris          // Battle remnants
    }

    #region Natural POIs

    /// <summary>
    /// Planet - large body with gravity well, may have moons.
    /// Takes up significant space in sector.
    /// </summary>
    [System.Serializable]
    public class Planet : PointOfInterest
    {
        [Header("Planet Properties")]
        public PlanetType planetType;
        public float gravityWellRadius = 15000f;        // No jumping within this range

        [Header("Visual")]
        public string spriteName;                       // Sprite filename (loaded from Resources/Planets/{Category}/)
        public float size = 60f;                        // Visual scale

        [Header("Habitability")]
        public bool habitable = false;
        public long population = 0;                     // If colonized

        [Header("Moons")]
        public List<string> moonIds = new List<string>(); // IDs of orbiting moons

        public Planet() : base(null, null, POIType.Planet) { }

        public Planet(string id, string name, PlanetType type)
            : base(id, name, POIType.Planet)
        {
            this.planetType = type;
        }

        public override string GetPrefabPath()
        {
            return $"Prefabs/POI/Planet_{planetType}";
        }
    }

    public enum PlanetType
    {
        Terran,         // Earth-like, habitable
        Desert,         // Arid, marginally habitable
        Ice,            // Frozen world
        Gas,            // Gas giant, not landable
        Barren,         // Rocky, no atmosphere
        Volcanic,       // Active volcanism
        Ocean           // Water world
    }

    /// <summary>
    /// Moon - orbits a planet, smaller body.
    /// </summary>
    [System.Serializable]
    public class Moon : PointOfInterest
    {
        [Header("Moon Properties")]
        public MoonType moonType;
        public float orbitRadius = 2000f;       // Distance from parent planet
        public float orbitSpeed = 0.1f;         // Radians per game hour

        [Header("Visual")]
        public string spriteName;               // Sprite filename (loaded from Resources/Planets/{Category}/)
        public float size = 20f;                // Visual scale

        [Header("Parent")]
        public string parentPlanetId;           // Which planet this orbits

        [Header("Resources")]
        public bool hasResources = false;
        public ResourceType resourceType;

        public Moon() : base(null, null, POIType.Moon) { }

        public Moon(string id, string name, MoonType type, string parentId)
            : base(id, name, POIType.Moon)
        {
            this.moonType = type;
            this.parentPlanetId = parentId;
        }

        public override string GetPrefabPath()
        {
            return $"Prefabs/POI/Moon_{moonType}";
        }

        /// <summary>
        /// Get the moon's position based on orbit around parent.
        /// </summary>
        public Vector2 GetOrbitPosition(Vector2 parentPos, float gameTime)
        {
            float angle = gameTime * orbitSpeed;
            return parentPos + new Vector2(
                Mathf.Cos(angle) * orbitRadius,
                Mathf.Sin(angle) * orbitRadius
            );
        }
    }

    public enum MoonType
    {
        Rocky,          // Standard rocky moon
        Ice,            // Ice moon (water source)
        Volcanic,       // Volcanic activity
        Metallic        // Rich in metals
    }

    /// <summary>
    /// Asteroid belt - ring of rocks, resources, hiding spots.
    /// </summary>
    [System.Serializable]
    public class AsteroidBelt : PointOfInterest
    {
        [Header("Belt Properties")]
        public float innerRadius = 1000f;       // Inner edge
        public float outerRadius = 3000f;       // Outer edge
        public AsteroidDensity density = AsteroidDensity.Medium;

        [Header("Resources")]
        public bool hasResources = true;
        public ResourceType primaryResource = ResourceType.Ore;

        [Header("Tactical")]
        public bool providesConcealment = true; // Ships can hide here

        public AsteroidBelt() : base(null, null, POIType.AsteroidBelt) { }

        public AsteroidBelt(string id, string name)
            : base(id, name, POIType.AsteroidBelt)
        {
        }

        public override string GetPrefabPath()
        {
            return "Prefabs/POI/AsteroidBelt";
        }

        /// <summary>
        /// Check if a position is within the asteroid belt.
        /// </summary>
        public bool ContainsPosition(Vector2 pos)
        {
            float dist = Vector2.Distance(position, pos);
            return dist >= innerRadius && dist <= outerRadius;
        }
    }

    public enum AsteroidDensity
    {
        Sparse,     // Easy navigation
        Medium,     // Some obstacles
        Dense,      // Dangerous, good hiding
        Extreme     // Nearly impassable
    }

    /// <summary>
    /// Nebula - gas cloud that affects sensors and visibility.
    /// </summary>
    [System.Serializable]
    public class Nebula : PointOfInterest
    {
        [Header("Nebula Properties")]
        public NebulaType nebulaType;
        public float radius = 5000f;

        [Header("Effects")]
        public float sensorPenalty = 0.5f;      // Sensors work at 50%
        public float shieldBonus = 0f;          // Some nebulae boost shields
        public bool hazardous = false;          // Damages ships over time

        public Nebula() : base(null, null, POIType.Nebula) { }

        public Nebula(string id, string name, NebulaType type)
            : base(id, name, POIType.Nebula)
        {
            this.nebulaType = type;
        }

        public override string GetPrefabPath()
        {
            return $"Prefabs/POI/Nebula_{nebulaType}";
        }
    }

    public enum NebulaType
    {
        Emission,       // Glowing, visible
        Reflection,     // Reflects nearby starlight
        Dark,           // Obscures vision
        Planetary,      // Around dying star
        Ionized         // Interferes with electronics
    }

    /// <summary>
    /// Anomaly - strange phenomena, story hooks, exploration targets.
    /// </summary>
    [System.Serializable]
    public class Anomaly : PointOfInterest
    {
        [Header("Anomaly Properties")]
        public AnomalyType anomalyType;
        public bool explored = false;
        public string eventId;                  // What happens when investigated

        public Anomaly() : base(null, null, POIType.Anomaly) { }

        public Anomaly(string id, string name, AnomalyType type)
            : base(id, name, POIType.Anomaly)
        {
            this.anomalyType = type;
        }

        public override string GetPrefabPath()
        {
            return "Prefabs/POI/Anomaly";
        }
    }

    public enum AnomalyType
    {
        GravityWell,        // Unusual gravity
        TemporalRift,       // Time distortion
        EnergySignature,    // Unknown energy source
        Radiation,          // Radiation source
        Signal,             // Unknown signal
        Wreckage,           // Ancient debris
        Unknown             // Unidentified phenomenon
    }

    #endregion

    #region Infrastructure POIs (Faction-placed)

    /// <summary>
    /// Station - faction-built infrastructure.
    /// Spawned by FactionManager, not world generation.
    /// Each StationType has fixed capabilities - no separate class system.
    /// </summary>
    [System.Serializable]
    public class Station : PointOfInterest
    {
        [Header("Station Type")]
        public StationType stationType;

        [Header("Capabilities")]
        public bool canDock = true;
        public bool canResupply = true;
        public bool canRepair = false;
        public bool canBuildShips = false;

        [Header("Capacity")]
        public int maxDockedShips = 10;
        public int currentDockedShips = 0;

        [Header("Defense")]
        public int defenseRating = 0;           // 0 = civilian, higher = more defended

        [Header("Orbital Position")]
        public string orbitingBodyId;           // Planet/moon this orbits
        public int orbitalSlot = -1;            // Which slot around the body (-1 = not assigned)

        public Station() : base(null, null, POIType.Station) { }

        public Station(string id, string name, StationType type, FactionId owner)
            : base(id, name, POIType.Station)
        {
            this.stationType = type;
            this.controlledBy = owner;
            ApplyTypeDefaults();
        }

        /// <summary>
        /// Apply default capabilities based on station type.
        /// </summary>
        public void ApplyTypeDefaults()
        {
            switch (stationType)
            {
                // Military - increasing capability
                case StationType.ListeningPost:
                    maxDockedShips = 2;
                    defenseRating = 1;
                    canRepair = false;
                    break;
                case StationType.Outpost:
                    maxDockedShips = 4;
                    defenseRating = 2;
                    canRepair = false;
                    break;
                case StationType.Base:
                    maxDockedShips = 15;
                    defenseRating = 5;
                    canRepair = true;
                    break;
                case StationType.Bastion:
                    maxDockedShips = 25;
                    defenseRating = 15;
                    canRepair = true;
                    break;
                case StationType.FleetHQ:
                    maxDockedShips = 50;
                    defenseRating = 20;
                    canRepair = true;
                    break;
                case StationType.MilitaryShipyard:
                    maxDockedShips = 20;
                    defenseRating = 8;
                    canRepair = true;
                    canBuildShips = true;
                    break;

                // Economic
                case StationType.MiningStation:
                    maxDockedShips = 6;
                    defenseRating = 1;
                    canRepair = false;
                    break;
                case StationType.IndustrialStation:
                    maxDockedShips = 10;
                    defenseRating = 2;
                    canRepair = true;
                    break;
                case StationType.CommercialHub:
                    maxDockedShips = 30;
                    defenseRating = 3;
                    canRepair = true;
                    break;
                case StationType.CivilianShipyard:
                    maxDockedShips = 15;
                    defenseRating = 2;
                    canRepair = true;
                    canBuildShips = true;
                    break;

                // Science
                case StationType.ResearchStation:
                    maxDockedShips = 4;
                    defenseRating = 1;
                    canRepair = false;
                    break;
                case StationType.Observatory:
                    maxDockedShips = 2;
                    defenseRating = 0;
                    canRepair = false;
                    break;

                // Civilian
                case StationType.OrbitalHabitat:
                    maxDockedShips = 20;
                    defenseRating = 2;
                    canRepair = false;
                    break;
                case StationType.Spaceport:
                    maxDockedShips = 40;
                    defenseRating = 3;
                    canRepair = true;
                    break;

                // Other
                case StationType.PirateHaven:
                    maxDockedShips = 12;
                    defenseRating = 4;
                    canRepair = true;
                    break;
            }
        }

        public override string GetPrefabPath()
        {
            return $"Prefabs/POI/Station_{stationType}";
        }

        public bool CanAcceptDocking()
        {
            return canDock && currentDockedShips < maxDockedShips;
        }

        /// <summary>
        /// Is this a military station?
        /// </summary>
        public bool IsMilitary => stationType switch
        {
            StationType.ListeningPost => true,
            StationType.Outpost => true,
            StationType.Base => true,
            StationType.Bastion => true,
            StationType.FleetHQ => true,
            StationType.MilitaryShipyard => true,
            _ => false
        };
    }

    public enum StationType
    {
        // Military (ordered by size/importance)
        ListeningPost,      // Sensors/intelligence, tiny
        Outpost,            // Smallest military presence
        Base,               // Standard military installation
        Bastion,            // Heavily fortified defensive position
        FleetHQ,            // Command center (1 per faction)
        MilitaryShipyard,   // Builds warships

        // Economic
        MiningStation,      // Resource extraction
        IndustrialStation,  // Manufacturing/refining
        CommercialHub,      // Trade center
        CivilianShipyard,   // Builds civilian vessels

        // Science
        ResearchStation,    // R&D facility
        Observatory,        // Deep space scanning

        // Civilian
        OrbitalHabitat,     // Population center
        Spaceport,          // Civilian transit hub

        // Other
        PirateHaven         // Hidden raider base
    }

    /// <summary>
    /// Debris field - remnants of battle or disaster.
    /// Created dynamically when ships are destroyed.
    /// </summary>
    [System.Serializable]
    public class DebrisField : PointOfInterest
    {
        [Header("Debris Properties")]
        public float radius = 500f;
        public string originEvent;              // What created this
        public bool hasSalvage = true;
        public bool hasHazards = false;         // Unexploded ordnance, radiation

        public DebrisField() : base(null, null, POIType.Debris) { }

        public DebrisField(string id, string name)
            : base(id, name, POIType.Debris)
        {
        }

        public override string GetPrefabPath()
        {
            return "Prefabs/POI/DebrisField";
        }
    }

    #endregion

    #region Shared Enums

    public enum ResourceType
    {
        Ore,            // Common metals
        RareMetals,     // Valuable materials
        Ice,            // Water/fuel
        Crystals,       // Special materials
        Gas,            // Fuel gases
        Organics        // Biological resources
    }

    #endregion
}
