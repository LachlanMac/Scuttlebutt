using UnityEngine;

namespace Starbelter.Arena
{
    /// <summary>
    /// Links an Arena prefab to its corresponding SpaceView prefab.
    /// Add this to Arena prefabs that should spawn a space view.
    /// </summary>
    public class ArenaSpaceLinker : MonoBehaviour
    {
        [Header("Space View")]
        [Tooltip("The space view prefab to spawn with this arena")]
        [SerializeField] private GameObject spaceViewPrefab;

        public GameObject SpaceViewPrefab => spaceViewPrefab;
    }
}
