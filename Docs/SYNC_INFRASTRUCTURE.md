# Sync Infrastructure — Sync Charges, Reconstruction, Stabilizer supply and safeguards

**Status:** roadmap increments 1, 2, 2A (playability pass, §8.6) and 3 (Nullifier reconfiguration, §9) implemented,
opt-in, verified by automated EditMode tests; 2A was merged into the increment 3 branch on 2026-11-26 (§9.5). Gate B
(the Director's interactive Sandbox playtest) passed on 2026-11-25. The playtest scenes and overlay have not yet been
played by hand, so controls, presentation and feel remain unverified.
**Design reference:** `Docs/DESIGN_DECISIONS.md` §4.1 (D2-A Reconstruction), §4.2 (U2 supply), §4.3 (Option A
allocation) and §7 item 3 (safeguards S1–S4, approved by the Director on 2026-11-25). Increment 2 (U2 supply and
safeguards) is described in §8 below; increment 3 (Nullifier reconfiguration N1–N3 with N1-A, `DESIGN_DECISIONS.md`
§4.5) in §9.

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
   `RenderGrid()`: unchanged, in the previous order. With increment 3's opt-in on, `UpdateNullifiers()` runs the
   staged reconfiguration instead of the Integrity drain (§9).

## 3. Allocation rule (`SyncChargeAllocator`)

- Charges are whole units, handed out one at a time.
- **Across categories:** round-robin in the fixed order Construction → Stabilizer supply → Nullifier operation, so no
  category with demand starves. Only Construction has consumers in increment 1; Stabilizer supply is used by U2
  (increment 2) and Nullifier operation by N1-A (increment 3), each only while its opt-in is on.
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
| `ProlongedCollapseSceneTests` (increment 2, re-run in 2A) | §8.5 prolonged collapse in the real simulation. Since increment 2A it runs in the playtest scene as saved (§8.6): naive player (diagnostic), competent player at the 2A defaults (F3: S3 completes in the hold; recovery), a generation-4 comparison with the 2A rules, determinism. |
| `StabilizerSupplyTests` (increment 2, extended in 2A) | U2 supply, store filling, S3 running on store then starving, partial supply, over-expansion, S2 foothold priority and generation floor, S1 safe zone, U2 off, the severed-connection scenario, determinism. 2A: construction share (rounding, never starved at generation 4, unused share returned, order after the foothold), shipped defaults, foothold by distance (nearest first, stranded Stabilizers excluded, ties by placement order, the reconnection case of F4). |
| `SyncDebugOverlayTests` (increment 2A) | The overlay text: Sync off, charges, reservations, upkeep, blueprint progress, each Stabilizer, MISSION FAILED, truncation, and that building it changes nothing. |
| `PlaytestSceneTests` (increment 2A) | The playtest scene's settings and starting Emitter, the overlay starting hidden, the five-item manual checklist scripted step by step with the same coordinates, the S4 defeat tick without player action, determinism. |
| `StabilizerSupplySceneTests` (increment 2) | In the real simulation: the **required severed-connection acceptance scenario** (compared with an unsevered control run), recovery from a collapse around the Sync Core, effectiveness scaling stabilization, S4 defeat on/off/after victory, Sandbox defaults off. |
| `NullifierReconfigurationTests` (increment 3) | Opt-in defaults, off = no requests, preparation with Sync Charges, disconnected or out-of-range Nullifiers, blueprint Nullifiers, manual initiation only from Ready and once per sequence, stage timing and pulses, interruption resets only the current stage, one vs. four Nullifiers, removing the initiating or the last Nullifier, waiting for a full pulse store, sharing charges with construction, determinism. |
| `IntegratedAllocationTests` (2A + 3 integration) | Every consumer at once: the order foothold → construction share → round-robin; Nullifier preparation never taking the foothold upkeep or the construction share; Nullifier Ready time; finding F7 (contention delay); charge conservation and determinism over a mixed run. |
| `CombinedPlaytestSceneTests` (2A + 3 integration) | The combined playtest scene's settings, the combined checklist scripted with the same coordinates (reconfigured before defeat), the F6 late-start diagnostic, determinism. |
| `FullLoopProgressionTests` (overnight assignment, 2026-11-26) | The full loop from a Sync Core near the map rim in the combined scene as saved: idle control, direct advance, defense-first advance and the lean best case; the best case from all four rim directions and with late starts; a Sync Core distance sweep; determinism. Diagnostics that print measurements (§9.6). |
| `NullifierReconfigurationSceneTests` (increment 3) | In the real simulation at the shipped defaults: Sandbox keeps the drain; the full sequence (build, Ready, no progress without initiation, three stages, production stops, objective flag); determinism; severing the network mid-stage; one vs. three Nullifiers; U2 on at generation 8; a diagnostic of U2 on at the shipped defaults (finding F3). |

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
6. For increment 3, also tick **Nullifier Reconfiguration Enabled** in the same block, before placing anything. Place a
   Core, then a Nullifier (`3`) within 6 cells of the Emitter and connected to the network; when its marker turns
   pale and pulses (Ready), press **N** with the cursor near it. See §9.
7. Since increment 2A there is a ready-made scene instead: open `Assets/_CoreFiles/Scenes/Playtest_SyncSupply.unity`
   and press **Play**. It has Sync Charges, Reconstruction, U2 and S4 on and starts with an Emitter at the grid centre.
   Press **`** (backquote) for the debug overlay. See §8.6.
8. To play increments 2A and 3 together, open `Assets/_CoreFiles/Scenes/Playtest_Combined.unity` instead. It is the
   same scene with Nullifier reconfiguration on: place a Nullifier, wait for Ready, then press **N**. See §9.5.
9. To play it without the Editor, build a standalone development player of the combined scene:
   `Unity -batchmode -executeMethod PlaytestDevBuild.BuildCombinedWindows64 -dfsBuildOut <folder outside the project> -quit`
   (Windows x64, Mono, development build, `Playtest_Combined.unity` only). The scene list is passed to the build
   directly, so `EditorBuildSettings` and the Player settings are not changed. See §9.6.

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
| Foothold (S2) | The first `footholdStabilizerCount` completed Stabilizers (placement order) get their upkeep before anything else, and each Sync Core always generates at least their total upkeep. **Changed in increment 2A (§8.6):** the foothold is the connected Stabilizers nearest a Sync Core, and the generation floor also covers the construction share. |
| Safe zone (S1) | Blueprints within `coreSafeZoneRadius` of a Sync Core count as on buildable ground even when contaminated: there is always somewhere to rebuild from. Applies whenever Sync Infrastructure is on. |
| Defeat (S4) | `GridManager → Reality Integrity Defeat` (separate opt-in, default off). Below `defeatIntegrityThreshold`, the mission fails: `MissionDefeated` latches, the simulation pauses, the HUD says MISSION FAILED. Never after the objective is complete. |
| Diagnostics | HUD line *Stabilizer Supply* (supplied, on store, reduced, starved, delivered/demand, stored), hover text with effectiveness and store, grey marker = starved. |
| Connectivity | Unchanged: Desync and starvation never disconnect or stop relaying (suppression rules stay deferred). |

### 8.2 Provisional values (Director decision pending: Part 4 item 6)

| Setting | Test value | Effect at the Sandbox tick rate (0.1 s) |
|---|---|---|
| `coreGenerationPerTick` | 4 (scene default); tests use 8. **8 since increment 2A** | 8 per tick sustains four Stabilizers at full strength |
| `stabilizerUpkeepPerTick` | 2 | Effectiveness steps of 50 % |
| `stabilizerStoreCapacity` | 20 | About 1 s of autonomy when disconnected |
| `maxSupplyIntakePerTick` | 4 | An empty store refills in about 1 s |
| `footholdStabilizerCount` | 2 | Generation never below 4 per tick with U2 on |
| `coreSafeZoneRadius` | 3 cells | About 28 cells around the Core always buildable |
| `defeatIntegrityThreshold` | 50 %. **30 % since increment 2A** | D1: configurable, provisional until playtest |
| Effectiveness curve | linear (consumed ÷ upkeep) | |

### 8.3 Engineering assumptions for increment 2 (Director review)

| # | Assumption |
|---|---|
| B1 | **Director decision 2026-11-25: kept as the provisional first-playable design.** S1 is the Sync Core's *emergency Reconstruction radius*: within it, blueprints may be built on contaminated ground. It provides **no passive stabilization and no immunity to Desync**; the Core's surroundings can still become contaminated. The radius (`coreSafeZoneRadius`, 3 cells) stays configurable and provisional until playtesting. The specification's alternative example (a small fixed stabilization at the Sync Core) was not adopted. |
| B2 | **S2's foothold** = the first completed Stabilizers by placement order (2 by default). If one is removed, the next-oldest becomes part of the foothold. Only their upkeep is reserved, not their refill. **Superseded by increment 2A (F4, §8.6):** nearest connected Stabilizers. |
| B3 | With U2 on, a **disconnected** Stabilizer stays active on its store (U2/S3: "stores deplete"); with U2 off it stops at once, as before. |
| B4 | A Stabilizer **completed this tick** starts with an empty store and gets supply from the next tick, so it shows *Starved* for one tick. |
| B5 | The Sync Core still stores nothing; only Stabilizers store (S3). |
| B6 | **S4 defeat** pauses the simulation and logs; the results/restart flow belongs to the mission-flow increment (5). Defeat is disabled after the objective is complete (D4). |
| B7 | Nullifiers still need no supply (N1-A belongs to increment 3). Superseded by increment 3 while its opt-in is on (§9). |

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

### 8.6 Increment 2A — playability pass

**Authorized 2026-11-26** (Director: "Increment 2A: Playability Pass"; plan approved the same day). Fixes F3 and F4,
sets provisional values, and adds a playtest scene and a debug overlay. Every value stays **provisional**; decision 5
(supply tuning) stays open beyond these values. `Sandbox.unity` is unchanged.

| Area | Behavior |
|---|---|
| F3: construction share | Each tick, after the foothold's upkeep is reserved, `constructionMinimumShare` (25 %) of generation, rounded up, is reserved for eligible blueprints (placement order). If no blueprint can use it, it returns to the general allocation, so nothing is wasted. With U2 on, a Sync Core always generates at least the smallest amount that covers both the foothold upkeep and the share (6 with the 2 × 2 foothold); at generation 8 this changes nothing. With construction as the only consumer (U2 off) the result is identical to before. |
| F4: foothold by distance | The foothold is the `footholdStabilizerCount` **connected**, completed Stabilizers nearest a connected Sync Core (straight-line distance; ties by placement order). Stranded Stabilizers never hold a slot, and a newly built link near the Core is fed first. Supersedes B2. |
| Provisional values | `coreGenerationPerTick` 8 (was 4); `defeatIntegrityThreshold` 30 % (was 50 %); `constructionMinimumShare` 25 % (new). S4 itself stays opt-in. |
| Playtest scene | `Assets/_CoreFiles/Scenes/Playtest_SyncSupply.unity`, generated from `Sandbox.unity` by `PlaytestSceneBuilder` (Editor menu *Imminent Desync → Rebuild Sync Supply Playtest Scene*, or headless `-executeMethod PlaytestSceneBuilder.Build`). It is the Sandbox with Sync Charges, Reconstruction, U2 and S4 on, the values above, and **an Emitter placed at the grid centre (50,50) at start** (Director: so the manual and automated tests start the same way). Sandbox controls are kept. |
| Debug overlay | **`** (backquote) shows or hides a text-only panel (top right): charges generated / delivered / unused, construction and supply delivered of demand, the foothold and construction-share reservations, upkeep, each blueprint's progress, each Stabilizer's effectiveness, status and store (`[foothold]`, `DISCONNECTED`), Reality Integrity and the S4 threshold. Read-only (`SyncDebugOverlay`); starts hidden; also available in the Sandbox, where it only appears when toggled. |

#### 8.6.1 Results (headless run `id-inc2a-tests-1`, 2026-11-26)

EditMode **67/67 passed** (49 earlier + 18 new). Sandbox smoke test 63/63 with its determinism repeat; final total
Desync 21332.911624925426 = recorded baseline. The prolonged collapse (§8.5) re-run **in the playtest scene as saved**
(Emitter at (50,50), so absolute numbers are not directly comparable with §8.5.2, where it was at (50,52)):

| Run | S3 built during the hold | Gap contaminated after the cut | Reconnected | S2 / S3 reclaimed | S2 + S3 footprints contaminated | Corridor | S4 (30 %) MISSION FAILED |
|---|---|---|---|---|---|---|---|
| 2A defaults, naive | yes | 571 ticks | **never** (4,000 ticks) | no / no | 162 → 162 | 390 → 390 | run tick 1407 |
| 2A defaults, competent | yes | 571 ticks | **yes**, rebuild tick 93 | **yes / yes** | 162 → 43 | 390 → 238 | run tick 1423 (rebuild tick ≈ 352, about 260 ticks after reconnection) |
| Generation 4 with the 2A rules (comparison) | **yes** (was never in §8.5.2) | 566 ticks | **yes**, rebuild tick 104 | **yes / yes** | 162 → 45 | 390 → 240 | run tick 1421 |

Determinism: two fresh runs of the competent scenario were identical. The scripted checklist run (`PlaytestSceneTests`):
the first two Stabilizers complete 20 ticks after placement; a third, placed while two stand, completes in 20 ticks,
receiving at least 2 charges every tick; once complete, the foothold moves to the Stabilizers nearest the Core; with
five Stabilizers (upkeep 10 against generation 8) four run at full strength and the newest starves.

Findings (for the Director; nothing was tuned beyond the brief):

- **F3 fixed.** Construction receives at least 2 charges every tick while two foothold Stabilizers stand, at
  generation 8 and, through the raised floor, at generation 4.
- **F4 fixed.** After reconnection the rebuilt link nearest the Core is in the foothold; stranded Stabilizers are not.
- **F1 unchanged (not in 2A scope).** The naive single-Stabilizer rebuild still never reaches the gap: reach, not
  supply, limits it (charges are left unused every tick).
- **F6 — S4 at 30 % ends every measured mission at about 140–147 s.** Reality Integrity is map-wide, and the Emitter
  keeps spreading elsewhere (Sandbox decay 0). With no player action, MISSION FAILED at tick 1389 (139 s at
  0.1 s/tick); with the checklist build near the Core, tick 1465; in the collapse runs, 1407–1423, about 260 ticks
  after a successful reconnection, while territory was still being reclaimed. Local Reconstruction does not move this
  much; whether a player can reach and drain the Emitter with a Nullifier in time was **not tested** and is the
  main question for the manual playtest. Input to decision 5 and to S4 (D1).

## 9. Increment 3 — Nullifier reconfiguration N1–N3 with N1-A

**Authorized 2026-11-25** (Director: "Approve increment 3"; plan approved the same day). Decision 2 (stage conditions,
durations, effects, redundancy behavior and charge costs) is **open**: every number and every choice marked C below is
provisional. N4 (victory latch, Restoration, Emitter removal) and the recovery mathematics belong to increment 4.

### 9.1 Behavior

| Area | Behavior |
|---|---|
| Opt-in | `GridManager` → **Sync Infrastructure** → `nullifierReconfigurationEnabled`, only together with `enabled`. Default **off**; `Sandbox.unity` does not contain it, so the Sandbox keeps the Integrity drain exactly. |
| Preparation (N1, N1-A) | A completed, connected Nullifier with an active Emitter in range fills a **pulse store** with Sync Charges (allocation category *Nullifier operation*). It is **Ready** when the store holds one pulse. |
| Initiation (N1) | Manual, once per sequence: **N** initiates from the Ready Nullifier nearest the cursor. `GridManager.TryInitiateReconfiguration` is the simulation command; the key only calls it. |
| Stages (N2) | Synchronize → Disrupt → Reconfigure. Each needs `reconfigurationStageTicks` qualifying ticks, then a pulse that uses one full store. A qualifying tick has at least one completed, connected Nullifier targeting that Emitter. |
| Interruption (N3) | A tick with no qualifying Nullifier resets the current stage's progress. Completed stages are kept on the Emitter. |
| No acceleration (N3) | Progress belongs to the Emitter and advances at most once per tick, so one or many Nullifiers finish on the same tick. No placement limit. |
| End state | Each completed stage lowers the Emitter's Integrity by a third; the final stage sets it to 0, the drain's existing end state: production stops and the existing objective flag fires, exactly as with the drain. |
| Diagnostics | HUD *Nullifiers* and *Reconfiguration* lines (only while on), hover text on Nullifiers and the Emitter, marker colors (dim violet preparing, pale pulsing Ready, violet engaged, gray inactive). |

### 9.2 Provisional values (decision 2 open)

| Setting | Value | Meaning |
|---|---|---|
| `nullifierPulseCost` | 30 | Charges per pulse; also the store capacity. |
| `maxNullifierIntakePerTick` | 2 | So a store fills in 15 ticks (1.5 s at the Sandbox tick rate). |
| `reconfigurationStageTicks` | 150 | Per stage; 450 ticks (45 s) in total, close to the drain's 500 ticks for one Nullifier. |
| `nullifierConstructionCost` | 60 | Unchanged from increment 1. |

### 9.3 Engineering assumptions for increment 3 (Director review)

| # | Assumption |
|---|---|
| C1 | **Readiness** = completed, connected, an active Emitter in range, full pulse store. The proposed "Nullifier's own cell must be clean" condition is **not** included. |
| C2 | Nullifiers have **no upkeep**; a disconnected Nullifier keeps its store but does not refill. |
| C3 | Initiation is **once per sequence**; after an interruption the sequence continues without re-initiation. |
| C4 | **Redundancy:** any qualifying Nullifier sustains a started sequence (resilience, never speed). The pulse comes from the oldest full store. |
| C5 | When a stage's time is complete but no store is full, the stage **waits** with its progress held ("waiting for a full pulse store"); it is not reset. |
| C6 | **Stage effects:** Synchronize and Disrupt have no simulation effect (Disrupt does not reduce Emitter output); only the Integrity display steps down. |
| C7 | Durations count **simulation ticks**, so they pause and single-step with the simulation and are deterministic. |
| C8 | With the opt-in on, the Integrity drain does not run. Turning the opt-in off mid-sequence returns to the drain from the current Integrity (not tested). |
| C9 | Sandbox debug: removing an Emitter discards its progress, since the progress belongs to the Emitter. |
| C10 | Blueprint rules are unchanged: a Nullifier blueprint still needs clean ground or the S1 emergency radius (D2-A). |

### 9.4 Results (headless run `id-inc3-nullifier-2`, 2026-11-25)

EditMode **72/72 passed**. The Sandbox smoke test passes 63/63 and matches the recorded baseline exactly (final total
Desync 21332.911624925426), so the Sandbox keeps the drain.

| Scenario (real simulation, Sandbox scene) | Result |
|---|---|
| Shipped defaults, U2 supply off (generation 4) | Nullifier blueprint built in 30 ticks, Ready 15 ticks later; no progress in 200 ticks without initiation; stages complete 150 / 300 / 450 ticks after initiation; production stops and the objective flag fires. |
| Same run twice | Identical (completion tick and grid Desync sum). |
| Sync Core removed 100 ticks into stage 2 | Stage 2 restarts, stage 1 is kept, one interruption; after the Core is rebuilt, 300 more ticks (stage 2 in full, then stage 3) with no re-initiation. |
| One vs. three Nullifiers | Both 450 ticks. |
| U2 supply on, test-fixture generation 8, two-Stabilizer foothold | Nullifier Ready 45 ticks after placement; reconfigured 450 ticks after initiation. |
| **U2 supply on, shipped defaults (generation 4)** — diagnostic | **The Nullifier blueprint received 0 of 60 charges in 2,000 ticks** (*waiting for charges*): the foothold's reserved upkeep (2 × 2) takes the whole generation of 4. This is finding F3 (§8.5.2) applied to the Nullifier: at these defaults a Nullifier can never be built once two Stabilizers stand. Input to decision 5; nothing was changed. |

**Not verified automatically:** the N key, HUD lines, hover text, marker colors and the Ready pulse, and how the
sequence feels to play. These need the Director's playtest.

### 9.5 Integration with increment 2A (2026-11-26)

**Approved by the Director on 2026-11-26** ("Approve the proposed integration of Increment 2A into the existing
Increment 3 branch using a merge, preserving commit ad6030e and its history"). Increment 2A (`9f4c976`) was merged into
this branch as a merge commit. `ad6030e` is unchanged and nothing was rebased. No gameplay value was changed by the
integration; F6 stays an open balancing finding. The table in §9.4 records increment 3 alone at the time; at the
merged defaults (generation 8, 25 % construction share) its last row no longer applies.

**Charge order in one tick** (with every opt-in on):
1. The foothold's upkeep: the 2 connected Stabilizers nearest the Sync Core.
2. The construction share: 25 % of generation, for eligible blueprints.
3. Round-robin, one charge at a time, across construction, Stabilizer supply and Nullifier preparation, in that order.

**Combined playtest scene:** `Assets/_CoreFiles/Scenes/Playtest_Combined.unity` (builder:
`PlaytestSceneBuilder.BuildCombined`). It is the 2A playtest scene with `nullifierReconfigurationEnabled` on, so N1–N3
replaces the Integrity drain. The 2A scene `Playtest_SyncSupply.unity` keeps the drain and is unchanged.

**Results** (headless run `id-integ-tests-1`): EditMode **99/99 passed**. That is 67 from 2A, 23 from increment 3,
`IntegratedAllocationTests` (5) and `CombinedPlaytestSceneTests` (4). The Sandbox smoke test passes 63/63 and matches
the recorded baseline exactly (final total Desync 21332.911624925426).

| Check | Result |
|---|---|
| Order with all three consumers (exact charge counts) | Foothold 4, construction share 2, then one charge each to supply and the Nullifier, out of 8. |
| Four preparing Nullifiers and three blueprints, 200 ticks | Foothold at 100 % every tick; the construction share is honored every tick; every blueprint completes; every charge is accounted for. |
| Nullifier with only the foothold competing | Ready in 15 ticks: 2 per tick into a 30-charge store. |
| Increment 3's F3 diagnostic at the merged defaults | Nullifier built 30 ticks after placement; it was 0 of 60 in 2,000 ticks before. It now asserts this. |
| Combined checklist, scripted (Core (50,57), Stabilizers (48,58) and (52,58), Nullifier (50,56), all placed at tick 0) | Stabilizers built at tick 20, Nullifier built at 30, Ready at 45, N at 45, reconfigured at **495** (450 after initiation, as in increment 3 alone). Reality Integrity 91.7 %, no defeat, foothold at 100 %. Deterministic. |
| F6 diagnostic: the same build started later | Start at tick 300: reconfigured at 795 (integrity 79.1 %). Start at 600: 1095 (52.3 %). Start at 900: 1395 (30.2 %, just above the threshold). Every run was **won**. |

**Finding F7 (recorded, not changed):** Nullifier preparation is last in the allocator's category order. When only two
charges remain after the foothold and the construction share, and both construction and Stabilizer refill still want
charges, the Nullifier receives nothing that tick. In the contention test it got its first charge at tick 21 and was
Ready at 40, against 15 uncontended. It is a delay, not starvation, while construction lasts, but a player who keeps
placing blueprints can hold Nullifier preparation back. Input to decision 2 or 5.

**F6 in the combined game:** a player who builds next to the Emitter (the S1 emergency radius makes the Nullifier site
always buildable) reconfigures it in about 50 s. Starting at 90 s (tick 900) still wins, just above the 30 % threshold.
How this feels by hand is the main question for the playtest. The threshold is unchanged.

### 9.6 Standalone Development Build 001 and full-loop validation (2026-11-26, overnight)

Assigned by the Director (`DESIGN_DECISIONS.md` §6). No gameplay code, tuning, scene or project setting was changed.

#### 9.6.1 Standalone Development Build 001

- **Builder:** `PlaytestDevBuild.BuildCombinedWindows64` (Editor only). It builds `Playtest_Combined.unity` alone as a
  Windows x64 development player (Mono) into a folder given on the command line, which must be outside the project.
  The scene list goes straight to `BuildPipeline`, so `EditorBuildSettings` (Sandbox only) and the Player settings are
  untouched.
- **Run `id-devbuild001-1`:** build succeeded at the tree of `532ae92` plus the builder. 0 errors, 1 warning (the
  existing CS0414 on `GridManager.placementMode`), 315 files, about 173 MB, one scene (`level0`), no Editor or test
  assemblies in the player.
- **Unity rewrote five tracked settings files during the build:** a format upgrade (`ProjectSettings.asset`
  serializedVersion 28 → 29, new default fields in it and `GraphicsSettings.asset`) and URP's build-time shader
  prefiltering data (`UniversalRP.asset`, `UniversalRenderPipelineGlobalSettings.asset`, `DefaultVolumeProfile.asset`).
  They are **not committed**. The Director decides whether to discard them or commit them separately, as for any
  Editor rewrite.
- **Startup was not verified.** Launching the built executable from the session was blocked by the studio's command
  guard, and per studio rules it was not retried another way. Startup, input, HUD and feel need the Director's run.

#### 9.6.2 Full gameplay loop from the map rim

**Test:** `FullLoopProgressionTests`, in the combined scene as saved (Emitter at (50,50), shipped provisional values).
A scripted player uses only player actions (placing structures through the game's own placement, and **N**), and
chooses cells from what the map shows. It respects S1 (it may build within 3 cells of the Sync Core on contaminated
ground) and keeps chain blueprints 2 cells away from visible contamination. Sync Core 44 cells north of the Emitter
(6 cells from the rim) unless stated. Results are identical in runs `id-fullloop-4` and `id-devbuild001-tests-1`.

| Run | Outcome | What happened |
|---|---|---|
| Idle control (Core + 2-Stabilizer foothold, then nothing) | **Lost**, S4 at tick 1413 | Foothold built at tick 20 and held at 100 %. Contamination reaches 6 cells from the Emitter by tick 100, 22 by tick 600, 34 by tick 1000. |
| Direct advance (foothold, then the next link as soon as the front completes) | **Lost**, S4 at 1450 | A relay 9 cells from the Emitter by tick 100. No buildable cell within Nullifier range from tick 75 on, so no Nullifier. 6 Stabilizers on 8 charges per tick: the 2 frontmost starve from tick 100. |
| Defense-first (each front fully supplied, full store and clean footprint before the next step; at most 150 ticks) | **Lost**, S4 at 1450 | The front stalls 16 cells from the Emitter at tick 250: by then every cell a next link could use is contaminated. |
| Lean direct, best case (no separate foothold, links 8 cells apart from the Core) | **Lost**, S4 at 1513 | Reaches Nullifier reach at tick 81 (four serial 20-tick builds). A 5th link 9 cells from the Emitter is needed to clear the Nullifier site, but it starves (5 × 2 upkeep > 8 generated). |
| Lean direct from the south, east and west rims | **Lost**, S4 at 1503 to 1577 | Same mechanism in every direction; terrain shifts the defeat tick only. |
| Lean direct, first action at tick 150 / 300 | **Lost**, S4 at 1494 / 1464 | Later starts only get worse: at tick 300 the chain cannot get closer than 17 cells. |

**Sync Core distance sweep** (lean direct, north): **won at every distance from 9 to 40 cells, lost at 42 and 44.**

| Sync Core distance | Chain links | Nullifier built | Reconfigured (integrity) |
|---|---|---|---|
| 9, 12, 14 | 0 (the Core reaches the Nullifier site) | tick 31 | 496 (87.4 %) |
| 16 to 22 | 1 | 51 | 516 (88.5–90.2 %) |
| 24 to 30 | 2 | 71 | 536 (87.9–89.6 %) |
| 32 | 3 | 91 | 556 (88.9 %) |
| 36 / 38 / 40 | 4 | 153 / 172 / 132 | 618 / 637 / 597 (85.7–87.5 %) |
| 42 | 4 | never: its blueprint waits for clean ground from tick 101 | lost, S4 at 1535 |
| 44 (rim) | 5, the 5th starves | not placeable | lost, S4 at 1513 |

**Findings (recorded, not changed):**

- **F8: the intended progression cannot be completed from the rim at the shipped values.**
  - The limiting mechanism is two rules together:
    - **The Nullifier site.** A Nullifier blueprint needs clean ground within 6 cells of the Emitter. Without a
      supplied Stabilizer near it, the whole Nullifier range is contaminated at tick 75 in every run, whatever the
      player does. `decayRate` is 0 in the scene, so it never clears by itself.
    - **The supply ceiling.** 8 charges per tick fully upkeep 4 completed Stabilizers. From 44 cells, the front has to
      be within about 9 cells to clean the site, which takes 5 links, so the frontmost starves at 0 %.
  - With the Core at 40 cells, 4 fully supplied links put one 8 cells from the Emitter, which pulls the site back to
    clean (Desync 4.8 at tick 94), and the run wins.
  - Inputs: decision 5 (generation, upkeep), decision 4 (blueprints on contaminated ground), decision 2 (Nullifier
    range and readiness), and map size.
- **F9: Sync Core placement has no constraint, so placing it near the Emitter dominates.**
  - A Core can go on any free cell, at any distance, even on contaminated ground.
  - Within 9 cells of the Emitter, S1 makes the Nullifier site always buildable, and the existing checklist wins in
    about 50 s.
  - The further the Core, the later the win. Defense-first never helps.
  - Whether the rim start is the intended mission start, and whether Core placement should be limited, is a Director
    decision (`DESIGN_DECISIONS.md` §7 item 10).
- **F6 (S4 at 30 %) did not bind in any run.** Every win finished by tick 640, and S4 came at ticks 1413–1577. Lost
  runs failed on F8 long before Integrity mattered. F6 stays open for late starts (§9.5).
- **F7 (Nullifier preparation last) did not bind either.** In every winning run the Nullifier got all it asked for once
  built (Ready 15 ticks after completion), because the scripted player stops placing chain links while the Nullifier is
  being built. At 36–40 cells, the Nullifier blueprint itself took 38–111 ticks instead of 30. At 40 its cell
  hovered at the threshold (Desync 4.8 to 5.1), so construction waited for clean ground.