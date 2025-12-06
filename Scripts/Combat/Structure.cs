using UnityEngine;
using Starbelter.Core;
using Starbelter.Pathfinding;

namespace Starbelter.Combat
{
    /// <summary>
    /// Unified controller for structures (cover, buildings, obstacles).
    /// Handles cover blocking, health, and destruction.
    ///
    /// Setup: Single solid collider (Is Trigger = false) for pathfinding and unit collision.
    /// Projectiles (with trigger colliders) will query this Structure to check if blocked.
    /// </summary>
    public class Structure : MonoBehaviour
    {
        [Header("Cover")]
        [Tooltip("Type of cover this structure provides")]
        [SerializeField] private CoverType coverType = CoverType.Half;

        [Tooltip("Chance to block incoming projectiles (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float blockChance = 0.5f;

        [Header("Health")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;

        [Header("Damage Mitigation (0-100%)")]
        [Range(0f, 100f)]
        [SerializeField] private float physicalMitigation = 0f;
        [Range(0f, 100f)]
        [SerializeField] private float heatMitigation = 0f;
        [Range(0f, 100f)]
        [SerializeField] private float energyMitigation = 0f;
        [Range(0f, 100f)]
        [SerializeField] private float ionMitigation = 0f;

        [Header("Effects")]
        [Tooltip("Spawned when projectile is blocked")]
        [SerializeField] private GameObject blockEffectPrefab;

        [Tooltip("Spawned when structure is destroyed")]
        [SerializeField] private GameObject destroyEffectPrefab;

        [Header("Pathfinding")]
        [Tooltip("Radius to update A* graph when destroyed")]
        [SerializeField] private float graphUpdateRadius = 3f;

        public CoverType CoverType => coverType;
        public float BlockChance => blockChance;
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float HealthPercent => maxHealth > 0 ? currentHealth / maxHealth : 0f;

        private void Awake()
        {
            currentHealth = maxHealth;
        }

        /// <summary>
        /// Called by Projectile to check if it should be blocked.
        /// Returns true if blocked (projectile should be destroyed).
        /// Cover penetration modifies block chance (lower = penetrates better).
        /// </summary>
        public bool TryBlockProjectile(Projectile projectile)
        {
            // Apply cover penetration to block chance
            // coverPenetration < 1.0 = penetrates better (aimed shots)
            // coverPenetration > 1.0 = blocked more easily (suppression/burst)
            float effectiveBlockChance = blockChance * projectile.CoverPenetration;
            effectiveBlockChance = Mathf.Clamp01(effectiveBlockChance);

            float roll = Random.value;
            bool blocked = roll <= effectiveBlockChance;

            // Record for projectile's consolidated report
            projectile.RecordCoverEncounter(name, blocked, effectiveBlockChance);

            if (blocked)
            {
                // Spawn block effect
                if (blockEffectPrefab != null)
                {
                    Instantiate(blockEffectPrefab, projectile.transform.position, Quaternion.identity);
                }

                // Take damage from blocked projectile (after mitigation)
                TakeDamage(projectile.Damage, projectile.DamageType);
            }

            return blocked;
        }

        /// <summary>
        /// Get the mitigation percentage for a specific damage type.
        /// </summary>
        public float GetMitigation(DamageType damageType)
        {
            return damageType switch
            {
                DamageType.Physical => physicalMitigation,
                DamageType.Heat => heatMitigation,
                DamageType.Energy => energyMitigation,
                DamageType.Ion => ionMitigation,
                _ => 0f
            };
        }

        /// <summary>
        /// Apply damage to the structure with damage type mitigation.
        /// </summary>
        public void TakeDamage(float damage, DamageType damageType)
        {
            float mitigation = GetMitigation(damageType) / 100f;
            float finalDamage = damage * (1f - mitigation);

            currentHealth -= finalDamage;

            if (currentHealth <= 0)
            {
                OnDestroyed();
            }
        }

        /// <summary>
        /// Apply damage to the structure (no mitigation).
        /// </summary>
        public void TakeDamage(float damage)
        {
            currentHealth -= damage;

            if (currentHealth <= 0)
            {
                OnDestroyed();
            }
        }

        /// <summary>
        /// Instantly destroy the structure.
        /// </summary>
        public void DestroyImmediate()
        {
            currentHealth = 0;
            OnDestroyed();
        }

        private void OnDestroyed()
        {
            // Spawn destruction effect
            if (destroyEffectPrefab != null)
            {
                Instantiate(destroyEffectPrefab, transform.position, Quaternion.identity);
            }

            // Notify game manager to update pathfinding and cover
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnCoverDestroyed(gameObject, transform.position, graphUpdateRadius);
            }

            Destroy(gameObject);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Visualize cover type
            switch (coverType)
            {
                case CoverType.Half:
                    Gizmos.color = new Color(1f, 1f, 0f, 0.3f); // Yellow
                    break;
                case CoverType.Full:
                    Gizmos.color = new Color(0f, 0f, 1f, 0.3f); // Blue
                    break;
                default:
                    Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f); // Gray
                    break;
            }

            var collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
            }
        }
#endif
    }
}
