using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Ship
{
    public enum WeaponControlType
    {
        Pilot,          // Fired by whoever is flying
        WeaponsOfficer, // Fired from weapons console (crew/player at station)
        Automated       // AI-controlled, auto-targets
    }

    /// <summary>
    /// Individual weapon mount. Handles firing, aiming, and turret rotation.
    /// </summary>
    public class WeaponMount : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string weaponName = "Weapon";

        [Header("Projectile")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private Transform[] firePoints;

        [Header("Stats")]
        [SerializeField] private float fireRate = 0.2f;
        [SerializeField] private int maxAmmo = -1; // -1 = unlimited
        [SerializeField] private int currentAmmo = -1;

        [Header("Control")]
        [SerializeField] private WeaponControlType controlType = WeaponControlType.Pilot;
        [SerializeField] private int weaponGroup = 1;

        [Header("Turret")]
        [SerializeField] private bool isTurret = false;
        [SerializeField] private float arcAngle = 360f;
        [SerializeField] private float rotationSpeed = 90f;
        [SerializeField] private Transform turretPivot; // What rotates (if null, uses this transform)

        [Header("Automated Targeting")]
        [SerializeField] private float detectionRange = 50f;
        [SerializeField] private LayerMask targetMask;
        [SerializeField] private string[] targetTags = { "Enemy", "Missile" };

        // Runtime
        private float lastFireTime;
        private Transform currentTarget;
        private float baseAngle; // Starting angle for arc calculations

        // Properties
        public string WeaponName => weaponName;
        public WeaponControlType ControlType => controlType;
        public int WeaponGroup => weaponGroup;
        public bool HasAmmo => maxAmmo < 0 || currentAmmo > 0;
        public int CurrentAmmo => currentAmmo;
        public int MaxAmmo => maxAmmo;
        public bool IsTurret => isTurret;

        void Start()
        {
            if (currentAmmo < 0 && maxAmmo > 0)
                currentAmmo = maxAmmo;

            if (turretPivot == null)
                turretPivot = transform;

            baseAngle = turretPivot.localEulerAngles.z;
        }

        void Update()
        {
            if (controlType == WeaponControlType.Automated)
            {
                UpdateAutomated();
            }
        }

        #region Firing

        /// <summary>
        /// Attempt to fire this weapon. Returns true if fired.
        /// </summary>
        public bool Fire()
        {
            if (!CanFire())
                return false;

            lastFireTime = Time.time;

            if (maxAmmo > 0)
                currentAmmo--;

            SpawnProjectile();
            return true;
        }

        /// <summary>
        /// Check if weapon can fire right now.
        /// </summary>
        public bool CanFire()
        {
            if (projectilePrefab == null)
                return false;

            if (Time.time - lastFireTime < fireRate)
                return false;

            if (!HasAmmo)
                return false;

            return true;
        }

        private void SpawnProjectile()
        {
            if (firePoints == null || firePoints.Length == 0)
            {
                // No fire points defined, fire from this transform
                Instantiate(projectilePrefab, transform.position, transform.rotation);
                return;
            }

            // Fire from all fire points
            foreach (var firePoint in firePoints)
            {
                if (firePoint == null) continue;
                Instantiate(projectilePrefab, firePoint.position, firePoint.rotation);
            }
        }

        #endregion

        #region Turret Aiming

        /// <summary>
        /// Aim turret at a world position. Returns true if target is within arc.
        /// </summary>
        public bool AimAt(Vector3 targetPosition)
        {
            if (!isTurret)
                return true; // Fixed weapons always "aimed"

            Vector3 direction = targetPosition - turretPivot.position;
            float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

            // Check if within arc
            if (!IsAngleWithinArc(targetAngle))
                return false;

            // Rotate towards target
            float currentAngle = turretPivot.eulerAngles.z;
            float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, rotationSpeed * Time.deltaTime);
            turretPivot.rotation = Quaternion.Euler(0, 0, newAngle);

            // Return true if close enough to target angle
            return Mathf.Abs(Mathf.DeltaAngle(newAngle, targetAngle)) < 5f;
        }

        /// <summary>
        /// Set a target for this turret to track.
        /// </summary>
        public void SetTarget(Transform target)
        {
            currentTarget = target;
        }

        /// <summary>
        /// Clear current target.
        /// </summary>
        public void ClearTarget()
        {
            currentTarget = null;
        }

        private bool IsAngleWithinArc(float worldAngle)
        {
            if (arcAngle >= 360f)
                return true;

            // Get parent's rotation to calculate relative angle
            float parentAngle = transform.parent != null ? transform.parent.eulerAngles.z : 0f;
            float relativeTargetAngle = Mathf.DeltaAngle(parentAngle + baseAngle, worldAngle);

            return Mathf.Abs(relativeTargetAngle) <= arcAngle / 2f;
        }

        #endregion

        #region Automated

        private void UpdateAutomated()
        {
            // Find target if we don't have one
            if (currentTarget == null || !IsValidTarget(currentTarget))
            {
                currentTarget = FindBestTarget();
            }

            if (currentTarget == null)
                return;

            // Aim and fire
            bool onTarget = AimAt(currentTarget.position);
            if (onTarget && CanFire())
            {
                Fire();
            }
        }

        private Transform FindBestTarget()
        {
            Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, detectionRange, targetMask);

            Transform bestTarget = null;
            float bestDistance = float.MaxValue;

            foreach (var col in colliders)
            {
                if (!IsValidTarget(col.transform))
                    continue;

                // Check if within arc
                Vector3 direction = col.transform.position - transform.position;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                if (!IsAngleWithinArc(angle))
                    continue;

                float distance = direction.magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = col.transform;
                }
            }

            return bestTarget;
        }

        private bool IsValidTarget(Transform target)
        {
            if (target == null)
                return false;

            // Check if target has one of the valid tags
            foreach (var tag in targetTags)
            {
                if (target.CompareTag(tag))
                    return true;
            }

            return false;
        }

        #endregion

        #region Ammo

        /// <summary>
        /// Reload ammo.
        /// </summary>
        public void Reload(int amount = -1)
        {
            if (maxAmmo < 0)
                return; // Unlimited ammo weapon

            if (amount < 0)
                currentAmmo = maxAmmo;
            else
                currentAmmo = Mathf.Min(currentAmmo + amount, maxAmmo);
        }

        #endregion

        #region Debug

        void OnDrawGizmosSelected()
        {
            // Draw firing arc
            if (isTurret && arcAngle < 360f)
            {
                Vector3 pos = turretPivot != null ? turretPivot.position : transform.position;
                float parentAngle = transform.parent != null ? transform.parent.eulerAngles.z : 0f;
                float centerAngle = parentAngle + baseAngle;

                Gizmos.color = Color.yellow;

                float halfArc = arcAngle / 2f;
                Vector3 leftDir = Quaternion.Euler(0, 0, centerAngle + halfArc) * Vector3.right;
                Vector3 rightDir = Quaternion.Euler(0, 0, centerAngle - halfArc) * Vector3.right;

                Gizmos.DrawLine(pos, pos + leftDir * 5f);
                Gizmos.DrawLine(pos, pos + rightDir * 5f);
            }

            // Draw detection range for automated
            if (controlType == WeaponControlType.Automated)
            {
                Gizmos.color = new Color(1f, 0f, 0f, 0.2f);
                Gizmos.DrawWireSphere(transform.position, detectionRange);
            }
        }

        #endregion
    }
}
