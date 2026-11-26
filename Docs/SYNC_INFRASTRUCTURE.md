# Sync Infrastructure — Sync Charges, Reconstruction, Stabilizer supply and safeguards

**Status:** roadmap increments 1 and 2 implemented, opt-in, verified by automated EditMode tests. **Not yet
played by the Director** (Gate B is pending), so controls, presentation and feel are unverified.
**Design reference:** `Docs/DESIGN_DECISIONS.md` §4.1 (D2-A Reconstruction), §4.2 (U2 supply), §4.3 (Option A
allocation) and §7 item 3 (safeguards S1–S4, approved by the Director on 2026-11-25). Increment 2 (U2 supply and
safeguards) is described in §8 below.

## 1. What it does

| Area | Behavior |
|---|---|
| Opt-in | `GridManager` → **Sync Infrastructure** → `enabled`. The default is **off**. `Sandbox.unity` does not contain these settings, so the Sandbox keeps instant, free building. |
| Generation | Each connected, completed Sync Core (`Core`) generates `coreGenerationPerTick` whole Sync Charges per **simulation tick**. |
| Distribution | Automatic and deterministic (Option A, no player controls). Charges go only to eligible connected consumers. |
| Blueprints | With the system on, placing a Stabilizer or Nullifier is free and immediate, but creates a **blueprint**. Occupancy rules are unchanged; contaminated cells are allowed. |
| Reconstruction | A blueprint receives charges only while it is **connected** to the Sync Network **and** its own cell is **clean**. It completes after receiving its full cost. |
| Unfinished blueprints | Connect to the network, but give **no stabilization, no relaying and no Nullifier effect**. |
| Diagnostics | HUD lines (only while enabled), `GridManager.SyncDiagnostics`, and the hovered structure's status. |

The Sync Core itself is never a blueprint (see A1).

## 2. Tick order

`GridManager.RunSimulationTick()` (called from `Update()` for each tick, and for single steps while paused):

1. `UpdateSyncInfrastructure()`: generation → demand → allocation → construction. If a blueprint completed, the network is
   refreshed, so the new structure relays and stabilizes **in the same tick**. Does nothing while disabled.
2. `Simulate()`, `UpdateNullifiers()`, `CalculateIntegrity()`, `CalculateActivity()`, `UpdateContainmentStatus()`,
   `RenderGrid()`: unchanged, in the previous order.

## 3. Allocation rule (`SyncChargeAllocator`)

- Charges are whole units, handed out one at a time.
- **Across categories:** round-robin in the fixed order Construction → Stabilizer supply → Nullifier operation, so no
  category with demand starves. Only Construction has consumers in increment 1; the other two categories are reserved
  for U2 (increment 2) and N1-A (increment 3).
- **Within a category:** placement order. The earliest-placed eligible blueprint is served first, up to its per-tick
  intake; then the next one. Construction spam delays later blueprints instead of spreading charges thinly.
- Blueprints that are disconnected or on contaminated ground make **no request**, so they never hold up eligible ones.
- The result depends only on the inputs (no randomness, frame time or list-order effects).

## 4. Provisional engineering assumptions (Director review needed)

All numbers are **test fixtures, not balance decisions**.

| # | Assumption | Why |
|---|---|---|
| A1 | The Sync Core is placed **operational immediately** and is never a blueprint. | It is the network root and the only generator; a Core blueprint could never be built. |
| A2 | **No charge storage** anywhere in increment 1. Charges not allocated in a tick are reported as *unused* and lost. | Simplest abstract throughput. Storage belongs to U2/S3 (increment 2). |
| A3 | One shared pool per tick for all connected consumers. With one Core per map (current rule) this is the whole network. | Multiple separate networks are not possible today. |
| A4 | **Clean ground** = the blueprint's own cell is at or below the game's contamination threshold (Desync ≤ 5 in the Sandbox), the same definition used everywhere else. | Uses the game's own condition values (metric standard). Footprint-wide checks were not specified. |
| A5 | A per-blueprint **intake cap** per tick (`maxConstructionIntakePerTick`) makes construction take time even with surplus supply. | D2-A: "takes time limited by supply". |
| A6 | Costs: Stabilizer 40, Nullifier 60; generation 4 per tick; intake 2 per tick. At the Sandbox tick rate (0.1 s), a Stabilizer takes about 2 s alone. | Placeholder values chosen so tests and a playtest show the behavior. |
| A7 | Removing a blueprint (Shift + Middle Click) refunds nothing. | No storage to refund into (A2). |
| A8 | A blueprint's status (waiting, reconstructing) is re-evaluated every tick; contamination arriving later pauses construction, it does not undo progress. | Not specified; the least surprising behavior. |
| A9 | Turning the system on only affects structures placed afterwards; turning it off leaves unfinished blueprints unfinished. Enable it before placing anything. | Avoids converting existing structures mid-game. |
| A10 | Marker colors: waiting blueprints dim blue, blueprints being built bright blue. | Minimal presentation only; increment 6 owns presentation. |

