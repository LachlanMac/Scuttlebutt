using UnityEngine;

namespace Starbelter.Core
{
    /// <summary>
    /// Reference guide for setting up the tactical combat system.
    /// Create an instance via Assets > Create > Starbelter > Setup Instructions
    /// </summary>
    [CreateAssetMenu(fileName = "SetupInstructions", menuName = "Starbelter/Setup Instructions")]
    public class SetupInstructions : ScriptableObject
    {
        [TextArea(3, 5)]
        public string step1_CreateTags = @"STEP 1: CREATE TAGS
Go to Edit > Project Settings > Tags and Layers
Add these tags:
  - HalfCover
  - FullCover";

        [TextArea(3, 10)]
        public string step2_SetupAStarGraph = @"STEP 2: SETUP A* PATHFINDING
1. Create empty GameObject named 'A*'
2. Add 'AstarPath' component
3. Add a Grid Graph:
   - Click 'Add New Graph' > Grid Graph
   - Width/Depth: Match your map size (e.g., 50x50)
   - Node Size: 1 (matches tile size)
   - Collision Testing > Use 2D Physics: ENABLED
   - Collision Testing > Diameter: 0.8 (slightly smaller than tile)
   - Collision Testing > Mask: Set to your obstacle layers
   - Height Testing: DISABLED (2D game)
   - Connections: Eight (8-directional movement)
4. Position the graph center to match your tilemap origin";

        [TextArea(3, 8)]
        public string step3_SetupTilemaps = @"STEP 3: SETUP TILEMAPS
1. Create Grid object (right-click > 2D Object > Tilemap > Rectangular)
2. Rename first tilemap to 'Tilemap_Ground' (visual only)
3. Create second tilemap child under Grid, name it 'Tilemap_Cover'
   - Set Tilemap Renderer sorting order lower (or disable renderer)
   - This tilemap is data-only for cover positions
4. Create a simple tile asset for cover (Assets > Create > 2D > Tiles > Rule Tile)
   - Or use any basic tile sprite";

        [TextArea(3, 8)]
        public string step4_SetupManagers = @"STEP 4: SETUP MANAGER OBJECTS
Create empty GameObject named 'Managers' with these children:
1. 'GameManager' - Add GameManager component
2. 'CoverBaker' - Add CoverBaker component
   - Assign Tilemap_Cover reference
   - Assign your cover tile asset
3. 'CoverQuery' - Add CoverQuery component
   - Set Cover Raycast Mask to obstacle layers
4. 'TileOccupancy' - Add TileOccupancy component
   - Assign Tilemap_Cover as reference tilemap";

        [TextArea(3, 10)]
        public string step5_CreateCoverObjects = @"STEP 5: CREATE COVER OBJECTS
For each cover prefab (sandbags, walls, turrets, etc.):
1. Add BoxCollider2D sized to the object bounds
2. Set the tag:
   - 'HalfCover' for partial cover (sandbags, crates)
   - 'FullCover' for complete cover (walls, thick barriers)
3. Put on a layer included in A*'s collision mask
4. (Optional) Add DestructibleCover component if it can be destroyed

Example sandbag setup:
  - 1x1 BoxCollider2D
  - Tag: HalfCover
  - Layer: Obstacles";

        [TextArea(3, 8)]
        public string step6_SetupUnits = @"STEP 6: SETUP UNITS
For each unit prefab:
1. Add Seeker component (A* pathfinding)
2. Add UnitMovement component
3. Add Rigidbody2D (set to Kinematic)
4. Add CircleCollider2D for unit bounds
5. (Optional) Add RVOController for local avoidance:
   - Simulator > Create New RVO Simulator if none exists
   - Set RVO Layer appropriately";

        [TextArea(3, 5)]
        public string step7_Testing = @"STEP 7: TESTING
1. Enter Play Mode
2. GameManager will auto-scan A* and bake cover
3. Check Console for initialization messages
4. Use Gizmos to visualize:
   - A* graph nodes (enable in A* inspector)
   - Cover positions (CoverQuery debug)
   - Occupied tiles (TileOccupancy debug)";

        [TextArea(3, 5)]
        public string rvoSetupNotes = @"RVO (LOCAL AVOIDANCE) NOTES
For real-time unit avoidance:
1. Add 'RVOSimulator' component to a manager object
2. Add 'RVOController' to each unit
3. Units will automatically avoid each other while moving
4. TileOccupancy handles stationary positioning";
    }
}
