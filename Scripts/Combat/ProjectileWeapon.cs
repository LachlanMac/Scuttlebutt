using UnityEngine;

namespace Starbelter.Combat
{
    public enum ProjectileType
    {
        Kinetic,    // Bullets - fast, reliable
        Energy,     // Lasers - accurate, no drop
        Plasma      // Plasma bolts - slower, high damage
    }

    /// <summary>
    /// Data class for projectile weapons.
    /// </summary>
    [System.Serializable]
    public class ProjectileWeapon
    {
        [Header("Weapon Info")]
        public string Name;
        public ProjectileType Type;

        [Header("Combat Stats")]
        public float Damage = 10f;
        public float Accuracy = 1f;         // Multiplier on character accuracy (1.0 = normal)
        public float OptimalRange = 10f;    // Best accuracy at this range
        public float MaxRange = 15f;        // Can't hit beyond this

        [Header("Ammo")]
        public int MagazineSize = 10;       // Shots before reload needed
        public int CurrentAmmo;             // Shots remaining in magazine
        public float ReloadTime = 2f;       // Seconds to reload

        [Header("Prefab")]
        public GameObject ProjectilePrefab;

        public ProjectileWeapon()
        {
            CurrentAmmo = MagazineSize;
        }

        /// <summary>
        /// Returns true if weapon has ammo to fire.
        /// </summary>
        public bool CanFire => CurrentAmmo > 0;

        /// <summary>
        /// Returns true if magazine is empty.
        /// </summary>
        public bool NeedsReload => CurrentAmmo <= 0;

        /// <summary>
        /// Consume one shot. Returns false if no ammo.
        /// </summary>
        public bool ConsumeAmmo()
        {
            if (CurrentAmmo <= 0) return false;
            CurrentAmmo--;
            return true;
        }

        /// <summary>
        /// Reload the weapon to full magazine.
        /// </summary>
        public void Reload()
        {
            CurrentAmmo = MagazineSize;
        }

        /// <summary>
        /// Get accuracy modifier based on distance to target.
        /// Returns 1.0 at optimal range, lower at other distances.
        /// </summary>
        public float GetRangeAccuracyModifier(float distance)
        {
            if (distance > MaxRange) return 0f;

            // Perfect accuracy at optimal range, drops off at other distances
            float rangeDiff = Mathf.Abs(distance - OptimalRange);
            float maxDiff = Mathf.Max(OptimalRange, MaxRange - OptimalRange);

            if (maxDiff <= 0) return 1f;

            float dropoff = rangeDiff / maxDiff;
            return Mathf.Clamp01(1f - dropoff * 0.5f); // 50% max penalty
        }
    }
}