## 5. Not in increment 1

U2 Stabilizer consumption (added in increment 2, §8), suppression and recovery variants, Nullifier reconfiguration,
Emitter Restoration, The Basin, player allocation controls (issue #2), UI art integration, travelling packet entities,
routing or logistics interfaces.

## 6. Automated verification (EditMode, headless)

Run with the studio's locking runner (`-Mode Tests -TestPlatform EditMode`). Tests live in `Assets/_CoreFiles/Tests/EditMode`.

| Test class | Covers |
|---|---|
| `SandboxSmokeRegressionTests` | The 63-check Sandbox smoke test, its determinism repeat, and exact match with the 2026-11-23 recorded baseline (final total Desync, contaminated-cell history, ticks to nullify); Sandbox has Sync Infrastructure disabled. |
| `SyncChargeAllocatorTests` | Supply limits, demand limits, placement order, category round-robin, determinism. |
| `ReconstructionTests` | Generation, completion after exactly the cost, Nullifier cost, disconnected and contaminated blueprints, no relay until complete, blocked blueprints not blocking others, spam vs. throughput, scarce supply order, determinism. |
| `SyncInfrastructureSceneTests` | The same rules inside the real `GridManager` and Sandbox scene (enabled in memory only, never saved): no stabilization until complete, chain building, occupancy, contaminated placement, unfinished Nullifier, determinism, and disabled = instant building. |
| `ProlongedCollapseSceneTests` (increment 2) | §8.5 prolonged collapse in the real simulation: naive player (diagnostic), competent player at the shipped defaults (reconnects, gap and S2 reclaimed) and at generation 8 (full recovery), determinism. |
| `StabilizerSupplyTests` (increment 2) | U2 supply, store filling, S3 running on store then starving, partial supply, over-expansion, S2 foothold priority and generation floor, S1 safe zone, U2 off, the severed-connection scenario, determinism. |
| `StabilizerSupplySceneTests` (increment 2) | In the real simulation: the **required severed-connection acceptance scenario** (compared with an unsevered control run), recovery from a collapse around the Sync Core, effectiveness scaling stabilization, S4 defeat on/off/after victory, Sandbox defaults off. |

**Not verified automatically:** interactive input, HUD layout and readability, marker colors, Play-mode timing (frame-rate
dependent tick rate), and how it feels to play. These need the Director's playtest.

## 7. Trying it in the Editor

1. Open `Assets/_CoreFiles/Scenes/Sandbox.unity` and press **Play**.
2. **While playing and before placing anything**, select the GridManager object. In the Inspector, expand
   **Sync Infrastructure** and tick **Enabled**. (Changes made during Play mode are undone when Play stops, so the Sandbox
   scene is never changed. Do not tick it outside Play mode.)
3. Place a Core (key `4`, left click), then Stabilizers (`2`) within 8 cells, and watch the HUD's *Sync Charges* and
   *Blueprints* lines and the blue blueprint markers. Hover a structure to see its progress.
4. Stop Play. Sync Infrastructure is off again automatically.
5. For increment 2, also tick **Stabilizer Supply Enabled** in the same block (and, to try S4, **Reality Integrity
   Defeat** further down) during Play mode before placing anything. See §8.

## 8. Increment 2 — U2 Stabilizer supply and recovery safeguards S1–S4

**Authorized 2026-11-25** (Director: "Approve S1–S4 and start increment 2"). The supply tuning (generation,
consumption, storage, effectiveness curve) is **not decided**: every number below is a provisional test value.

### 8.1 Behavior

| Area | Behavior |
|---|---|
| Opt-in | `Sync Infrastructure → Stabilizer Supply Enabled` (needs `Enabled` too). Default **off**: Stabilizers need no supply, exactly as in increment 1 and the Sandbox. |
| Upkeep (U2) | Every completed Stabilizer consumes `stabilizerUpkeepPerTick` Sync Charges per tick, from what it received this tick plus its store. |
| Store (S3) | Each Stabilizer stores up to `stabilizerStoreCapacity` charges. Connected Stabilizers request upkeep plus refill (up to `maxSupplyIntakePerTick`). A disconnected Stabilizer keeps working on its store; brief disconnections do not drop it to zero at once. |
| Effectiveness | Share of the upkeep actually consumed this tick (linear). It scales the Stabilizer's whole stabilization. Full = 100 %; partial = reduced; nothing and an empty store = **zero stabilization**. |
| Allocation | Stabilizer supply is the second allocator category: one charge at a time, alternating with construction, placement order within each category (Option A). |
| Foothold (S2) | The first `footholdStabilizerCount` completed Stabilizers (placement order) get their upkeep before anything else, and each Sync Core always generates at least their total upkeep. |
| Safe zone (S1) | Blueprints within `coreSafeZoneRadius` of a Sync Core count as on buildable ground even when contaminated: there is always somewhere to rebuild from. Applies whenever Sync Infrastructure is on. |
| Defeat (S4) | `GridManager → Reality Integrity Defeat` (separate opt-in, default off). Below `defeatIntegrityThreshold`, the mission fails: `MissionDefeated` latches, the simulation pauses, the HUD says MISSION FAILED. Never after the objective is complete. |
| Diagnostics | HUD line *Stabilizer Supply* (supplied, on store, reduced, starved, delivered/demand, stored), hover text with effectiveness and store, grey marker = starved. |
| Connectivity | Unchanged: Desync and starvation never disconnect or stop relaying (suppression rules stay deferred). |

### 8.2 Provisional values (Director decision pending: Part 4 item 6)

| Setting | Test value | Effect at the Sandbox tick rate (0.1 s) |
|---|---|---|
| `coreGenerationPerTick` | 4 (scene default); tests use 8 | 8 per tick sustains four Stabilizers at full strength |
| `stabilizerUpkeepPerTick` | 2 | Effectiveness steps of 50 % |
| `stabilizerStoreCapacity` | 20 | About 1 s of autonomy when disconnected |
| `maxSupplyIntakePerTick` | 4 | An empty store refills in about 1 s |
| `footholdStabilizerCount` | 2 | Generation never below 4 per tick with U2 on |
| `coreSafeZoneRadius` | 3 cells | About 28 cells around the Core always buildable |
| `defeatIntegrityThreshold` | 50 % | D1: configurable, provisional until playtest |
| Effectiveness curve | linear (consumed ÷ upkeep) | |

### 8.3 Engineering assumptions for increment 2 (Director review)

| # | Assumption |
|---|---|
| B1 | **Director decision 2026-11-25: kept as the provisional first-playable design.** S1 is the Sync Core's *emergency Reconstruction radius*: within it, blueprints may be built on contaminated ground. It provides **no passive stabilization and no immunity to Desync**; the Core's surroundings can still become contaminated. The radius (`coreSafeZoneRadius`, 3 cells) stays configurable and provisional until playtesting. The specification's alternative example (a small fixed stabilization at the Sync Core) was not adopted. |
| B2 | **S2's foothold** = the first completed Stabilizers by placement order (2 by default). If one is removed, the next-oldest becomes part of the foothold. Only their upkeep is reserved, not their refill. |
| B3 | With U2 on, a **disconnected** Stabilizer stays active on its store (U2/S3: "stores deplete"); with U2 off it stops at once, as before. |
| B4 | A Stabilizer **completed this tick** starts with an empty store and gets supply from the next tick, so it shows *Starved* for one tick. |
| B5 | The Sync Core still stores nothing; only Stabilizers store (S3). |
| B6 | **S4 defeat** pauses the simulation and logs; the results/restart flow belongs to the mission-flow increment (5). Defeat is disabled after the objective is complete (D4). |
| B7 | Nullifiers still need no supply (N1-A belongs to increment 3). |

### 8.4 Required acceptance scenario: severed connection (automated)

`StabilizerSupplySceneTests.SeveredConnection_DownstreamStarves_DesyncAdvances_AndRebuildingRecovers`, in the real
simulation with an Emitter at (50,52):

1. Sync Core (50,85) → S1 (50,78) → S2 (50,71) → S3 (50,64), built from blueprints and fully supplied after 200 ticks.
2. The player removes S1.
3. S2 and S3 are disconnected, receive nothing, run on their stores for about 10 ticks, then stabilize nothing; Desync
   advances into their former footprints. Measured against an **unsevered control run** with the game's own threshold:
   centre cell, footprint contamination, effectiveness and connectivity reported separately.
4. A new blueprint in the gap completes; S2 and S3 reconnect, supply resumes, and Desync in S3's footprint is pushed back.
5. At every stage a legal recovery action exists (connected Sync Core, generation ≥ one upkeep, free ground in the S1 zone).

`S1_PlayerCanRebuildFromTheSyncCore_AfterCollapse` checks the "can the player still recover?" case where Desync has
already covered the ground around a newly placed Sync Core.

### 8.5 Required acceptance scenario: prolonged network collapse

Required by the Director on 2026-11-25; implemented and executed the same day (Director: "Proceed with implementing
and executing the prolonged-collapse acceptance test", within increment 2's verification scope) as
`ProlongedCollapseSceneTests`. It covers what §8.4 does not: there the rebuilt gap is still clean, and the S1 test
only shows that a blueprint can complete.

1. Sever the network and let the collapse continue long enough that the **original connection gap itself becomes
   contaminated** (outside the S1 radius, so a blueprint there cannot simply be rebuilt).
2. The player must **rebuild progressively from the Sync Core**: start inside the emergency Reconstruction radius, then
   extend outward step by step as completed Stabilizers clean the ground for the next blueprint.
3. Pass criterion: **recovery is achievable**: the network reaches and reconnects the stranded Stabilizers and Desync
   in their footprints is pushed back. Completing a single blueprint is not enough. Report it with the metric standard
   (centre, footprint, effectiveness and connectivity separately), against the provisional values in §8.2.
4. If recovery is not achievable with the provisional values, report it as a finding for the tuning decision rather
   than changing the values.

Balancing is deferred until playtesting; the Sandbox baseline stays unchanged.

#### 8.5.1 Implementation

Same layout as §8.4 (Emitter (50,52); Sync Core (50,85); line (50,78) → (50,71) S2 → (50,64) S3), held for 200 ticks.
The player removes (50,78); the collapse runs until (50,78) itself is contaminated, then 300 ticks more. The
rebuild then starts from the emergency radius, with two scripted players using only existing player actions
(place, remove):

- **Naive:** one Stabilizer at (50,82) (3 cells from the Core), then a blueprint on the gap (50,78) once it is complete.
- **Competent:** as many Stabilizers inside the emergency radius as generation sustains at full strength
  (generation ÷ upkeep: 2 at the defaults, 4 at generation 8). Then 2-cell steps toward the gap ((50,80), then (50,78)),
  each placed once the previous step is complete. After each step completes, the rearmost Stabilizer is removed to
  stay within supply. Once reconnected, the helpers are removed, so supply goes to the link and the stranded
  Stabilizers.

Two supply configurations, neither changed to make the test pass: the **shipped provisional defaults** (generation 4)
and the **test-fixture generation** of §8.4 (8). Reality Integrity defeat (S4) is off, so the rebuild can be measured.

#### 8.5.2 Results (headless run `id-inc2-collapse-3`, 2026-11-25)

| Run | Gap contaminated after the cut | Reconnected | Gap clean at end | S2 reclaimed | S3 reclaimed | Corridor contaminated (start → end) | S2 + S3 footprints contaminated |
|---|---|---|---|---|---|---|---|
| Defaults, naive | 468 ticks | **never** (4,000 ticks) | no | no | no | 390 → 390 | 162 → 162 |
| Defaults, competent | 468 ticks | **yes**, rebuild tick 132 | yes | **yes** | **no** | 390 → 294 | 162 → 114 |
| Generation 8, competent | 524 ticks | **yes**, rebuild tick 90 | yes | **yes** | **yes** | 390 → 240 | 162 → 45 |

The "corridor" is x 44–56, y 59–88, 390 cells. Determinism: two fresh runs of the defaults/competent scenario
produced identical results (grid Desync sum, event ticks, footprints).

**Recovery is achievable with progressive Reconstruction from the emergency radius, but not with a single Stabilizer.**
Findings (for the Director's tuning decision; nothing was changed):

- **F1 — reach, not supply, limits the naive rebuild.** A full-strength Stabilizer's stabilization falls off linearly
  over its 5-cell radius (20 % at 4 cells). One Stabilizer 4 cells from the gap brought the gap's Desync from about 38
  down to about 13, but never below the threshold of 5. The front kept advancing, so the blueprint waited for clean
  ground indefinitely, with charges unused every tick. Clustering 2–4 Stabilizers in the emergency radius and
  stepping 2 cells at a time cleaned the gap.
- **F2 — at the defaults, supply limits how much is reclaimed.** Generation 4 sustains two Stabilizers at full
  strength. After reconnection, the gap and S2 were supplied (demand 8, delivered 4) and reclaimed; S3's site was not.
- **F3 — at the defaults, construction is starved once two Stabilizers stand.** S2's foothold reservation
  (2 × upkeep 2 = 4) equals the whole generation of 4, so construction receives nothing while two connected,
  completed Stabilizers exist. In the setup, S3 stayed a blueprint for the whole hold. The competent rebuild worked
  only because one foothold slot was a disconnected Stabilizer, whose reservation is skipped.
- **F4 — the foothold follows placement order, not the player's current network.** At the moment of reconnection
  the oldest Stabilizers (the stranded ones, deep in Desync) become the foothold and are fed first; the new link
  starves until the player removes helpers.
- **F5 — S4 timing.** Reality Integrity was already about 60 % when the rebuild began and fell below the provisional
  50 % defeat threshold at rebuild tick 103–137, about the moment of reconnection. With S4 on at 50 %, this mission
  would be lost before reclaiming anything. Integrity kept falling afterwards (about 15 %), because the Emitter keeps
  spreading elsewhere and Sandbox decay is 0.
