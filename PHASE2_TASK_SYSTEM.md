# Phase 2: Task & Duty System - Architecture Planning

## Overview
Crew members are assigned to duty stations by department controllers. Stations provide interfaces to ship systems. Controllers manage allocation based on ship state, priorities, and events.

---

## Architecture Layers

```
┌─────────────────────────────────────────────────────────────┐
│                    DEPARTMENT CONTROLLERS                    │
│  SecurityController, BridgeController, EngineeringController │
│  - Knows all stations in their domain                        │
│  - Calculates desired manning based on ship state            │
│  - Assigns crew to stations                                  │
│  - Responds to events (alerts, damage, schedules)            │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      DUTY STATIONS                           │
│  ReactorStation, HelmStation, GuardPost, SensorStation       │
│  - Physical location (pathfinding target)                    │
│  - Occupancy tracking (who's here)                          │
│  - Job/Role requirements                                     │
│  - FUNCTIONS: What can crew DO here (not just "be" here)    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                      SHIP SYSTEMS                            │
│  Reactor, Shields, Sensors, Weapons, Propulsion              │
│  - Data/state (power level, shield strength, etc.)          │
│  - Stations provide INTERFACE to these systems               │
└─────────────────────────────────────────────────────────────┘
```

---

## Key Insight: Stations Are NOT Dumb

Stations are the **interface between crew and ship systems**.

Example - ReactorStation:
- Crew member walks to ReactorStation, occupies it
- While occupied, they can PERFORM ACTIONS:
  - MonitorReactor() - passive, detects problems
  - BoostPower() - increase output temporarily
  - ScramReactor() - emergency shutdown
  - RepairCore(int coreIndex) - fix damaged core
- These actions affect the actual Reactor system
- Skill checks use crew's Technical stat
- Better engineers = better outcomes

**Station responsibilities:**
1. Physical location / occupancy (base class)
2. Job/Role requirements (base class)
3. Available actions (subclass)
4. Interface to ship system (subclass)

**Controller responsibilities:**
1. WHO should be at which station
2. WHEN to reassign (shift change, emergency)
3. Responding to department-level events

---

## Department Controllers

| Controller | Job(s) Managed | Stations | Notes |
|------------|----------------|----------|-------|
| BridgeController | Pilot(Capital), Operator | Helm, Sensors, Comms, Weapons, Tactical | Bridge crew coordination |
| EngineeringController | Engineer | Reactor, PropulsionConsole, SystemsConsole | Power management, repairs |
| SecurityController | Armsman | GuardPosts[], Brig, Armory | Patrols, response teams |
| FlightController | Pilot, DeckCrew | LaunchControl, MechanicBay, OrdnanceBay | Hangar operations |
| MedicalController | Medical | MedBayStations[], TriageStation | Patient care, deployment |
| FabricationController | Machinist | Lathe, Fabricator, RepairBench | Work orders, resources |

---

## Station Examples

### ReactorStation
```
Required: Job.Engineer, Role.Power
Ship System: Reactor (on ShipController)

Actions:
- Monitor() - continuous, detects anomalies
- BoostOutput(float percent) - temporary overcharge, builds heat
- ReduceOutput(float percent) - reduce power, reduce heat
- ScramReactor() - emergency shutdown
- DiagnosticScan() - identify damaged cores
- RepairCore(int index) - fix a specific core

Events (station detects, notifies controller):
- PowerSpikeDetected
- CoreDamaged
- OverheatWarning
```

### HelmStation
```
Required: Job.Pilot, Role.Capital
Ship System: ShipController (movement)

Actions:
- SteerShip() - continuous, enables navigation
- EngageAutopilot(Vector3 destination)
- EvasiveManeuvers() - combat dodging
- EmergencyStop()

When Unmanned:
- Ship cannot maneuver (or only autopilot)
- Some actions unavailable
```

### GuardPost
```
Required: Job.Armsman, Role.None
Ship System: None (presence-based)

Actions:
- Stand Guard - continuous, detects intruders
- Challenge(Unit target) - demand identification
- Apprehend(Unit target) - attempt arrest
- Raise Alarm()

No direct system interface - but SecurityController
tracks which posts are manned for coverage calculation
```

---

## Open Questions

### 1. How do actions get triggered?
- **Automatic**: Station decides (MonitorReactor runs automatically when occupied)
- **AI Decision**: Crew AI decides what to do (see problem, boost power)
- **Controller Order**: Controller tells crew what to do
- **Player Command**: Player issues work order

### 2. Station vs System - where does logic live?
Example: BoostPower()
- Does ReactorStation.BoostPower() directly modify Reactor?
- Or does it call Reactor.Boost() and the system handles it?
- Leaning toward: Station calls System methods, System owns the logic

### 3. Skill checks and outcomes
- Who rolls the dice? Station? System? Crew?
- Crew.Character has the stats (Technical, etc.)
- Station probably does the check since it knows the action context
- System receives the result and applies it

### 4. What happens when station is unmanned?
- System degrades? (Reactor slowly loses efficiency)
- System stops working? (Can't steer without helmsman)
- System goes to autopilot? (Dumb but functional)
- Per-system decision?

### 5. Multiple crew at one station?
- Some stations might support 2+ crew (damage control team)
- Primary operator + assistant?
- Or separate stations that work together?

### 6. How do controllers know about ship state?
- Subscribe to events from a central ShipStateManager?
- Poll ShipController?
- Each controller watches what it cares about?

---

## Prototype Plan: SecurityController + GuardPost

**Why Security first:**
- Simplest station (no ship system interface)
- Clear priority logic (alert levels)
- Multiple stations managed by one controller
- Good test of allocation logic

**Implementation steps:**
1. [ ] GuardPost.cs - basic station with occupancy
2. [ ] SecurityController.cs - manages guard posts, assigns Armsmen
3. [ ] Alert level integration - priorities change with alert
4. [ ] Crew walks to assigned post
5. [ ] Shift handoff - replacement arrives, original leaves

---

## Prototype Plan: EngineeringController + ReactorStation

**Why Engineering second:**
- Has ship system interface (Reactor)
- Actions affect game state
- Skill checks matter
- Good test of station→system communication

**Implementation steps:**
1. [ ] ReactorStation.cs - station with actions
2. [ ] Reactor system (data on ShipController or separate?)
3. [ ] EngineeringController.cs - assigns engineers
4. [ ] Action execution - crew performs reactor actions
5. [ ] Skill checks - Technical stat affects outcomes

---

## Notes / Decisions Made

(Add decisions as we discuss)

