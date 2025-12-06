using UnityEngine;
using Starbelter.Core;
using Starbelter.AI;

namespace Starbelter.Combat
{
    /// <summary>
    /// Handles unit health, damage, and dodge calculations based on cover state.
    /// Delegates actual health tracking to Character data class.
    /// </summary>
    public class UnitHealth : MonoBehaviour
    {
        [Header("Effects")]
        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private GameObject dodgeEffectPrefab;
        [SerializeField] private GameObject deathEffectPrefab;

        // References
        private UnitController unitController;
        private Character character;

        // Events
        public System.Action<float, float> OnHealthChanged; // current, max
        public System.Action<float> OnDamageTaken;
        public System.Action<float, GameObject> OnDamageTakenWithAttacker; // damage, attacker
        public System.Action OnDeath;
        public System.Action OnDodge;
        public System.Action<Vector2> OnFlanked; // direction of flanking attack

        // Delegate to Character (default to "not dead" if character not yet assigned)
        public float CurrentHealth => character?.CurrentHealth ?? 0f;
        public float MaxHealth => character?.MaxHealth ?? 0f;
        public float HealthPercent => character?.HealthPercent ?? 0f;
        public bool IsDead => character?.IsDead ?? false;

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
            if (unitController != null)
            {
                character = unitController.Character;
            }

            // Initialize character health if not already done
            if (character != null && character.MaxHealth <= 0)
            {
                character.InitializeHealth();
            }

            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        /// <summary>
        /// Called when a projectile hits this unit.
        /// Returns true if damage was applied, false if dodged.
        /// </summary>
        /// <param name="coverPenetration">How well this shot penetrates cover. 1.0 = normal, less than 1.0 = penetrates better, greater than 1.0 = cover more effective</param>
        public bool TryApplyDamage(float damage, DamageType damageType, Vector2 projectileOrigin, Vector2 projectileDirection, GameObject attacker = null, float coverPenetration = 1.0f)
        {
            if (IsDead) return false;

            // Check if projectile actually crossed cover (raycast validation)
            bool hasCoverFromShot = CheckCoverFromProjectile(projectileOrigin);

            // Calculate dodge chance based on actual cover from this shot
            float dodgeChance = CalculateDodgeChance(hasCoverFromShot, coverPenetration);

            // Roll for dodge
            float roll = Random.value;
            bool dodged = roll < dodgeChance;

            if (dodged)
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

            // Register who hit us for aggro tracking and perception
            // PerceptionManager handles both threat tracking and awareness
            if (attacker != null && unitController != null && unitController.PerceptionManager != null)
            {
                unitController.PerceptionManager.RegisterEnemyShot(attacker, damage);
            }

            // Apply damage with mitigation
            ApplyDamage(damage, damageType);

            // Invoke event with attacker info
            OnDamageTakenWithAttacker?.Invoke(damage, attacker);

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
            if (character == null || IsDead) return;

            // Track HP before damage for threshold check
            float hpBefore = character.HealthPercent;

            float finalDamage = character.TakeDamage(damage, damageType);

            // Morale penalty for taking damage
            character.TakeMoraleDamage(10f);

            // Check for crossing 50% HP threshold (one-time penalty)
            if (hpBefore >= 0.5f && character.HealthPercent < 0.5f && !character.HasAppliedLowHealthMoralePenalty)
            {
                character.HasAppliedLowHealthMoralePenalty = true;
                character.TakeMoraleDamage(30f);
                Debug.Log($"[{unitController?.name ?? name}] Crossed 50% HP - morale -30. Now: {character.CurrentMorale:F0}");
            }

            // Spawn hit effect
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            }

            OnDamageTaken?.Invoke(finalDamage);
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

            if (IsDead)
            {
                Die();
            }
        }

