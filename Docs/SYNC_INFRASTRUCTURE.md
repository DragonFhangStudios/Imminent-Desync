# Sync Infrastructure — Sync Charges and Reconstruction (roadmap increment 1)

**Status:** implemented, opt-in, verified by automated EditMode tests. **Not yet played by the Director**
(Gate B is pending), so its controls, presentation and feel are unverified.
**Design reference:** `Docs/DESIGN_DECISIONS.md` §4.1 (D2-A Reconstruction) and §4.3 (Option A allocation).

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

## 5. Not in this increment

U2 Stabilizer consumption, suppression and recovery variants, Nullifier reconfiguration, Emitter Restoration, The Basin,
player allocation controls (issue #2), UI art integration, travelling packet entities, routing or logistics interfaces.

## 6. Automated verification (EditMode, headless)

Run with the studio's locking runner (`-Mode Tests -TestPlatform EditMode`). Tests live in `Assets/_CoreFiles/Tests/EditMode`.

| Test class | Covers |
|---|---|
| `SandboxSmokeRegressionTests` | The 63-check Sandbox smoke test, its determinism repeat, and exact match with the 2026-11-23 recorded baseline (final total Desync, contaminated-cell history, ticks to nullify); Sandbox has Sync Infrastructure disabled. |
| `SyncChargeAllocatorTests` | Supply limits, demand limits, placement order, category round-robin, determinism. |
| `ReconstructionTests` | Generation, completion after exactly the cost, Nullifier cost, disconnected and contaminated blueprints, no relay until complete, blocked blueprints not blocking others, spam vs. throughput, scarce supply order, determinism. |
| `SyncInfrastructureSceneTests` | The same rules inside the real `GridManager` and Sandbox scene (enabled in memory only, never saved): no stabilization until complete, chain building, occupancy, contaminated placement, unfinished Nullifier, determinism, and disabled = instant building. |

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
