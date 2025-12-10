using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace Starbelter.Core.Editor
{
    /// <summary>
    /// Custom property drawer that shows Position IDs as a dropdown.
    /// Filters based on sibling quartersType field if present.
    /// </summary>
    [CustomPropertyDrawer(typeof(PositionIdAttribute))]
    public class PositionIdDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            // Get all position IDs
            var positions = PositionRegistry.GetAll();
            if (positions == null || !positions.Any())
            {
                // Fallback to text field if registry not loaded
                EditorGUI.PropertyField(position, property, label);
                EditorGUI.HelpBox(
                    new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight, position.width, 20),
                    "PositionRegistry not loaded. Enter ID manually.",
                    MessageType.Warning);
                return;
            }

            // Try to find sibling quartersType field for filtering
            QuartersType? quartersFilter = null;
            var quartersTypeProp = property.serializedObject.FindProperty("quartersType");
            if (quartersTypeProp != null)
            {
                quartersFilter = (QuartersType)quartersTypeProp.enumValueIndex;
            }

            // Build options list with filtering
            var options = new List<string> { "(None)" };
            var displayNames = new List<string> { "(None)" };

            foreach (var pos in positions.OrderBy(p => p.Job.ToString()).ThenBy(p => p.DisplayName))
            {
                // Apply quarters filter
                if (quartersFilter.HasValue && quartersFilter.Value != QuartersType.Any)
                {
                    if (!PositionMatchesQuarters(pos, quartersFilter.Value))
                        continue;
                }

                options.Add(pos.Id);
                displayNames.Add($"{pos.Job}/{pos.DisplayName}");
            }

            // Find current selection
            int currentIndex = 0;
            string currentValue = property.stringValue;
            if (!string.IsNullOrEmpty(currentValue))
            {
                currentIndex = options.IndexOf(currentValue);
                if (currentIndex < 0)
                {
                    // Current value not in filtered list - add it with warning
                    var currentPos = PositionRegistry.Get(currentValue);
                    string displayName = currentPos != null
                        ? $"(MISMATCH) {currentPos.Job}/{currentPos.DisplayName}"
                        : $"(Unknown) {currentValue}";

                    options.Add(currentValue);
                    displayNames.Add(displayName);
                    currentIndex = options.Count - 1;
                }
            }

            // Draw dropdown
            EditorGUI.BeginProperty(position, label, property);
            int newIndex = EditorGUI.Popup(position, label.text, currentIndex, displayNames.ToArray());

            if (newIndex != currentIndex)
            {
                property.stringValue = newIndex == 0 ? "" : options[newIndex];
            }
            EditorGUI.EndProperty();
        }

        // Rank thresholds for senior positions
        private const int SeniorEnlistedMinRank = 7;  // E-7+ (Chiefs)
        private const int SeniorOfficerMinRank = 4;   // O-4+ (Lt Commander+)

        private bool PositionMatchesQuarters(Position pos, QuartersType quarters)
        {
            switch (quarters)
            {
                case QuartersType.Officer:
                    // Junior officers (O-1 to O-3)
                    return pos.IsOfficer && pos.Branch != ServiceBranch.Marine && pos.MinRank < SeniorOfficerMinRank;

                case QuartersType.SeniorOfficer:
                    // Senior officers (O-4+)
                    return pos.IsOfficer && pos.Branch != ServiceBranch.Marine && pos.MinRank >= SeniorOfficerMinRank;

                case QuartersType.Enlisted:
                    // Junior enlisted (E-1 to E-6)
                    return !pos.IsOfficer && pos.Branch != ServiceBranch.Marine && pos.MinRank < SeniorEnlistedMinRank;

                case QuartersType.SeniorEnlisted:
                    // Senior enlisted / Chiefs (E-7+)
                    return !pos.IsOfficer && pos.Branch != ServiceBranch.Marine && pos.MinRank >= SeniorEnlistedMinRank;

                case QuartersType.Marine:
                    return pos.Branch == ServiceBranch.Marine;

                case QuartersType.Any:
                default:
                    return true;
            }
        }
    }
}
