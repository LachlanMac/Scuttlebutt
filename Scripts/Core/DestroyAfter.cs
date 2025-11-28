using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Destroys the GameObject after a specified time.
    /// Useful for cleaning up particle effects, sounds, etc.
    /// </summary>
    public class DestroyAfter : MonoBehaviour
    {
        [SerializeField] private float lifetime = 2f;

        private void Start()
        {
            Destroy(gameObject, lifetime);
        }
    }
}
