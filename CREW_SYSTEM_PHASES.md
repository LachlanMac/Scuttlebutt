# Crew & Task System - Implementation Phases

## Overview
A simulation-style job system where crew members perform actual tasks at duty stations throughout the ship. Tasks have priorities and can be continuous, recurring, conditional, or one-shot.

## Core Concepts
- **Shift**: Main (12hr) and Off (12hr) - crew hand off duty stations
- **DutyStation**: Physical locations where work happens (Helm, Reactor Console, Lathe, etc.)
- **Job**: A crew position defining what tasks/stations a person is responsible for
- **Role**: A specialization/qualification within a job (Fighter pilot vs Shuttle pilot)
- **Task**: A unit of work with priority, assigned to crew members

---

## Job + Role System

Characters have a **Job** (their position) and one or more **Roles** (their qualifications).
DutyStations require a specific Job and optionally a specific Role.

### Jobs and Their Roles

| Job | Branch | Roles | Notes |
|-----|--------|-------|-------|
| **Captain** | Navy | - | Commanding officer |
| **ExecutiveOfficer** | Navy | - | Second in command |
| **Pilot** | Navy | Fighter, Bomber, Shuttle, Capital | Capital = helm qualified |
| **Operator** | Navy | Sensors, Comms, Weapons, Tactical | Bridge/CIC console operators |
| **Engineer** | Navy | Power, Propulsion, Systems | Ship's engineering |
| **Machinist** | Navy | Fabrication, Repair | Machine shop work |
| **Medical** | Navy | Combat, Trauma, General | Combat medics deploy with Marines |
| **DeckCrew** | Navy | Mechanic, Ordnance, Handler | Hangar operations |
| **Quartermaster** | Navy | - | Logistics/supply |
| **Armsman** | Navy | - | Ship security, brig, armory |
| **Marine** | Marine | Rifleman, Marksman, Demolitions, Shocktrooper | Combat troops |

### Role Definitions

```
Pilot Roles:
  - Fighter: Single-seat combat craft
  - Bomber: Attack craft, torpedoes
  - Shuttle: Transport, utility craft
  - Capital: Starship helm qualified

Marine Roles:
  - Rifleman: Standard infantry
  - Marksman: Long-range precision
  - Demolitions: Explosives, breaching
  - Shocktrooper: Aggressive close-quarters

Machinist Roles:
  - Fabrication: Creating parts from raw materials
  - Repair: Fixing equipment/components

Engineer Roles:
  - Power: Reactor, electrical, power distribution
  - Propulsion: Engines, thrusters, FTL
  - Systems: Computers, life support, shields

Operator Roles:
  - Sensors: Detection, scanning, tracking
  - Comms: Communications, signals
  - Weapons: Weapons console operation
  - Tactical: CIC coordination, threat assessment

Medical Roles:
  - Combat: Field medicine, deploys with Marines
  - Trauma: Emergency surgery, critical care
  - General: Routine care, checkups

DeckCrew Roles:
  - Mechanic: Repairs fighters/shuttles
  - Ordnance: Loads weapons/fuel
  - Handler: Moves craft, directs launches
```

---

## Task Sources
| Type | Description | Example |
|------|-------------|---------|
| Continuous | Must always be happening | Man the Helm |
| Recurring | Scheduled intervals | Daily Maintenance Log |
| Conditional | When conditions are met | Divert Power (when load >90%) |
| OneShot | Event or player triggered | Repair Battle Damage |

## Task Priorities
| Priority | Behavior |
|----------|----------|
| Critical | Drop everything, do this now |
| High | Interrupt normal/low priority tasks |
| Normal | Standard work |
| Low | When nothing else to do |
| Idle | Default station behavior (stay at post) |

---

## Phase 1: Foundation
**Status: COMPLETE**

- [x] Job enum (skill categories)
- [x] Role enum (qualifications/certifications)
- [x] Shift enum (Main, Off)
- [x] Position system (JSON-defined billets: Positions.json)
- [x] PositionRegistry (load/lookup positions)
- [x] RoleAdjacency (cross-training logic based on experience)
- [x] DutyStation.cs component (location, job+role requirements, occupant tracking)
- [x] CrewMember.cs (wraps Character + job/shift/roles)
- [x] CharacterFactory updated (GenerateForPosition, multi-role generation)
- [x] Bed.cs integration (generates CrewMember based on position assignment)
- [x] CrewManager.cs (finds beds, generates crew, provides lookups)

**Design Decisions Made:**
- Three-layer system: Job (skill) → Role (qualification) → Position (billet)
- Positions defined in JSON for designer flexibility
- Role adjacency system for realistic cross-training
- Experience-based role acquisition (5+ years = chance for extra roles)

---

## Phase 2: Task System
**Status: PENDING**

- [ ] Task.cs (priority, trigger type, duration, status, target station)
- [ ] TaskManager.cs (global queue, assignment logic)
- [ ] Basic continuous task execution (crew goes to station, "works")
- [ ] Task completion/failure handling

---

## Phase 3: Scheduling & Handoffs
**Status: PENDING**

- [ ] Recurring task scheduler (hourly, daily, etc.)
- [ ] Shift handoff system (wait for replacement before leaving)
- [ ] Priority interrupt system (high priority pulls from low)
- [ ] Shift change logic (who goes where when shift ends)

---

## Phase 4: Reactive & Advanced
**Status: PENDING**

- [ ] Conditional task triggers (monitor game state, spawn tasks)
- [ ] Work order system (player can queue orders)
- [ ] Task dependencies (fabricate → install)
- [ ] Multi-step tasks (go to A, get item, bring to B)
- [ ] Failure/fallback handling (no qualified crew available)

---

## Future Considerations
- Skill progression (crew get better at their job over time)
- Cross-training (crew can learn secondary jobs)
- Morale effects on task performance
- Emergency protocols (battle stations, abandon ship)
