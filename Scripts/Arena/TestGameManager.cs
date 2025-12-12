using UnityEngine;
using Starbelter.Core;

namespace Starbelter.Arena
{
    /// <summary>
    /// Debug UI for camera controls during development.
    /// </summary>
    public class TestGameManager : MonoBehaviour
    {
        #region Debug UI

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 200, 200));

            GUILayout.Label("=== Camera Controls ===");

            if (CameraManager.Instance != null)
            {
                GUILayout.Label($"View: {CameraManager.Instance.CurrentView}");

                if (GUILayout.Button("Toggle View"))
                {
                    CameraManager.Instance.ToggleView();
                }

                // Floor switching (only shown in Arena view)
                if (CameraManager.Instance.CurrentView == ViewMode.Arena)
                {
                    GUILayout.Space(10);
                    GUILayout.Label($"Floor: {CameraManager.Instance.CurrentFloorIndex}");

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("▼ Down"))
                    {
                        CameraManager.Instance.FloorDown();
                    }
                    if (GUILayout.Button("▲ Up"))
                    {
                        CameraManager.Instance.FloorUp();
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("CameraManager not found");
            }

            GUILayout.EndArea();
        }

        #endregion
    }
}
