using UnityEngine;
using System.IO;

namespace Starbelter.Strategic
{
    /// <summary>
    /// Generates a territory map image showing faction control across the galaxy.
    /// Each pixel represents one chunk. Color = controlling faction.
    /// </summary>
    public static class TerritoryMapGenerator
    {
        /// <summary>
        /// Generate a territory map for the entire galaxy.
        /// Image size = (GALAXY_SIZE * CHUNKS_PER_AXIS) x (GALAXY_SIZE * CHUNKS_PER_AXIS)
        /// With 10x10 galaxy and 10x10 chunks = 100x100 pixels.
        /// </summary>
        public static void GenerateTerritoryMap(GalaxyData galaxy, string outputPath)
        {
            int chunksPerAxis = Sector.CHUNKS_PER_AXIS;
            int galaxySize = GalaxyData.GALAXY_SIZE;
            int imageSize = galaxySize * chunksPerAxis; // 10 * 10 = 100

            var texture = new Texture2D(imageSize, imageSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point; // No smoothing

            // Fill with black (unclaimed)
            var blackPixels = new Color[imageSize * imageSize];
            for (int i = 0; i < blackPixels.Length; i++)
                blackPixels[i] = Color.black;
            texture.SetPixels(blackPixels);

            // Iterate through all sectors and chunks
            for (int sectorX = 0; sectorX < galaxySize; sectorX++)
            {
                for (int sectorY = 0; sectorY < galaxySize; sectorY++)
                {
                    var sector = galaxy.GetSector(sectorX, sectorY);
                    if (sector == null) continue;

                    // Recalculate claims for this sector
                    sector.RecalculateAllClaims();

                    for (int chunkX = 0; chunkX < chunksPerAxis; chunkX++)
                    {
                        for (int chunkY = 0; chunkY < chunksPerAxis; chunkY++)
                        {
                            var claim = sector.GetChunkClaim(new Vector2Int(chunkX, chunkY));
                            if (claim == null || !claim.HasAnyClaims) continue;

                            FactionId dominant = claim.GetDominantFaction();
                            if (dominant == FactionId.None)
                            {
                                // Contested - use purple/magenta
                                int pixelX = sectorX * chunksPerAxis + chunkX;
                                int pixelY = sectorY * chunksPerAxis + chunkY;
                                texture.SetPixel(pixelX, pixelY, new Color(0.5f, 0, 0.5f));
                                continue;
                            }

                            var faction = Factions.Get(dominant);
                            if (faction != null)
                            {
                                int pixelX = sectorX * chunksPerAxis + chunkX;
                                int pixelY = sectorY * chunksPerAxis + chunkY;
                                texture.SetPixel(pixelX, pixelY, faction.factionColor);
                            }
                        }
                    }
                }
            }

            texture.Apply();

            // Save to file
            byte[] pngData = texture.EncodeToPNG();
            string filePath = Path.Combine(outputPath, "territory_map.png");
            File.WriteAllBytes(filePath, pngData);

            // Cleanup
            Object.DestroyImmediate(texture);

            Debug.Log($"[TerritoryMapGenerator] Generated {imageSize}x{imageSize} territory map at {filePath}");
        }

        /// <summary>
        /// Generate a higher resolution map with multiple pixels per chunk.
        /// </summary>
        public static void GenerateTerritoryMapHiRes(GalaxyData galaxy, string outputPath, int pixelsPerChunk = 10)
        {
            int chunksPerAxis = Sector.CHUNKS_PER_AXIS;
            int galaxySize = GalaxyData.GALAXY_SIZE;
            int imageSize = galaxySize * chunksPerAxis * pixelsPerChunk; // 10 * 10 * 10 = 1000

            var texture = new Texture2D(imageSize, imageSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;

            // Fill with black
            var blackPixels = new Color[imageSize * imageSize];
            for (int i = 0; i < blackPixels.Length; i++)
                blackPixels[i] = Color.black;
            texture.SetPixels(blackPixels);

            // Iterate through all sectors and chunks
            for (int sectorX = 0; sectorX < galaxySize; sectorX++)
            {
                for (int sectorY = 0; sectorY < galaxySize; sectorY++)
                {
                    var sector = galaxy.GetSector(sectorX, sectorY);
                    if (sector == null) continue;

                    sector.RecalculateAllClaims();

                    for (int chunkX = 0; chunkX < chunksPerAxis; chunkX++)
                    {
                        for (int chunkY = 0; chunkY < chunksPerAxis; chunkY++)
                        {
                            var claim = sector.GetChunkClaim(new Vector2Int(chunkX, chunkY));
                            Color chunkColor = Color.black;

                            if (claim != null && claim.HasAnyClaims)
                            {
                                FactionId dominant = claim.GetDominantFaction();
                                if (dominant == FactionId.None)
                                {
                                    chunkColor = new Color(0.5f, 0, 0.5f); // Contested
                                }
                                else
                                {
                                    var faction = Factions.Get(dominant);
                                    if (faction != null)
                                        chunkColor = faction.factionColor;
                                }
                            }

                            // Fill the chunk area with this color
                            int baseX = (sectorX * chunksPerAxis + chunkX) * pixelsPerChunk;
                            int baseY = (sectorY * chunksPerAxis + chunkY) * pixelsPerChunk;

                            for (int px = 0; px < pixelsPerChunk; px++)
                            {
                                for (int py = 0; py < pixelsPerChunk; py++)
                                {
                                    texture.SetPixel(baseX + px, baseY + py, chunkColor);
                                }
                            }
                        }
                    }
                }
            }

            // Draw sector grid lines (darker version of background)
            Color gridColor = new Color(0.15f, 0.15f, 0.15f);
            int sectorPixelSize = chunksPerAxis * pixelsPerChunk;

            for (int i = 0; i <= galaxySize; i++)
            {
                int linePos = i * sectorPixelSize;
                if (linePos >= imageSize) linePos = imageSize - 1;

                for (int j = 0; j < imageSize; j++)
                {
                    // Vertical lines
                    if (linePos < imageSize)
                        texture.SetPixel(linePos, j, gridColor);
                    // Horizontal lines
                    if (linePos < imageSize)
                        texture.SetPixel(j, linePos, gridColor);
                }
            }

            texture.Apply();

            byte[] pngData = texture.EncodeToPNG();
            string filePath = Path.Combine(outputPath, "territory_map_hires.png");
            File.WriteAllBytes(filePath, pngData);

            Object.DestroyImmediate(texture);

            Debug.Log($"[TerritoryMapGenerator] Generated {imageSize}x{imageSize} hi-res territory map at {filePath}");
        }
    }
}
