using UnityEditor;
using System.IO;

namespace Starbelter.Editor
{
    /// <summary>
    /// Clears the Unity Editor log file when entering play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class ClearLogOnPlay
    {
        private static readonly string LogPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Unity", "Editor", "Editor.log"
        );

        static ClearLogOnPlay()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                ClearLog();
            }
        }

        private static void ClearLog()
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    File.WriteAllText(LogPath, string.Empty);
                    UnityEngine.Debug.Log("[ClearLogOnPlay] Editor log cleared.");
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"[ClearLogOnPlay] Failed to clear log: {e.Message}");
            }
        }
    }
}
