using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Space
{
    /// <summary>
    /// Base class for all space projectiles (lasers, missiles, bullets, etc.)
    /// Handles movement and implements ISpaceWeapon for damage dealing.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class SpaceProjectile : MonoBehaviour, ISpaceWeapon
    {
        [Header("Damage")]
        [SerializeField] protected float damage = 10f;
        [SerializeField] protected DamageType damageType = DamageType.Energy;

        [Header("Movement")]
        [SerializeField] protected float speed = 20f;
        [SerializeField] protected float lifetime = 5f;

        [Header("Effects")]
        [SerializeField] protected GameObject onHitPrefab;

        // Runtime
        protected Vector2 origin;
        protected float spawnTime;
        protected Rigidbody2D rb;

        // ISpaceWeapon implementation
        public float Damage => damage;
        public DamageType DamageType => damageType;
        public Vector2 Origin => origin;

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
        }

        protected virtual void Start()
        {
            origin = transform.position;
            spawnTime = Time.time;

            // Set initial velocity in facing direction (up is forward in 2D top-down)
            rb.linearVelocity = transform.up * speed;
        }

        protected virtual void Update()
        {
            // Destroy after lifetime expires
            if (Time.time - spawnTime > lifetime)
            {
                Destroy(gameObject);
            }
        }

        protected virtual void OnTriggerEnter2D(Collider2D collision)
        {
            // Don't hit other projectiles
            if (collision.CompareTag("SpaceWeapon"))
                return;

            //OnImpact(collision);
        }

        /// <summary>
        /// Called when projectile hits something. Override for custom behavior.
        /// </summary>
        public virtual void OnImpact()
        {
            // Spawn hit effect
            if (onHitPrefab != null)
            {
                Instantiate(onHitPrefab, transform.position, transform.rotation);
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Initialize the projectile with custom values (called by weapon that fires it).
        /// </summary>
        public virtual void Initialize(float damage, float speed, DamageType type)
        {
            this.damage = damage;
            this.speed = speed;
            this.damageType = type;
        }
    }
}
