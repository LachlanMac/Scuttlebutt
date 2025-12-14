using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Loads station prefabs based on station type.
    /// Uses a JSON config to map StationType -> visual size -> prefab path.
    /// This allows reusing a few test prefabs for many station types during development.
    /// </summary>
    public static class StationPrefabLoader
    {
        private static Dictionary<string, string> prefabPaths;      // size -> path
        private static Dictionary<StationType, string> typeMappings; // type -> size
        private static string fallbackSize = "Small";
        private static bool isLoaded = false;

        // Cache loaded prefabs
        private static Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        /// <summary>
        /// Get the prefab for a station type.
        /// Returns null if prefab not found (caller should create placeholder).
        /// </summary>
        public static GameObject GetPrefab(StationType stationType)
        {
            EnsureLoaded();

            string size = GetSizeForType(stationType);
            return GetPrefabForSize(size);
        }

        /// <summary>
        /// Get the visual size category for a station type.
        /// </summary>
        public static string GetSizeForType(StationType stationType)
        {
            EnsureLoaded();

            if (typeMappings != null && typeMappings.TryGetValue(stationType, out var size))
                return size;

            return fallbackSize;
        }

        /// <summary>
        /// Get the prefab for a specific size category.
        /// </summary>
        public static GameObject GetPrefabForSize(string size)
        {
            EnsureLoaded();

            // Check cache first
            if (prefabCache.TryGetValue(size, out var cached))
                return cached;

            // Get path
            if (prefabPaths == null || !prefabPaths.TryGetValue(size, out var path))
            {
                Debug.LogWarning($"[StationPrefabLoader] No path for size '{size}'");
                return null;
            }

            // Load prefab
            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null)
            {
                prefabCache[size] = prefab;
            }
            else
            {
                Debug.LogWarning($"[StationPrefabLoader] Prefab not found at '{path}'");
            }

            return prefab;
        }

        /// <summary>
        /// Get the scale multiplier for a station type.
        /// Even within the same prefab, some types should appear larger.
        /// </summary>
        public static float GetScaleMultiplier(StationType stationType)
        {
            return stationType switch
            {
                // Large stations - vary scale within category
                StationType.FleetHQ => 1.5f,
                StationType.Bastion => 1.2f,
                StationType.CommercialHub => 1.3f,
                StationType.Spaceport => 1.1f,

                // Medium stations
                StationType.Base => 1.0f,
                StationType.MilitaryShipyard => 1.1f,
                StationType.IndustrialStation => 0.9f,
                StationType.CivilianShipyard => 1.0f,
                StationType.OrbitalHabitat => 0.8f,

                // Small stations
                StationType.Outpost => 1.0f,
                StationType.ListeningPost => 0.6f,
                StationType.MiningStation => 0.8f,
                StationType.ResearchStation => 0.7f,
                StationType.Observatory => 0.5f,
                StationType.PirateHaven => 0.9f,

                _ => 1.0f
            };
        }

        /// <summary>
        /// Reload the config (useful for hot-reloading in editor).
        /// </summary>
        public static void Reload()
        {
            isLoaded = false;
            prefabPaths = null;
            typeMappings = null;
            prefabCache.Clear();
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (isLoaded) return;
            LoadConfig();
        }

        private static void LoadConfig()
        {
            prefabPaths = new Dictionary<string, string>();
            typeMappings = new Dictionary<StationType, string>();

            var textAsset = Resources.Load<TextAsset>("Data/StationPrefabs");
            if (textAsset == null)
            {
                Debug.LogWarning("[StationPrefabLoader] StationPrefabs.json not found, using defaults");
                SetupDefaults();
                isLoaded = true;
                return;
            }

            var config = JsonUtility.FromJson<StationPrefabConfig>(textAsset.text);
            if (config == null)
            {
                Debug.LogError("[StationPrefabLoader] Failed to parse StationPrefabs.json");
                SetupDefaults();
                isLoaded = true;
                return;
            }

            // Parse prefab paths
            if (config.prefabPaths != null)
            {
                foreach (var entry in config.prefabPaths)
                {
                    prefabPaths[entry.size] = entry.path;
                }
            }

            // Parse type mappings
            if (config.typeMappings != null)
            {
                foreach (var entry in config.typeMappings)
                {
                    if (System.Enum.TryParse<StationType>(entry.type, out var stationType))
                    {
                        typeMappings[stationType] = entry.size;
                    }
                }
            }

            if (!string.IsNullOrEmpty(config.fallbackSize))
                fallbackSize = config.fallbackSize;

            isLoaded = true;
            Debug.Log($"[StationPrefabLoader] Loaded {prefabPaths.Count} prefab paths, {typeMappings.Count} type mappings");
        }

        private static void SetupDefaults()
        {
            // Hardcoded fallbacks if config is missing
            prefabPaths["Large"] = "Prefabs/Stations/Station_Large";
            prefabPaths["Medium"] = "Prefabs/Stations/Station_Medium";
            prefabPaths["Small"] = "Prefabs/Stations/Station_Small";

            typeMappings[StationType.FleetHQ] = "Large";
            typeMappings[StationType.Bastion] = "Large";
            typeMappings[StationType.Base] = "Medium";
            typeMappings[StationType.Outpost] = "Small";
            // etc - abbreviated for defaults
        }

        // JSON structure - Unity's JsonUtility needs arrays for nested objects
        [System.Serializable]
        private class StationPrefabConfig
        {
            public PrefabPathEntry[] prefabPaths;
            public TypeMappingEntry[] typeMappings;
            public string fallbackSize;
        }

        [System.Serializable]
        private class PrefabPathEntry
        {
            public string size;
            public string path;
        }

        [System.Serializable]
        private class TypeMappingEntry
        {
            public string type;
            public string size;
        }
    }
}
