using UnityEngine;

namespace Starbelter.Combat
{
    public enum ProjectileType
    {
        Kinetic,    // Bullets - fast, reliable
        Energy,     // Lasers - accurate, no drop
        Plasma      // Plasma bolts - slower, high damage
    }

    public enum ShotType
    {
        Snap,       // Quick reactive shot - moderate accuracy
        Aimed,      // Careful aimed shot - high accuracy, penetrates cover
        Suppress,   // Suppressing fire - low accuracy, adds threat
        Burst       // Burst fire - multiple shots, close range
    }

    /// <summary>
    /// Data class for projectile weapons.
    /// Loaded from JSON - do not use Unity attributes like [Header].
    /// </summary>
    [System.Serializable]
    public class ProjectileWeapon
    {
        // --- Weapon Info ---
        public string Name;
        public ProjectileType Type;

        // --- Combat Stats ---
        public float Damage = 10f;
        public float Accuracy = 1f;         // Multiplier on character accuracy (1.0 = normal)
        public float OptimalRange = 10f;    // Best accuracy at this range
        public float MaxRange = 15f;        // Can't hit beyond this

        // --- Ammo ---
        public int MagazineSize = 10;       // Shots before reload needed
        public int CurrentAmmo;             // Shots remaining in magazine
        public float ReloadTime = 2f;       // Seconds to reload

        // --- Prefab (set at runtime) ---
        public GameObject ProjectilePrefab;

        // --- Snap Shot (quick, reactive) ---
        public float SnapAccuracy = 0.7f;
        public float SnapCoverPenetration = 1.0f;

        // --- Aimed Shot (careful, deadly) ---
        public bool CanAimedShot = true;
        public float AimTime = 1.5f;
        public float AimedAccuracy = 1.0f;
        public float AimedCoverPenetration = 0.5f;

        // --- Suppressing Fire (volume of fire) ---
        public bool CanSuppress = true;
        public float SuppressionEffectiveness = 1.0f;  // Threat multiplier when suppressing
        public float SuppressAccuracy = 0.5f;
        public float SuppressFireRateMultiplier = 2.0f;
        public float SuppressCoverPenetration = 1.5f;

        // --- Burst Fire (controlled burst) ---
        public bool CanBurst = false;
        public int BurstCount = 3;
        public float BurstDelay = 0.1f;
        public float BurstAccuracy = 0.8f;
        public float BurstCoverPenetration = 1.25f;

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
        /// Consume multiple shots (for burst fire). Returns actual shots consumed.
        /// </summary>
        public int ConsumeAmmo(int count)
        {
            int consumed = Mathf.Min(count, CurrentAmmo);
            CurrentAmmo -= consumed;
            return consumed;
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
