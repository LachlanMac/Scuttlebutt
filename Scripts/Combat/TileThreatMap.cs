using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using Starbelter.Core;

namespace Starbelter.Combat
{
    /// <summary>
    /// Tracks threat levels per tile based on projectile activity.
    /// Visualizes threat as a colored overlay tilemap.
    /// Attach to GameManager or a dedicated ThreatMap object.
    /// </summary>
    public class TileThreatMap : MonoBehaviour
    {
        public static TileThreatMap Instance { get; private set; }

        [Header("Tilemap Reference")]
        [Tooltip("The tilemap used to visualize threat levels")]
        [SerializeField] private Tilemap threatTilemap;

        [Header("Tile Visuals")]
        [Tooltip("Base tile to use for threat visualization (will be tinted)")]
        [SerializeField] private TileBase threatTile;

        [Header("Threat Settings")]
        [Tooltip("Threat added per point of projectile damage (damage / divisor)")]
        [SerializeField] private float damageToDivisor = 10f;

        [Tooltip("Threat bleed to adjacent NSEW tiles (multiplier of main threat)")]
        [Range(0f, 1f)]
        [SerializeField] private float adjacentBleedMultiplier = 0.25f;

        [Tooltip("Maximum threat a tile can accumulate")]
        [SerializeField] private float maxThreat = 30f;

        [Header("Decay Settings")]
        [Tooltip("Base decay rate per second (at low threat levels)")]
        [SerializeField] private float baseDecayRate = 1f;

        [Tooltip("Decay multiplier at high threat (20+)")]
        [SerializeField] private float highThreatDecayMultiplier = 3f;

        [Tooltip("Threat level considered 'high' for fast decay")]
        [SerializeField] private float highThreatThreshold = 20f;

        [Tooltip("Threat level considered 'low' for normal decay")]
        [SerializeField] private float lowThreatThreshold = 5f;

        [Header("Color Settings")]
        [Tooltip("Base opacity for threat tiles")]
        [Range(0f, 1f)]
        [SerializeField] private float baseOpacity = 0.2f;

        [Tooltip("Max opacity at highest threat")]
        [Range(0f, 1f)]
        [SerializeField] private float maxOpacity = 0.6f;

        [Tooltip("Threat level for maximum color intensity")]
        [SerializeField] private float maxThreatForColor = 30f;

        [Header("Visualization")]
        [Tooltip("Which team's threat view to display (threat TO this team)")]
        [SerializeField] private Team displayTeam = Team.Federation;

        // Threat data per tile, per team
        // Key: team that is THREATENED (not the shooter)
        private Dictionary<Team, Dictionary<Vector3Int, TileData>> teamThreats = new Dictionary<Team, Dictionary<Vector3Int, TileData>>();

        // Tiles to remove after decay
        private List<Vector3Int> tilesToRemove = new List<Vector3Int>();

        private struct TileData
        {
            public float threat;
            public float lastUpdated;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            // Initialize threat dictionaries for each team
            teamThreats[Team.Federation] = new Dictionary<Vector3Int, TileData>();
            teamThreats[Team.Empire] = new Dictionary<Vector3Int, TileData>();
        }

        private void Update()
        {
            DecayAllThreats();
        }

        /// <summary>
        /// Add threat to a single tile for a specific team (no bleed).
        /// </summary>
        private void AddThreatDirect(Vector3Int tile, float amount, Team threatenedTeam)
        {
            if (!teamThreats.TryGetValue(threatenedTeam, out var tileThreats))
                return;

            if (!tileThreats.TryGetValue(tile, out var data))
            {
                data = new TileData { threat = 0f, lastUpdated = Time.time };
            }

            data.threat = Mathf.Min(data.threat + amount, maxThreat);
            data.lastUpdated = Time.time;
            tileThreats[tile] = data;

            // Only update visual if this is the displayed team
            if (threatenedTeam == displayTeam)
            {
                UpdateTileVisual(tile, data.threat);
            }
        }

