using UnityEngine;
using UnityEditor;
using System.IO;
using Starbelter.Strategic;

/// <summary>
/// Editor window for generating and managing galaxy data.
/// </summary>
public class GalaxyGeneratorEditor : EditorWindow
{
    private int seed = 12345;
    private string outputPath;

    [MenuItem("Starbelter/Galaxy Generator")]
    public static void ShowWindow()
    {
        GetWindow<GalaxyGeneratorEditor>("Galaxy Generator");
    }

    private void OnEnable()
    {
        // Default output to StreamingAssets/Galaxy
        outputPath = Path.Combine(Application.streamingAssetsPath, "Galaxy");
    }

    private void OnGUI()
    {
        GUILayout.Label("Galaxy Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        seed = EditorGUILayout.IntField("Seed", seed);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output Path:", outputPath);

        if (GUILayout.Button("Browse Output Folder"))
        {
            string selected = EditorUtility.OpenFolderPanel("Select Output Folder", outputPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                outputPath = selected;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This will generate a 10x10 galaxy (100 sectors) with:\n" +
            "- 5-10 planets per sector\n" +
            "- Moons, asteroid belts, nebulae\n" +
            "- Faction homeworlds at set positions\n\n" +
            "Output: galaxy.json + sectors/*.json",
            MessageType.Info
        );

        EditorGUILayout.Space();

        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Generate Galaxy", GUILayout.Height(40)))
        {
            GenerateGalaxy();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();

        GUILayout.Label("Utilities", EditorStyles.boldLabel);

        if (GUILayout.Button("Open Galaxy Folder"))
        {
            if (Directory.Exists(outputPath))
            {
                EditorUtility.RevealInFinder(outputPath);
            }
            else
            {
                EditorUtility.DisplayDialog("Not Found", "Galaxy folder doesn't exist yet. Generate a galaxy first!", "OK");
            }
        }

        if (GUILayout.Button("Validate Galaxy Files"))
        {
            ValidateGalaxy();
        }

        if (GUILayout.Button("Random Seed"))
        {
            seed = Random.Range(1, 999999);
        }
    }

    private void GenerateGalaxy()
    {
        // Ensure StreamingAssets exists
        if (!Directory.Exists(Application.streamingAssetsPath))
        {
            Directory.CreateDirectory(Application.streamingAssetsPath);
        }

        // Confirm if files already exist
        string galaxyFile = Path.Combine(outputPath, "galaxy.json");
        if (File.Exists(galaxyFile))
        {
            if (!EditorUtility.DisplayDialog(
                "Overwrite?",
                "Galaxy files already exist. This will overwrite them.",
                "Overwrite",
                "Cancel"))
            {
                return;
            }
        }

        EditorUtility.DisplayProgressBar("Generating Galaxy", "Creating sectors...", 0.5f);

        try
        {
            GalaxyGenerator.GenerateGalaxy(seed, outputPath);

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog(
                "Success",
                $"Galaxy generated successfully!\n\nSeed: {seed}\nPath: {outputPath}",
                "OK"
            );

            AssetDatabase.Refresh();
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Error", $"Failed to generate galaxy:\n{e.Message}", "OK");
            Debug.LogException(e);
        }
    }

    private void ValidateGalaxy()
    {
        string galaxyFile = Path.Combine(outputPath, "galaxy.json");
        if (!File.Exists(galaxyFile))
        {
            EditorUtility.DisplayDialog("Not Found", "No galaxy.json found. Generate a galaxy first!", "OK");
            return;
        }

        int sectorCount = 0;
        int poiCount = 0;
        string sectorsPath = Path.Combine(outputPath, "sectors");

        if (Directory.Exists(sectorsPath))
        {
            var files = Directory.GetFiles(sectorsPath, "sector_*.json");
            sectorCount = files.Length;

            foreach (var file in files)
            {
                string json = File.ReadAllText(file);
                // Count "poiType" occurrences as rough POI count
                int idx = 0;
                while ((idx = json.IndexOf("\"poiType\"", idx + 1)) >= 0)
                {
                    poiCount++;
                }
            }
        }

        EditorUtility.DisplayDialog(
            "Validation",
            $"Galaxy files found!\n\n" +
            $"Sectors: {sectorCount}/100\n" +
            $"Total POIs: ~{poiCount}\n" +
            $"Avg POIs/sector: ~{(sectorCount > 0 ? poiCount / sectorCount : 0)}",
            "OK"
        );
    }
}
