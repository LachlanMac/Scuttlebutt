using UnityEngine;
using System.Collections.Generic;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Generates random planet names from prefix/middlefix/suffix combinations.
    /// Tracks used names to prevent duplicates during galaxy generation.
    /// </summary>
    public static class PlanetNameGenerator
    {
        private static PlanetNameData nameData;
        private static HashSet<string> usedNames = new HashSet<string>();
        private static System.Random rng;
        private static bool isLoaded;

        [System.Serializable]
        private class PlanetNameData
        {
            public string[] prefixes;
            public string[] middlefixes;
            public string[] suffixes;
        }

        /// <summary>
        /// Initialize the generator with a seed. Call this before generating names.
        /// Forces a reload of name data to pick up any JSON changes.
        /// </summary>
        public static void Initialize(int seed)
        {
            rng = new System.Random(seed);
            usedNames.Clear();

            // Force reload to pick up any JSON changes
            isLoaded = false;
            nameData = null;
            LoadNameData();
        }

        /// <summary>
        /// Reset the used names tracker. Call this when starting a new generation.
        /// </summary>
        public static void Reset()
        {
            usedNames.Clear();
        }

        private static void LoadNameData()
        {
            if (isLoaded && nameData != null) return;

            var textAsset = Resources.Load<TextAsset>("Data/PlanetNames");
            if (textAsset != null)
            {
                nameData = JsonUtility.FromJson<PlanetNameData>(textAsset.text);
                isLoaded = true;
                Debug.Log($"[PlanetNameGenerator] Loaded {nameData.prefixes?.Length ?? 0} prefixes, " +
                          $"{nameData.middlefixes?.Length ?? 0} middlefixes, {nameData.suffixes?.Length ?? 0} suffixes");
            }
            else
            {
                Debug.LogWarning("[PlanetNameGenerator] Could not load PlanetNames.json");
                nameData = new PlanetNameData
                {
                    prefixes = new string[0],
                    middlefixes = new string[0],
                    suffixes = new string[0]
                };
            }
        }

        /// <summary>
        /// Generate a unique planet name.
        /// 40% chance: prefix + suffix
        /// 40% chance: prefix + middlefix
        /// 20% chance: prefix + middlefix + suffix
        /// </summary>
        /// <param name="fallbackName">Name to use if generator data is empty or all combinations exhausted</param>
        public static string GenerateName(string fallbackName = null)
        {
            if (!isLoaded) LoadNameData();

            // Check if we have valid data
            if (nameData == null ||
                nameData.prefixes == null || nameData.prefixes.Length == 0)
            {
                return fallbackName ?? $"Planet-{usedNames.Count + 1}";
            }

            // Try to generate a unique name (with retry limit)
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string name = GenerateNameInternal();
                if (!string.IsNullOrEmpty(name) && !usedNames.Contains(name))
                {
                    usedNames.Add(name);
                    return name;
                }
            }

            // Fallback if we couldn't generate a unique name
            string uniqueFallback = fallbackName ?? $"Planet-{usedNames.Count + 1}";
            int counter = 1;
            while (usedNames.Contains(uniqueFallback))
            {
                uniqueFallback = $"{fallbackName ?? "Planet"}-{usedNames.Count + counter}";
                counter++;
            }
            usedNames.Add(uniqueFallback);
            return uniqueFallback;
        }

        private static string GenerateNameInternal()
        {
            double roll = rng.NextDouble();

            // 40% prefix + suffix
            if (roll < 0.4)
            {
                if (nameData.suffixes != null && nameData.suffixes.Length > 0)
                {
                    string prefix = GetRandomElement(nameData.prefixes);
                    string suffix = GetRandomElement(nameData.suffixes);
                    if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(suffix))
                    {
                        return prefix + suffix;
                    }
                }
            }
            // 40% prefix + middlefix
            else if (roll < 0.8)
            {
                if (nameData.middlefixes != null && nameData.middlefixes.Length > 0)
                {
                    string prefix = GetRandomElement(nameData.prefixes);
                    string middlefix = GetRandomElement(nameData.middlefixes);
                    if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(middlefix))
                    {
                        return prefix + middlefix;
                    }
                }
            }
            // 20% prefix + middlefix + suffix
            else
            {
                if (nameData.middlefixes != null && nameData.middlefixes.Length > 0 &&
                    nameData.suffixes != null && nameData.suffixes.Length > 0)
                {
                    string prefix = GetRandomElement(nameData.prefixes);
                    string middlefix = GetRandomElement(nameData.middlefixes);
                    string suffix = GetRandomElement(nameData.suffixes);
                    if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(middlefix) && !string.IsNullOrEmpty(suffix))
                    {
                        return prefix + middlefix + suffix;
                    }
                }
            }

            return null;
        }

        private static string GetRandomElement(string[] array)
        {
            if (array == null || array.Length == 0) return null;
            return array[rng.Next(array.Length)];
        }

        /// <summary>
        /// Check if a name has already been used.
        /// </summary>
        public static bool IsNameUsed(string name)
        {
            return usedNames.Contains(name);
        }

        /// <summary>
        /// Mark a name as used (for external tracking, e.g., homeworlds).
        /// </summary>
        public static void MarkNameAsUsed(string name)
        {
            usedNames.Add(name);
        }

        /// <summary>
        /// Get the count of names generated so far.
        /// </summary>
        public static int UsedNamesCount => usedNames.Count;
    }
}
