using UnityEngine;
using Starbelter.Combat;

namespace Starbelter.Core
{
    /// <summary>
    /// Basic player controller for testing. WASD movement, click to shoot.
    /// </summary>
    public class PlayerController : MonoBehaviour, ITargetable
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;

        [Header("Shooting")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Team team = Team.Empire;
        [SerializeField] private float fireRate = 0.2f;

        private Camera mainCamera;
        private float nextFireTime;
        private UnitHealth unitHealth;

        // ITargetable implementation
        public Team Team => team;
        public Transform Transform => transform;
        public bool IsDead => unitHealth != null && unitHealth.IsDead;

        private void Start()
        {
            mainCamera = Camera.main;
            unitHealth = GetComponentInChildren<UnitHealth>();
        }

        private void Update()
        {
            HandleMovement();
            HandleShooting();
        }

        private void HandleMovement()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            Vector3 direction = new Vector3(horizontal, vertical, 0f).normalized;
            transform.position += direction * moveSpeed * Time.deltaTime;
        }

        private void HandleShooting()
        {
            if (Input.GetMouseButton(0) && Time.time >= nextFireTime)
            {
                Shoot();
                nextFireTime = Time.time + fireRate;
            }
        }

        private void Shoot()
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning("[PlayerController] No projectile prefab assigned!");
                return;
            }

            if (mainCamera == null) return;

            // Get direction toward cursor
            Vector3 mousePos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = 0f;
            Vector2 direction = (mousePos - transform.position).normalized;

            // Spawn projectile
            GameObject projectileObj = Instantiate(projectilePrefab, transform.position, Quaternion.identity);

            // Initialize projectile
            Projectile projectile = projectileObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.Fire(direction, team);
            }
            else
            {
                Debug.LogWarning("[PlayerController] Projectile prefab missing Projectile component!");
                Destroy(projectileObj);
            }
        }
    }
}