        /// <summary>
        /// Apply damage directly (no mitigation, no dodge roll).
        /// </summary>
        public void ApplyDamage(float damage)
        {
            if (character == null || IsDead) return;

            // Track HP before damage for threshold check
            float hpBefore = character.HealthPercent;

            float finalDamage = character.TakeDamage(damage);

            // Morale penalty for taking damage
            character.TakeMoraleDamage(10f);

            // Check for crossing 50% HP threshold (one-time penalty)
            if (hpBefore >= 0.5f && character.HealthPercent < 0.5f && !character.HasAppliedLowHealthMoralePenalty)
            {
                character.HasAppliedLowHealthMoralePenalty = true;
                character.TakeMoraleDamage(30f);
                Debug.Log($"[{unitController?.name ?? name}] Crossed 50% HP - morale -30. Now: {character.CurrentMorale:F0}");
            }

            // Spawn hit effect
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            }

            OnDamageTaken?.Invoke(finalDamage);
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

            if (IsDead)
            {
                Die();
            }
        }

        /// <summary>
        /// Heal the unit.
        /// </summary>
        public void Heal(float amount)
        {
            if (character == null || IsDead) return;

            character.Heal(amount);
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        /// <summary>
        /// Calculate dodge chance based on whether projectile crossed cover and unit's movement state.
        /// Cover dodge chances (when bullet crossed cover):
        ///   - Ducking: 95% (very safe - hard to hit someone fully behind cover)
        ///   - Standing in place: 40% (moderate protection)
        ///   - Moving: 20% (dangerous - running through fire)
        /// Cover penetration modifies the cover bonus:
        ///   - Less than 1.0 = penetrates cover (aimed shots)
        ///   - Greater than 1.0 = cover more effective (suppression/burst)
        /// </summary>
        private float CalculateDodgeChance(bool hasCoverFromShot, float coverPenetration = 1.0f)
        {
            if (unitController == null) return 0f;

            float coverDodge = 0f;

            // Only get cover bonus if projectile actually crossed cover
            if (hasCoverFromShot)
            {
                bool isDucking = unitController.IsDucked;
                bool isMoving = unitController.Movement != null && unitController.Movement.IsMoving;

                if (isDucking)
                {
                    // Ducking behind cover - very safe (95%)
                    coverDodge = 0.95f;
                }
                else if (isMoving)
                {
                    // Moving through cover - dangerous (20%)
                    coverDodge = 0.20f;
                }
                else
                {
                    // Standing/peeking in cover - moderate protection (40%)
                    coverDodge = 0.40f;
                }
            }
            // No cover crossed = no cover dodge bonus (0%)

            // Apply cover penetration modifier
            // coverPenetration < 1.0 = better at hitting through cover (aimed shots)
            // coverPenetration > 1.0 = worse at hitting through cover (suppression/burst)
            coverDodge *= coverPenetration;

            float baseDodge = coverDodge;

            // Modify by reflex stat
            if (unitController.Character != null)
            {
                float reflexMod = Character.StatToModifier(unitController.Character.Reflex);
                baseDodge += reflexMod * 0.05f; // Reflex adds smaller bonus now (up to ~2.5%)
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
            string unitName = unitController != null ? unitController.name : gameObject.name;
            Debug.Log($"[{unitName}] DIED");

            // Spawn death effect
            if (deathEffectPrefab != null)
            {
                Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
            }

            OnDeath?.Invoke();

            // Notify squad for morale penalty (leader death = 2x)
            if (unitController != null && unitController.Squad != null)
            {
                unitController.Squad.OnAllyDeath(unitController);
            }

            // Find the root unit transform
            Transform rootTransform = transform;
            if (unitController != null)
            {
                rootTransform = unitController.transform;
            }
            else
            {
                var targetable = GetComponentInParent<ITargetable>();
                if (targetable != null)
                {
                    rootTransform = targetable.Transform;
                }
            }

            // Get character and team data before destroying
            Character character = unitController?.Character;
            Team team = unitController?.Team ?? Team.Neutral;

            // Create corpse with character data
            Corpse.Create(rootTransform, character, team);

            // Destroy the unit GameObject
            Destroy(rootTransform.gameObject);
        }
    }
}
