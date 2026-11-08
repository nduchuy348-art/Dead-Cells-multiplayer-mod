---
name: coop-architect
description: Architectural principles and root-cause engineering rules for Dead Cells co-op multiplayer modding.
---

# Dead Cells Co-op Architectural Invariants & Root-Cause Engineering

This skill defines the architectural foundation for the Dead Cells Multiplayer Mod, enforcing systemic correctness over symptom-level patching.

## Core Invariants

### 1. Actor Ownership & Authority
- Exactly one machine simulates each actor (Host is authoritative for all mobs, elites, bosses, and world objects).
- Clients render replicas (puppets) driven purely by authoritative state snapshots.
- Spatial culling on the host or client must NEVER alter entity lifecycle state (e.g., setting life to 0 or prematurely running `onDie`).
- Un-culling an entity must cleanly restore visual interpolation without triggering death or AI spasms.

### 2. Targeting & AI Aggro Parity
- All human players (Local `Hero` and remote `GhostKing`) must be equally valid, reachable, and scored targets for mob AI.
- An entity wrapper's internal engine flags (such as `_targetable`) must not disqualify remote players if they are alive and active on the level.

### 3. Puppet Locomotion & Animation Soundness
- Position replication uses a monotonic Hermite interpolation buffer with velocity-bounded extrapolation.
- Animation switches must clean up previous sprite accessories and attack trails to prevent phantom weapon layering.
- Routine locomotion (walking, jumping, rolling, landing) must map to deterministic game animations (`land`, `crouch`, `jump`, `fall`) without hard-coded duration stuns.
- Audio events must be spatialized with quadratic falloff and vertical attenuation, cutting off below minimum audibility thresholds.

### 4. Deterministic World & Transition Synchronization
- World interactions (portals, shrines, breakable grounds, teleporters) must be idempotent: executing twice produces the same state.
- Door and exit transitions must use fuzzy proximity matching (allowing cell tolerances up to 6 tiles) to prevent player split desyncs.
- Level RNG seeds must be authoritatively committed by the Host and acknowledged by Clients before entity simulation begins.

### 5. Persistence & Save Isolation
- Client (Player 2) save data must be isolated in dedicated multiplayer storage (`save/MSave/`) to prevent clobbering vanilla profiles.
- Serialization and deserialization must always be executed within an explicit `LocalSerializerSaveScope`.
- Graceful shutdown on quit must preserve the multiplayer store override until the game's write completes.
