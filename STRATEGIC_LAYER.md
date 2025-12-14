# Strategic Layer - Galaxy, Factions, and Fleet Simulation

## Overview
The player's ship is one cog in a larger war. Factions control territory, allocate ships to operations, and the war progresses with or without the player. Most ships are simulated abstractly; only nearby ships get full detail.

---

## Simulation Levels

```
┌─────────────────────────────────────────────────────────────┐
│  LEVEL 0: FULL SIMULATION (Player's Ship)                   │
│  - Arena loaded, crew walking around                        │
│  - Every crew member simulated                              │
│  - Tasks, shifts, personalities                             │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  LEVEL 1: DETAILED (Nearby/Docked Ships)                    │
│  - Arena CAN be loaded (player boards, or close range)      │
│  - Crew roster exists with full Character data              │
│  - Ship state tracked (damage, fuel, ammo)                  │
│  - Not tick-by-tick simulated unless needed                 │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  LEVEL 2: ABSTRACT (Ships in same sector)                   │
│  - Ship exists as data (class, name, captain, status)       │
│  - Crew count known, not individual crew                    │
│  - Combat resolved abstractly                               │
│  - Can be promoted to Level 1 if player approaches          │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│  LEVEL 3: STRATEGIC (Ships elsewhere in galaxy)             │
│  - Just a record: "Destroyer Vanguard assigned to 3rd Fleet"│
│  - Battles resolved as fleet vs fleet outcomes              │
│  - Ship may be destroyed, damaged, or victorious            │
│  - Crew transfers happen abstractly                         │
└─────────────────────────────────────────────────────────────┘
```

**Promotion/Demotion:**
- Player warps into sector → Level 3 ships in that sector become Level 2
- Player approaches ship → Level 2 becomes Level 1
- Player docks with / boards ship → Level 1 gets Arena loaded (Level 0-adjacent)
- Player leaves sector → Ships demote back down

---

## Factions

```
Faction
├── Name: "Terran Federation"
├── Territory[]: Sectors they control
├── Fleets[]: Groups of ships
├── Stations[]: Shipyards, supply depots, HQs
├── Resources: Production capacity, manpower, etc.
├── AtWarWith[]: Enemy factions
├── Relations: Diplomacy with other factions
└── AI: Strategic decision making (allocate fleets, attack/defend)
```

**Example Factions:**
| Faction | Description | Player relation |
|---------|-------------|-----------------|
| Terran Federation | Player's faction, democratic, large navy | Friendly (we serve them) |
| Hegemony | Authoritarian empire, enemy | At war |
| Free Colonies | Independent systems, neutral | Varies |
| Pirates | Raiders, hostile to all | Enemy |

---

## Fleets & Task Forces

Ships are organized into fleets, which are assigned to operations.

```
Fleet
├── Name: "3rd Fleet"
├── Faction: Terran Federation
├── Admiral: Character (commanding officer)
├── Ships[]: List of ships assigned
├── HomeBase: Station where it resupplies
├── CurrentOperation: What it's doing
└── Location: Where it is (sector, or in-transit)

TaskForce (temporary grouping for a mission)
├── Name: "Task Force Hammer"
├── ParentFleet: 3rd Fleet
├── Ships[]: Subset of fleet ships
├── Mission: "Assault enemy station in Korax System"
├── Duration: Estimated time
└── Status: Forming, Deploying, Engaged, Returning
```

**Player's ship might be:**
- Part of 3rd Fleet
- Assigned to Task Force Hammer
- Or detached for independent patrol duty

---

## Ships (Abstract Representation)

```
ShipRecord (for Level 2-3 ships)
├── Id: Unique identifier
├── Name: "TFS Chimera"
├── Class: Destroyer
├── Faction: Terran Federation
├── Captain: Character (just the CO)
├── Fleet: 3rd Fleet (or null if independent)
├── Status: Operational, Damaged, Repairing, Destroyed
├── Location: Sector + position (or in-transit)
├── CrewCount: 75 (not individual crew)
├── Loadout: Fighters, weapons (abstract)
└── CanPromote(): Can this become Level 1?

ShipInstance (for Level 1 ships - full data)
├── ShipRecord (base data)
├── Crew[]: Full CrewMember list
├── Fuel, Ammo, Supplies: Exact values
├── Damage: Per-system damage
├── Fighters[]: Individual craft status
└── ArenaPrefab: What to load if player boards
```

