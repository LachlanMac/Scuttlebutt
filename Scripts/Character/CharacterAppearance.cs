using UnityEngine;
using UnityEngine.U2D.Animation;

namespace Starbelter.Unit
{
    public class CharacterAppearance : MonoBehaviour
    {
        [Header("Body Sprite Libraries")]
        [SerializeField] private SpriteLibraryAsset maleLibrary;
        [SerializeField] private SpriteLibraryAsset femaleLibrary;

        [Header("Hair Sprite Libraries")]
        [SerializeField] private SpriteLibraryAsset[] maleHairLibraries;
        [SerializeField] private SpriteLibraryAsset[] femaleHairLibraries;

        [Header("References")]
        [SerializeField] private SpriteLibrary spriteLibrary;
        [SerializeField] private SpriteLibrary hairSpriteLibrary;
        [SerializeField] private SpriteRenderer legsRenderer;
        [SerializeField] private SpriteRenderer armsRenderer;
        [SerializeField] private SpriteRenderer torsoRenderer;
        [SerializeField] private SpriteRenderer headRenderer;
        [SerializeField] private SpriteRenderer hairRenderer;

        // Skin tones - realistic human range
        public static readonly Color[] SkinTones = new Color[]
        {
            new Color(0.98f, 0.89f, 0.78f),  // Caucasian - fair
            new Color(0.94f, 0.82f, 0.71f),  // Caucasian - medium
            new Color(0.87f, 0.73f, 0.60f),  // Caucasian - olive
            new Color(0.96f, 0.87f, 0.70f),  // Asian - light
            new Color(0.91f, 0.78f, 0.58f),  // Asian - medium
            new Color(0.82f, 0.64f, 0.47f),  // Hispanic
            new Color(0.55f, 0.38f, 0.26f),  // African - light
            new Color(0.36f, 0.25f, 0.18f),  // African - dark
        };

        private bool isMale;
        private int skinToneIndex;
        private int hairStyleIndex;
        private Color torsoColor = Color.white;
        private Color legsColor = Color.white;
        private Color hairColor = Color.white;

        public void Initialize(bool male, int skinIndex, int hairStyle, Color torso, Color legs, Color hair)
        {
            isMale = male;
            skinToneIndex = Mathf.Clamp(skinIndex, 0, SkinTones.Length - 1);
            hairStyleIndex = hairStyle;
            torsoColor = torso;
            legsColor = legs;
            hairColor = hair;

            ApplyAppearance();
        }

        public void Initialize(bool male)
        {
            isMale = male;
            skinToneIndex = Random.Range(0, SkinTones.Length);
            hairStyleIndex = Random.Range(0, GetHairLibraryCount());
            torsoColor = Color.white;
            legsColor = Color.white;
            hairColor = GetRandomHairColor();

            ApplyAppearance();
        }

        public void Initialize(bool male, int skinIndex, int hairStyle)
        {
            isMale = male;
            skinToneIndex = Mathf.Clamp(skinIndex, 0, SkinTones.Length - 1);
            hairStyleIndex = hairStyle;
            torsoColor = Color.white;
            legsColor = Color.white;
            hairColor = GetRandomHairColor();

            ApplyAppearance();
        }

        public void Initialize(bool male, int skinIndex, int hairStyle, int hairColorIndex)
        {
            isMale = male;
            skinToneIndex = Mathf.Clamp(skinIndex, 0, SkinTones.Length - 1);
            hairStyleIndex = hairStyle;
            torsoColor = Color.white;
            legsColor = Color.white;
            hairColor = GetHairColor(hairColorIndex);

            ApplyAppearance();
        }

        public void SetGender(bool male)
        {
            isMale = male;
            ApplyLibrary();
            ApplyHairLibrary();
        }

        public void SetSkinTone(int index)
        {
            skinToneIndex = Mathf.Clamp(index, 0, SkinTones.Length - 1);
            ApplySkinColor();
        }

        public void SetHairStyle(int index)
        {
            hairStyleIndex = Mathf.Clamp(index, 0, GetHairLibraryCount() - 1);
            ApplyHairLibrary();
        }

        public void SetTorsoColor(Color color)
        {
            torsoColor = color;
            if (torsoRenderer != null)
                torsoRenderer.color = color;
        }

        public void SetLegsColor(Color color)
        {
            legsColor = color;
            if (legsRenderer != null)
                legsRenderer.color = color;
        }

        public void SetHairColor(Color color)
        {
            hairColor = color;
            if (hairRenderer != null)
                hairRenderer.color = color;
        }

        private void ApplyAppearance()
        {
            ApplyLibrary();
            ApplyHairLibrary();
            ApplySkinColor();
            ApplyClothingColors();
            ApplyHairColor();
        }

        private void ApplyLibrary()
        {
            if (spriteLibrary == null)
                return;

            spriteLibrary.spriteLibraryAsset = isMale ? maleLibrary : femaleLibrary;
        }

        private void ApplyHairLibrary()
        {
            if (hairSpriteLibrary == null)
                return;

            var libraries = isMale ? maleHairLibraries : femaleHairLibraries;
            if (libraries == null || libraries.Length == 0)
                return;

            int index = Mathf.Clamp(hairStyleIndex, 0, libraries.Length - 1);
            hairSpriteLibrary.spriteLibraryAsset = libraries[index];
        }

        private int GetHairLibraryCount()
        {
            var libraries = isMale ? maleHairLibraries : femaleHairLibraries;
            return libraries != null ? libraries.Length : 0;
        }

        private void ApplySkinColor()
        {
            Color skin = SkinTones[skinToneIndex];

            if (headRenderer != null)
                headRenderer.color = skin;
            if (armsRenderer != null)
                armsRenderer.color = skin;
        }

        private void ApplyClothingColors()
        {
            if (torsoRenderer != null)
                torsoRenderer.color = torsoColor;
            if (legsRenderer != null)
                legsRenderer.color = legsColor;
        }

        private void ApplyHairColor()
        {
            if (hairRenderer != null)
                hairRenderer.color = hairColor;
        }

        // Hair colors - common natural shades
        public static readonly Color[] HairColors = new Color[]
        {
            new Color(0.10f, 0.07f, 0.05f),  // Black
            new Color(0.26f, 0.15f, 0.09f),  // Dark brown
            new Color(0.45f, 0.30f, 0.18f),  // Brown
            new Color(0.65f, 0.50f, 0.30f),  // Light brown
            new Color(0.85f, 0.65f, 0.35f),  // Blonde
            new Color(0.55f, 0.22f, 0.12f),  // Auburn
            new Color(0.70f, 0.28f, 0.15f),  // Red
            new Color(0.40f, 0.40f, 0.40f),  // Gray
        };

        public static Color GetHairColor(int index)
        {
            return HairColors[Mathf.Clamp(index, 0, HairColors.Length - 1)];
        }

        private Color GetRandomHairColor()
        {
            return HairColors[Random.Range(0, HairColors.Length)];
        }

        public static Color GetRandomSkinTone()
        {
            return SkinTones[Random.Range(0, SkinTones.Length)];
        }
    }
}
