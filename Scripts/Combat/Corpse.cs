using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Combat
{
    /// <summary>
    /// Simple component attached to corpse objects.
    /// Holds the Character data of the deceased unit for later reference (looting, identification, etc.).
    /// </summary>
    public class Corpse : MonoBehaviour
    {
        [Header("Deceased Info")]
        [SerializeField] private Character character;
        [SerializeField] private Team team;
        [SerializeField] private float timeOfDeath;

        public Character Character => character;
        public Team Team => team;
        public float TimeOfDeath => timeOfDeath;
        public float TimeSinceDeath => Time.time - timeOfDeath;

        /// <summary>
        /// Initialize the corpse with data from the dead unit.
        /// </summary>
        public void Initialize(Character sourceCharacter, Team sourceTeam)
        {
            // Copy character data (don't keep reference to original)
            if (sourceCharacter != null)
            {
                character = new Character(
                    sourceCharacter.Name,
                    sourceCharacter.Fitness,
                    sourceCharacter.Accuracy,
                    sourceCharacter.Reflexes,
                    sourceCharacter.Bravery,
                    sourceCharacter.Perception,
                    sourceCharacter.Stealth
                );
                // Copy runtime health state
                character.MaxHealth = sourceCharacter.MaxHealth;
                character.CurrentHealth = sourceCharacter.CurrentHealth;
            }
            else
            {
                character = new Character();
                character.Name = "Unknown";
            }

            team = sourceTeam;
            timeOfDeath = Time.time;
        }

        /// <summary>
        /// Create a corpse from a dying unit.
        /// </summary>
        public static Corpse Create(Transform unitTransform, Character character, Team team)
        {
            // Get visual info from the unit before destroying it
            var unitSR = unitTransform.GetComponentInChildren<SpriteRenderer>();

            // Create corpse GameObject
            var corpseGO = new GameObject($"Corpse_{character?.Name ?? "Unknown"}");
            corpseGO.transform.position = unitTransform.position;
            corpseGO.tag = "Untagged"; // Ensure corpses don't show up in unit scans
            corpseGO.layer = LayerMask.NameToLayer("Default");

            // Add SpriteRenderer and copy settings from unit
            var sr = corpseGO.AddComponent<SpriteRenderer>();
            if (unitSR != null)
            {
                sr.sprite = unitSR.sprite;
                sr.color = unitSR.color * 0.6f; // Darken to indicate death
                sr.sortingLayerID = unitSR.sortingLayerID;
                sr.sortingOrder = unitSR.sortingOrder - 1; // Behind live units
                sr.flipX = unitSR.flipX;
                sr.flipY = unitSR.flipY;
                sr.material = unitSR.material;
            }
            else
            {
                sr.color = Color.gray * 0.6f;
            }

            // Rotate to show they're dead (randomly left or right)
            float rotation = Random.value > 0.5f ? 90f : -90f;
            corpseGO.transform.rotation = Quaternion.Euler(0, 0, rotation);

            // Add Corpse component and initialize
            var corpse = corpseGO.AddComponent<Corpse>();
            corpse.Initialize(character, team);

            return corpse;
        }
    }
}
