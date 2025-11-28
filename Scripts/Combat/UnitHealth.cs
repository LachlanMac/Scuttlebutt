using UnityEngine;
using Starbelter.Core;
using Starbelter.AI;

namespace Starbelter.Combat
{
    /// <summary>
    /// Handles unit health, damage, and dodge calculations based on cover state.
    /// </summary>
    public class UnitHealth : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;

        [Header("Cover Dodge Chances")]
        [Tooltip("Dodge chance when in cover and not peeking")]
        [SerializeField] private float inCoverDodgeChance = 0.8f;

        [Tooltip("Dodge chance when in cover but peeking")]
        [SerializeField] private float peekingDodgeChance = 0.2f;

        [Header("Effects")]
        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private GameObject dodgeEffectPrefab;
        [SerializeField] private GameObject deathEffectPrefab;

        // References
        private UnitController unitController;

        // Events
        public System.Action<float, float> OnHealthChanged; // current, max
        public System.Action<float> OnDamageTaken;
        public System.Action OnDeath;
        public System.Action OnDodge;
        public System.Action<Vector2> OnFlanked; // direction of flanking attack

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float HealthPercent => maxHealth > 0 ? currentHealth / maxHealth : 0f;
        public bool IsDead => currentHealth <= 0;

        private void Awake()
        {
            // Find UnitController on this object or parent
            unitController = GetComponentInParent<UnitController>();
        }

        private void Start()
        {
            InitializeHealth();
        }

        private void InitializeHealth()
        {
            // Scale max health by character stat if available
            if (unitController != null && unitController.Character != null)
            {
                float healthMultiplier = Character.StatToMultiplier(unitController.Character.Health);
                maxHealth = 50f + (healthMultiplier * 100f); // 50-150 range
            }

            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        /// <summary>
        /// Called when a projectile hits this unit.
        /// Returns true if damage was applied, false if dodged.
        /// </summary>
        public bool TryApplyDamage(float damage, DamageType damageType, Vector2 projectileOrigin, Vector2 projectileDirection)
        {
            if (IsDead) return false;

            // Check if projectile actually crossed cover (raycast validation)
            bool hasCoverFromShot = CheckCoverFromProjectile(projectileOrigin);

            // Calculate dodge chance based on actual cover from this shot
            float dodgeChance = CalculateDodgeChance(hasCoverFromShot);

            // Roll for dodge
            if (Random.value < dodgeChance)
            {
                OnDodged();
                return false;
            }

            // If we got hit and had no cover from this angle, we're being flanked
            if (!hasCoverFromShot && unitController != null)
            {
                Vector2 flankDirection = (projectileOrigin - (Vector2)unitController.transform.position).normalized;
                OnFlanked?.Invoke(flankDirection);
            }

            // Apply damage with mitigation
            ApplyDamage(damage, damageType);
            return true;
        }

        /// <summary>
        /// Raycast from projectile origin to unit to check if cover was actually crossed.
        /// </summary>
        private bool CheckCoverFromProjectile(Vector2 projectileOrigin)
        {
            if (unitController == null) return false;

            Vector2 unitPos = unitController.transform.position;
            Vector2 direction = unitPos - projectileOrigin;
            float distance = direction.magnitude;

            // Raycast from projectile origin toward unit, looking for Structure (cover)
            RaycastHit2D[] hits = Physics2D.RaycastAll(projectileOrigin, direction.normalized, distance);

            foreach (var hit in hits)
            {
                // Skip self
                if (hit.collider.transform.IsChildOf(unitController.transform)) continue;
                if (hit.collider.transform == unitController.transform) continue;

                // Check if we hit a Structure (cover)
                var structure = hit.collider.GetComponent<Structure>();
                if (structure != null)
                {
                    return true; // Cover was between projectile and unit
                }
            }

            return false; // No cover crossed
        }

        /// <summary>
        /// Apply damage with damage type mitigation from Character.
        /// </summary>
        public void ApplyDamage(float damage, DamageType damageType)
        {
            if (IsDead) return;

            // Apply mitigation from character's armor/gear
            float finalDamage = damage;
            if (unitController != null && unitController.Character != null)
            {
                float mitigation = unitController.Character.GetMitigation(damageType) / 100f;
                finalDamage = damage * (1f - mitigation);
            }

            currentHealth -= finalDamage;
            currentHealth = Mathf.Max(0, currentHealth);

            // Spawn hit effect
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            }

            OnDamageTaken?.Invoke(finalDamage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            if (currentHealth <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Apply damage directly (no mitigation, no dodge roll).
        /// </summary>
        public void ApplyDamage(float damage)
        {
            if (IsDead) return;

            currentHealth -= damage;
            currentHealth = Mathf.Max(0, currentHealth);

            // Spawn hit effect
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            }

            OnDamageTaken?.Invoke(damage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            if (currentHealth <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Heal the unit.
        /// </summary>
        public void Heal(float amount)
        {
            if (IsDead) return;

            currentHealth += amount;
            currentHealth = Mathf.Min(currentHealth, maxHealth);

            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        /// <summary>
        /// Calculate dodge chance based on whether projectile crossed cover.
        /// </summary>
        private float CalculateDodgeChance(bool hasCoverFromShot)
        {
            if (unitController == null) return 0f;

            bool isPeeking = unitController.IsPeeking;

            float baseDodge = 0f;

            // Only get cover bonus if projectile actually crossed cover
            if (hasCoverFromShot && !isPeeking)
            {
                baseDodge = inCoverDodgeChance;
            }
            else if (hasCoverFromShot && isPeeking)
            {
                baseDodge = peekingDodgeChance;
            }
            // No cover crossed = no cover dodge bonus

            // Modify by reflex stat
            if (unitController.Character != null)
            {
                float reflexMod = Character.StatToModifier(unitController.Character.Reflex);
                baseDodge += reflexMod * 0.2f; // Reflex can add/subtract up to 10% dodge
            }

            return Mathf.Clamp01(baseDodge);
        }

        private void OnDodged()
        {
            // Spawn dodge/miss effect
            if (dodgeEffectPrefab != null)
            {
                Instantiate(dodgeEffectPrefab, transform.position, Quaternion.identity);
            }

            OnDodge?.Invoke();
        }

        private void Die()
        {
            // Spawn death effect
            if (deathEffectPrefab != null)
            {
                Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
            }

            OnDeath?.Invoke();

            // Destroy the unit
            Destroy(gameObject);
        }
    }
}
