using UnityEngine;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Holds sprite arrays for planet/moon generation.
    /// Attach to SectorTestRunner or a persistent GameObject.
    /// </summary>
    public class PlanetSprites : MonoBehaviour
    {
        private static PlanetSprites instance;
        public static PlanetSprites Instance => instance;

        [Header("Gas Giants")]
        [Tooltip("Large gas giant planets (Jupiter, Saturn types)")]
        public Sprite[] gasGiants;

        [Header("Habitable")]
        [Tooltip("Earth-like, ocean, or otherwise life-supporting worlds")]
        public Sprite[] habitable;

        [Header("Non-Habitable")]
        [Tooltip("Barren, desert, ice, volcanic - not life-supporting")]
        public Sprite[] nonHabitable;

        [Header("Asteroids")]
        [Tooltip("Rock chunks for asteroid fields")]
        public Sprite[] asteroids;

        [Header("Size Settings")]
        [Tooltip("Planet size range (units)")]
        public Vector2 planetSizeRange = new Vector2(40f, 80f);

        [Tooltip("Moon size range (units)")]
        public Vector2 moonSizeRange = new Vector2(15f, 25f);

        [Tooltip("Gas giant size range (units)")]
        public Vector2 gasGiantSizeRange = new Vector2(80f, 150f);

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        /// <summary>
        /// Get a random sprite for a planet type.
        /// </summary>
        public Sprite GetRandomSprite(PlanetType planetType)
        {
            Sprite[] array = GetArrayForPlanetType(planetType);
            if (array == null || array.Length == 0) return null;
            return array[Random.Range(0, array.Length)];
        }

        /// <summary>
        /// Get a random sprite for a moon (uses habitable/nonHabitable based on type).
        /// </summary>
        public Sprite GetRandomMoonSprite(MoonType moonType)
        {
            // Ice moons could be habitable (water), others are non-habitable
            Sprite[] array = moonType switch
            {
                MoonType.Ice => habitable,  // Could support life
                _ => nonHabitable
            };

            if (array == null || array.Length == 0) return null;
            return array[Random.Range(0, array.Length)];
        }

        /// <summary>
        /// Get a random asteroid sprite.
        /// </summary>
        public Sprite GetRandomAsteroidSprite()
        {
            if (asteroids == null || asteroids.Length == 0) return null;
            return asteroids[Random.Range(0, asteroids.Length)];
        }

        /// <summary>
        /// Get the appropriate size for a planet.
        /// </summary>
        public float GetRandomPlanetSize(PlanetType planetType)
        {
            Vector2 range = planetType == PlanetType.Gas ? gasGiantSizeRange : planetSizeRange;
            return Random.Range(range.x, range.y);
        }

        /// <summary>
        /// Get the appropriate size for a moon.
        /// </summary>
        public float GetRandomMoonSize()
        {
            return Random.Range(moonSizeRange.x, moonSizeRange.y);
        }

        private Sprite[] GetArrayForPlanetType(PlanetType planetType)
        {
            return planetType switch
            {
                PlanetType.Gas => gasGiants,
                PlanetType.Terran => habitable,
                PlanetType.Ocean => habitable,
                PlanetType.Desert => nonHabitable,
                PlanetType.Ice => nonHabitable,
                PlanetType.Barren => nonHabitable,
                PlanetType.Volcanic => nonHabitable,
                _ => nonHabitable
            };
        }
    }
}