        /// <summary>
        /// Add threat to a tile with bleed to adjacent NSEW tiles.
        /// </summary>
        public void AddThreat(Vector3Int tile, float amount, Team threatenedTeam)
        {
            // Main tile gets full threat
            AddThreatDirect(tile, amount, threatenedTeam);

            // Adjacent tiles get bleed
            if (adjacentBleedMultiplier > 0f)
            {
                float bleedAmount = amount * adjacentBleedMultiplier;
                AddThreatDirect(tile + Vector3Int.up, bleedAmount, threatenedTeam);
                AddThreatDirect(tile + Vector3Int.down, bleedAmount, threatenedTeam);
                AddThreatDirect(tile + Vector3Int.left, bleedAmount, threatenedTeam);
                AddThreatDirect(tile + Vector3Int.right, bleedAmount, threatenedTeam);
            }
        }

        /// <summary>
        /// Add threat along a path from one position to another.
        /// Called by projectiles as they move.
        /// </summary>
        /// <param name="sourceTeam">Team that fired the projectile (threat applies to OPPOSING team)</param>
        public void AddThreatAlongPath(Vector3 from, Vector3 to, float projectileDamage, Team sourceTeam)
        {
            // Determine which team is threatened (opposite of shooter)
            Team threatenedTeam = GetOpposingTeam(sourceTeam);
            if (threatenedTeam == Team.Neutral) return; // Neutral projectiles don't create threat

            float threatAmount = projectileDamage / damageToDivisor;
            var tiles = GetTilesCrossed(from, to);

            foreach (var tile in tiles)
            {
                AddThreat(tile, threatAmount, threatenedTeam);
            }
        }

        /// <summary>
        /// Get the opposing team.
        /// </summary>
        private Team GetOpposingTeam(Team team)
        {
            return team switch
            {
                Team.Federation => Team.Empire,
                Team.Empire => Team.Federation,
                _ => Team.Neutral
            };
        }

        /// <summary>
        /// Get the current threat level at a tile for a specific team.
        /// </summary>
        public float GetThreat(Vector3Int tile, Team team)
        {
            if (!teamThreats.TryGetValue(team, out var tileThreats))
                return 0f;
            return tileThreats.TryGetValue(tile, out var data) ? data.threat : 0f;
        }

        /// <summary>
        /// Get the threat level at a world position for a specific team.
        /// </summary>
        public float GetThreatAtWorld(Vector3 worldPosition, Team team)
        {
            Vector3Int tile = WorldToTile(worldPosition);
            return GetThreat(tile, team);
        }