---

## Crew Transfers & Rosters

**Naval Personnel System:**
```
NavalRoster (faction-wide)
├── AllPersonnel[]: Every Character in the navy
├── Assignments: Who is on which ship
├── TransferQueue[]: Pending transfers
├── Casualties[]: KIA, MIA, WIA tracking
└── Recruitment: New personnel joining

Transfer
├── Character: Who
├── FromShip: Current assignment
├── ToShip: New assignment
├── Reason: Promotion, rotation, request, needed
├── EffectiveDate: When (happens at station dock)
└── Status: Pending, InTransit, Completed
```

**When do transfers happen?**
- Ships dock at same station
- Character physically moves (if Level 1 ships)
- Or just roster update (if Level 2-3)

**Drama potential:**
- Your best engineer gets transferred
- A troublemaker arrives from another ship
- Friend from academy is now on ship docked next to you
- Receive crew from destroyed ship (survivors)

---

## Operations & Orders

Command (faction HQ) assigns operations to fleets/ships.

```
Operation
├── Name: "Operation Iron Gate"
├── Type: Assault, Defense, Patrol, Escort, Recon
├── Objective: "Capture enemy station in Korax-4"
├── AssignedForces[]: Fleets/TaskForces/Ships
├── Timeline: Start, estimated duration
├── Priority: Critical, High, Normal
├── Status: Planning, Active, Complete, Failed
└── Outcomes: What happened (for history)
```

**For player's ship:**
```
CurrentOrders
├── Source: "3rd Fleet Command"
├── Type: Patrol
├── Area: Sectors 7, 8, 9
├── Duration: 2 weeks
├── ROE: Rules of engagement
├── Resupply: "Return to Station Alpha when needed"
└── SpecialInstructions: "Report any Hegemony activity"
```

---

## Map Structure

```
Galaxy
├── Sectors[]: All regions of space
└── JumpNetwork: How sectors connect

Sector
├── Id, Name
├── ControlledBy: Faction (or Contested)
├── ThreatLevel: None, Low, Medium, High, Extreme
├── Locations[]:
│   ├── Stations (friendly, enemy, neutral)
│   ├── Planets (colonies, resources)
│   ├── Asteroid Fields (mining, hiding)
│   ├── Jump Points (connections to other sectors)
│   └── Points of Interest (derelicts, anomalies)
├── ShipsPresent[]: Who's here right now
└── Events[]: Active events in this sector
```

---

## Simulation Tick (Strategic Layer)

Every game-day (or hour?), the strategic layer updates:

```
1. FACTION AI
   - Evaluate war status
   - Allocate ships to operations
   - Issue new orders

2. FLEET MOVEMENT
   - Ships in transit advance toward destination
   - Ships arrive at new locations

3. BATTLES (Abstract)
   - If opposing forces in same location
   - Resolve combat (fleet strength, commanders, luck)
   - Results: Ships damaged/destroyed, victor controls area

4. OPERATIONS PROGRESS
   - Active operations advance
   - Objectives achieved or failed
   - New operations generated

5. LOGISTICS
   - Ships consume supplies over time
   - Damaged ships repair at stations
   - Reinforcements produced at shipyards

6. PERSONNEL
   - Transfers execute when conditions met
   - Promotions, casualties processed
   - New recruits assigned
```

---

## What Player Experiences

The player doesn't see spreadsheets. They experience the war through:

1. **Orders** - "Command has assigned us to patrol Sector 7"
2. **News** - "3rd Fleet engaged enemy at Korax - 2 ships lost"
3. **Crew Talk** - "Did you hear the Vanguard was destroyed? My friend was on that ship."
4. **Transfers** - New crew arrive, old crew leave
5. **Encounters** - Random events based on war state
6. **Stakes** - If your fleet loses, you might be retreating. If it wins, you advance.

---

## Open Questions

1. **How much faction AI?** Simple or complex strategic decisions?