        /// <summary>
        /// Calculate all tiles crossed by a line segment.
        /// Uses stepping to catch fast-moving projectiles.
        /// </summary>
        private List<Vector3Int> GetTilesCrossed(Vector3 from, Vector3 to)
        {
            var tiles = new HashSet<Vector3Int>();
            float distance = Vector3.Distance(from, to);

            // At least 2 samples per unit distance to catch diagonal crossings
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance * 2));

            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0f;
                Vector3 point = Vector3.Lerp(from, to, t);
                tiles.Add(WorldToTile(point));
            }

            return new List<Vector3Int>(tiles);
        }

        /// <summary>
        /// Convert world position to tile position.
        /// </summary>
        private Vector3Int WorldToTile(Vector3 worldPos)
        {
            if (threatTilemap != null)
            {
                return threatTilemap.WorldToCell(worldPos);
            }
            // Fallback: assume 1 unit = 1 tile
            return new Vector3Int(
                Mathf.FloorToInt(worldPos.x),
                Mathf.FloorToInt(worldPos.y),
                0
            );
        }

        /// <summary>
        /// Decay all threat values over time.
        /// Higher threat decays faster (3x at 20+, normal at 5-).
        /// </summary>
        private void DecayAllThreats()
        {
            foreach (var teamKvp in teamThreats)
            {
                Team team = teamKvp.Key;
                var tileThreats = teamKvp.Value;

                tilesToRemove.Clear();

                var keys = new List<Vector3Int>(tileThreats.Keys);
                foreach (var tile in keys)
                {
                    var data = tileThreats[tile];

                    // Calculate decay multiplier based on threat level
                    float decayMultiplier = CalculateDecayMultiplier(data.threat);
                    float decay = baseDecayRate * decayMultiplier * Time.deltaTime;

                    data.threat -= decay;

                    if (data.threat <= 0.01f)
                    {
                        tilesToRemove.Add(tile);
                    }
                    else
                    {
                        tileThreats[tile] = data;

                        // Only update visual for displayed team
                        if (team == displayTeam)
                        {
                            UpdateTileVisual(tile, data.threat);
                        }
                    }
                }

                // Remove dead tiles
                foreach (var tile in tilesToRemove)
                {
                    tileThreats.Remove(tile);

                    // Only clear visual for displayed team
                    if (team == displayTeam)
                    {
                        ClearTileVisual(tile);
                    }
                }
            }
        }

        /// <summary>
        /// Calculate decay multiplier based on threat level.
        /// 3x at 20+, smoothly transitions to 1x at 5-.
        /// </summary>
        private float CalculateDecayMultiplier(float threat)
        {
            if (threat >= highThreatThreshold)
            {
                return highThreatDecayMultiplier;
            }
            else if (threat <= lowThreatThreshold)
            {
                return 1f;
            }
            else
            {
                // Smooth interpolation between low and high thresholds
                float t = (threat - lowThreatThreshold) / (highThreatThreshold - lowThreatThreshold);
                return Mathf.Lerp(1f, highThreatDecayMultiplier, t);
            }
        }

        /// <summary>
        /// Update the visual representation of a tile.
        /// Green (low) → Yellow → Orange → Red (high)
        /// </summary>
        private void UpdateTileVisual(Vector3Int tile, float threat)
        {
            if (threatTilemap == null || threatTile == null) return;

            float normalizedThreat = Mathf.Clamp01(threat / maxThreatForColor);

            // Color gradient: Green → Yellow → Orange → Red
            Color color = GetThreatColor(normalizedThreat);

            // Opacity scales with threat
            float opacity = Mathf.Lerp(baseOpacity, maxOpacity, normalizedThreat);
            color.a = opacity;

            // Set tile and color
            threatTilemap.SetTile(tile, threatTile);
            threatTilemap.SetTileFlags(tile, TileFlags.None);
            threatTilemap.SetColor(tile, color);
        }

        /// <summary>
        /// Clear a tile's visual when threat reaches zero.
        /// </summary>
        private void ClearTileVisual(Vector3Int tile)
        {
            if (threatTilemap == null) return;
            threatTilemap.SetTile(tile, null);
        }

        /// <summary>
        /// Get color for a normalized threat value (0-1).
        /// 0.0 = Green, 0.33 = Yellow, 0.66 = Orange, 1.0 = Red
        /// </summary>
        private Color GetThreatColor(float t)
        {
            if (t < 0.33f)
            {
                // Green to Yellow
                float localT = t / 0.33f;
                return Color.Lerp(Color.green, Color.yellow, localT);
            }
            else if (t < 0.66f)
            {
                // Yellow to Orange
                float localT = (t - 0.33f) / 0.33f;
                return Color.Lerp(Color.yellow, new Color(1f, 0.5f, 0f), localT);
            }
            else
            {
                // Orange to Red
                float localT = (t - 0.66f) / 0.34f;
                return Color.Lerp(new Color(1f, 0.5f, 0f), Color.red, localT);
            }
        }

        /// <summary>
        /// Clear all threat data and visuals.
        /// </summary>
        public void ClearAll()
        {
            foreach (var kvp in teamThreats)
            {
                kvp.Value.Clear();
            }
            if (threatTilemap != null)
            {
                threatTilemap.ClearAllTiles();
            }
        }

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 250, 120));
            GUILayout.Label($"Displaying: {displayTeam} threat view");

            if (teamThreats.TryGetValue(displayTeam, out var tileThreats))
            {
                GUILayout.Label($"Active Threat Tiles: {tileThreats.Count}");

                float maxThreatValue = 0f;
                foreach (var kvp in tileThreats)
                {
                    if (kvp.Value.threat > maxThreatValue)
                        maxThreatValue = kvp.Value.threat;
                }
                GUILayout.Label($"Max Threat: {maxThreatValue:F1}");
            }
            GUILayout.EndArea();
        }
#endif
    }
}