2. **Battle resolution** - Pure dice? Ship stats? Commander skill?

3. **Can player influence strategy?** Or just follow orders?
   - Maybe high-rank players can suggest operations?
   - Or player actions affect local outcomes that ripple up?

4. **Permadeath for player ship?** If destroyed in battle, game over? Or reassigned to new ship?

5. **Time scale** - How fast does game time pass?
   - In arena: 1 real sec = 1 game sec?
   - In transit/patrol: Accelerated?
   - Player controls time scale?

6. **Save/Load** - How to save the entire war state?

---

## Implementation Priority

**Phase A: Map & Navigation** ✅ COMPLETE
- [x] Sector data structure - `Sector.cs` (100k x 100k units, 20x20 chunks of 5k each)
- [x] Location types - `PointOfInterest.cs` (Station, Planet, AsteroidField, JumpPoint, DebrisField)
- [x] Ship can warp between locations - `SectorManager.JumpToSector()`
- [x] Gravity well logic - `Sector.IsInGravityWell()`, `GetNearestJumpPoint()`
- [x] Faction system - `Faction.cs` (TerranFederation, Hegemony, FreeColonies, Pirates)
- [x] Abstract ships - `ShipRecord.cs` (SimulationLevel enum: Strategic, Abstract, Detailed, Full)
- [x] Test infrastructure - `TestSectorFactory.cs`, `SectorTestRunner.cs`

**Phase B: Basic Events**
- [ ] Event scheduler
- [ ] Patrol mission (move around, random encounters)
- [ ] Resupply event (dock at station)
- [ ] Simple combat encounter

**Phase C: Faction & Fleets**
- [x] Faction data structure - `Faction.cs`
- [ ] Fleet assignment
- [x] Abstract ships (Level 2-3) - `ShipRecord.cs`
- [ ] Orders from command

**Phase D: War Simulation**
- [ ] Faction AI (simple)
- [ ] Abstract battle resolution
- [ ] Territory control changes
- [ ] Operations system

**Phase E: Personnel**
- [ ] Naval roster
- [ ] Transfers between ships
- [ ] Promotions, casualties
- [ ] Crew from destroyed ships

---

## Files Created

```
Scripts/Strategic/
├── Sector.cs           - Sector data (100k units, 20x20 chunks)
├── PointOfInterest.cs  - POI base + Station, Planet, AsteroidField, JumpPoint, DebrisField
├── Faction.cs          - Faction data + static registry (4 factions)
├── ShipRecord.cs       - Abstract ship representation (Level 2-3)
├── SectorManager.cs    - Loads sectors, spawns POIs/ships, manages sim levels
├── TestSectorFactory.cs - Creates test sectors (Home, Frontier)
└── SectorTestRunner.cs - MonoBehaviour for quick testing (F1=info, F2=jump)
```

---

## Notes / Decisions

**Sector Specifications:**
- Sector size: 100,000 x 100,000 units (-50,000 to 50,000)
- Chunk size: 5,000 x 5,000 units
- Grid: 20x20 = 400 chunks per sector
- POIs centered in their chunk
- Home sector is hand-crafted, others can be procedural

**POI Types:**
| Type | Size | Notes |
|------|------|-------|
| Planet | radius 5000-8000, gravity well 15000-20000 | Takes entire sector |
| Station | ~50-100 units | Military, Civilian, Mining, Research, Shipyard, Outpost |
| AsteroidField | radius 2000-4000 | Provides concealment, resources |
| JumpPoint | Small marker | Must be outside gravity wells |
| DebrisField | radius 500 | Battle remnants, salvage |

**Jump Points:**
- Must be outside gravity wells (planet.gravityWellRadius)
- Usually at sector edges
- Connect specific chunks between sectors

**Simulation Levels:**
- Level 3 (Strategic): Just a record
- Level 2 (Abstract): In sector, visible, abstract combat
- Level 1 (Detailed): Full crew roster, can load arena
- Level 0 (Full): Player's ship, always loaded

**Loading Strategy:**
- Load ALL POIs and ships when entering sector (Space View)
- Don't load Arenas until player boards/docks
- Placeholders used if prefabs not found

