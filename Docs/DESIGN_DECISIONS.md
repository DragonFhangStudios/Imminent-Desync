# Imminent Desync — Design Decisions and Amendments

**Status:** this file records the design decisions as of 2026-11-24, under the Game Director's authorization of roadmap increment 0.
**Working design reference:** *Imminent Desync — Consolidated Gameplay Specification & Proposed Roadmap 2026-11-24*,
prepared from the Director's *Consolidated Director Gameplay Decisions* (Game Director and DFS Jr., 2026-11-24).
That specification is a studio document kept outside this repository.

## How to read this file

| Label | Meaning |
|---|---|
| **[Approved]** | Director-approved design decision. |
| **[Amendment]** | A recorded change to an earlier decision. The wording comes from the specification's Part 2 draft. The Director may still refine it. |
| **[Proposal]** | Engineering proposal; not approved. |
| **[Tuning]** | Numbers or curves not yet decided. |
| **[Deferred]** | Intentionally outside the first playable. |
| **[Open]** | A decision still needed from the Director. |

Design approval is not implementation approval. Implementation is authorized one roadmap increment at a time.
The current code and scenes are the source of truth for actual behavior; this file records design intent.

---

## 1. Historical baseline: GDD v1.0

- `Docs/Imminent_Desync_GDD_v1.0_Final.docx` (2026-09-27) is kept unchanged as the **historical design baseline**.
- A section-by-section reconciliation of GDD v1.0 against the November 24 decisions is **deferred** by the Director.
  Until that happens:
  - the November 24 specification and this file are the working reference for the newly approved decisions;
  - any GDD v1.0 requirement that this file does not mention has an **unknown status**. Ask before relying on it; do not
    assume it is still current or that it is superseded.
- GDD Amendment A is referenced in earlier documents but is not in the repository. Its contents are unknown.
- The specification says these GDD v1.0 sections become outdated once the amendments below are recorded: §9.4, §12, §15
  and §26. Their exact text has not been compared here.

## 2. Terminology [Approved]

| Term | Meaning | Current code (not renamed) |
|---|---|---|
| Sync Core | Central infrastructure and Sync Charge generator | `Core` |
| Sync Charges | Universal resource for construction, stabilization and Nullifier operation | `SyncCharge*` types (increment 1) |
| Sync Network | Connected infrastructure that distributes Sync Charges | `InfrastructureNetwork` |
| Reconstruction | Building infrastructure from blueprints using Sync Charges | `Reconstruction*` types (increment 1) |
| Suppression | Recoverable loss of infrastructure function | None; the nearest is the display-only Breached condition |

Proposed distinctions, used in this sense: **Reconfiguration** (correcting an Emitter through Nullifier pulses),
**Restoration** (recovery of the contaminated environment) and **Resynchronization** (returning suppressed
infrastructure to operation). Approving the terms does **not** authorize renaming code.

## 3. Earlier decisions (2026-11-23) and their current status

| # | Decision | Status |
|---|---|---|
| D1 | Defeat when Reality Integrity falls below a configurable threshold; the threshold is provisional until playtest. No other failure mechanics. | **[Approved]**, stands |
| D2 | Free, instant, unlimited building; no costs, cooldowns, limits or economy. | **Superseded in part** by D2-A (instant building and no economy). |
| D3 | A deliberate, fixed basin terrain through the smallest extension to the terrain generator; Sandbox noise behavior preserved. | **[Approved]**, stands |
| D4 | Victory flow: pause and objective-complete panel; Claim Victory → results; Continue → optional cleanup with defeat disabled; Restart resets the mission. | **[Approved]**, stands; now triggered at reconfiguration (N4) |
| D5 | Emitter on the basin floor near the centre; Sync Core on the elevated rim; the player builds downhill. | **[Approved]**, stands |
| D6 | The Basin is added to build settings later, through a controlled change; the Sandbox stays available. | **[Approved]** in principle; not done |
| D7 | Implementation of The Basin (Gate D) on hold until Gate B results are reviewed. | Stands. **Gate D is not authorized.** |

The original guardrails also stand: a single Emitter in The Basin, originality relative to Creeper World,
and no new mechanics beyond the approved ones.

## 4. November 24 decisions and recorded amendments

### 4.1 Reconstruction [Approved]: amendment D2-A

> **D2-A (replaces the instant and no-economy parts of D2):** "Blueprint placement remains free and immediate, on
> contaminated or clean ground subject to occupancy. Structures are built through Reconstruction, which requires clean
> ground, an operational Sync Network connection and Sync Charges, and takes time limited by supply. Unfinished
> structures provide no stabilization or relaying. No structure-count limits are imposed."

- [Tuning] Charge cost and build time per structure type.
- [Proposal] A blueprint on contaminated ground waits, visibly flagged, until its cell is clean. Blueprints are served in
  placement order, so building spam delays construction rather than diluting it.

### 4.2 Stabilizer supply [Approved]: record U2

- Stabilizers need a continuous Sync Charge supply.
- Insufficient supply reduces stabilization effectiveness.
- Complete starvation gives zero stabilization once stored charges run out.
- Players must protect infrastructure and keep supply connections intact. Severing a critical connection can cut supply
  downstream and let Desync spread again.
- [Tuning] Generation rate, consumption rate, per-Stabilizer storage, and the curve from supply to effectiveness.
- Implemented (opt-in) in roadmap increment 2, authorized 2026-11-25. The tuning above is still open.

### 4.3 Sync Charge allocation [Approved]: record Option A

- Distribution is automatic for the first playable.
- [Deferred] Player priority modes and manual allocation (GitHub issue #2 in this repository).
- [Proposal] Deterministic round-robin across consumer categories (construction, Stabilizer supply, Nullifier), then
  placement order within a category, evaluated once per simulation tick.

### 4.4 Infrastructure suppression [Approved direction; rules deferred]: suppression record

- Direction: recoverable infrastructure failure, not destruction.
- **Baseline preserved:** today's Stabilizer behavior. Stabilizers can be overwhelmed and become Breached; Breached is
  display-only; Desync does not interrupt network connectivity.
- The rules are deferred to a comparative playtest of three candidates: immediate suppression and recovery; delayed
  suppression and recovery; immediate suppression with delayed recovery. **No variant is selected.**

### 4.5 Nullifier reconfiguration [Approved, amended]: N1–N3 with amendment N1-A

- Automatic preparation subject to readiness conditions; manual initiation.
- Conditional reconfiguration pulses instead of continuous integrity damage.
- Completed stages keep their progress; an interrupted unfinished stage restarts.
- Additional Nullifiers cannot accelerate the reconfiguration of the same Emitter.

> **N1-A (replaces "no additional buildings or resource economy are required for activation"):** "The Nullifier prepares
> automatically while operationally connected and supplied with Sync Charges, and uses Sync Charges to execute
> reconfiguration pulses. No other resource or building is required."

- [Open] Stage conditions, durations, effects, redundancy behavior and charge costs.
- Implementation belongs to roadmap increment 3, which was authorized on 2026-11-25 (§6). Its plan was approved the same
  day and it is implemented opt-in, with provisional values and choices C1–C10 (`Docs/SYNC_INFRASTRUCTURE.md` §9).
  The open details above are unchanged.

### 4.6 Emitter lifecycle and victory [Approved]: record N4

1. A successful final reconfiguration stops Emitter production.
2. Victory is registered at successful reconfiguration.
3. The player may finish the mission or Continue (existing Continue behavior preserved: defeat disabled during cleanup).
4. The reconfigured Emitter temporarily assists environmental Restoration.
5. Residual Desync loses its ability to sustain itself and should gradually recover.
6. The Emitter is eventually removed, freeing its cell. Victory stays registered after removal.

- [Open] Recovery mathematics, Restoration strength, radius and duration. Note: with natural decay at 0 (the Sandbox
  value), removing growth alone does not remove Desync; it only diffuses. Recovery needs post-reconfiguration decay,
  Restoration clearing, the player's Stabilizers, or a combination.
- Implementation belongs to roadmap increment 4 (not authorized yet).

## 5. Withdrawn research findings: do not reuse

These findings from earlier research (2026-11-23/24) are **withdrawn**. The cause: the breach metric counted only the
centre cell, and the scripted players avoided dense Desync.

| # | Withdrawn finding | Current position |
|---|---|---|
| W1 | "Stabilizers can never fail" | Stabilizers are overwhelmed in heavy Desync and reach Breached in normal play. |
| W2 | "Suppression would almost never trigger" | Centre-cell and footprint triggers both fire in current play. |
| W3 | "Suppression needs supply-dependent strength" | Starvation is a possible second route to Suppression, not a requirement. |
| W4 | "No breaches / no soft-locks" results | To be re-measured with the metric standard below. |
| W5 | Restrict Nullifier stacking to one per Emitter | Stacking is current code behavior, not intended design; replaced by Emitter-owned reconfiguration progress. |
| W6 | Candidate A timing, defence-necessity and rush conclusions | Based on instant free building and integrity drain, both superseded. Candidate A is a historical baseline only. |

### Research metric standard (all future studies)

- Use the game's own Stabilizer condition values (Normal, Warning, Critical, Breached, as computed each tick).
- Measure separately: centre-cell contamination; footprint contamination; stabilization effectiveness (is Desync in the
  footprint rising or falling); operational connectivity.
- Include scripted placements inside dense Desync and near the Emitter, not only on clean ground.
- Never report "not observed in scripted runs" as "cannot happen".

## 6. Gates

| Gate | Status |
|---|---|
| A — import and compile | Passed |
| B — interactive Sandbox playtest by the Director | **Passed** (2026-11-25, Director) |
| C — The Basin feasibility research | Complete; historical (previous gameplay model) |
| D — The Basin implementation | **Not authorized** |
| E — end-to-end validation | Future |

**Limited exception (2026-11-24):** the Director authorized roadmap increments 0 and 1 (this record, automated test
infrastructure, Sync Charge generation and distribution, blueprint Reconstruction) before Gate B, as isolated,
opt-in infrastructure development. This does not authorize Gate D, The Basin, or later increments. Manual verification
that has not been performed is not marked as passed.

**2026-11-25:** the Director approved safeguards S1–S4 and authorized roadmap increment 2 (U2 Stabilizer supply, the
effectiveness curve, starvation and the recovery safeguards). Supply tuning (Part 4 item 6) is still undecided.
Increment 3 onward, Gate D and The Basin remain unauthorized.

**Gate B passed (2026-11-25):** the Director played the Sandbox in the Unity Editor and declared Gate B passed
("B passed"). Passing Gate B approves nothing else. Supply tuning is unchanged, and the increment 2 pull request is
not merged.

**Increment 3 authorized (2026-11-25):** directly after Gate B, the Director authorized roadmap increment 3
("Approve increment 3"): Nullifier reconfiguration N1–N3 with amendment N1-A (§4.5). Decision 2 (stage conditions,
durations, effects, redundancy and charge costs) remains open, so its values will be provisional. Gate D, The Basin,
increment 4 onward (including N4 and the recovery mathematics) and supply-tuning changes remain unauthorized.

**Increment 2A authorized (2026-11-26):** the Director authorized "Increment 2A: Playability Pass" to fix the
increment 2 findings and make Reconstruction and U2 playable by hand. Scope: F3 (construction never fully starves:
a minimum share of generation, provisionally 25 %, reserved for construction after the foothold's upkeep); F4 (the
foothold chosen by distance to the Sync Core, not placement order); provisional tuning (generation 8 per tick, S4
defeat at 30 % Reality Integrity); a Sandbox-style playtest scene with Sync Charges, Reconstruction and U2 on, which
starts with an Emitter placed (Director: so the manual and automated tests start the same way); a text-only debug
overlay; re-runs; a one-page report and a five-item manual checklist. The plan was approved the same day. All values
remain provisional; decision 5 stays open beyond these values. `Sandbox.unity` is unchanged. Increment 3 remains
approved and implemented, with publication held until 2A validation and the integration review are complete.
Increment 4 onward, Gate D and The Basin remain unauthorized. Details: `SYNC_INFRASTRUCTURE.md` §8.6.

## 7. Open Director decisions

1. Recovery mathematics after reconfiguration (post-victory decay, Restoration strength, radius and duration, or a combination).
2. Nullifier details: readiness conditions, stage durations and effects, redundancy behavior, charge costs.
3. ~~Recovery safeguards S1–S4~~ — **[Approved] 2026-11-25** (Director: "Approve S1–S4 and start increment 2"):
   S1 the Sync Core's surroundings always stay buildable; S2 generation always covers the starting foothold's upkeep;
   S3 Stabilizers keep a small charge store; S4 Reality Integrity defeat ends a truly collapsed mission. Implemented in
   increment 2 (see `Docs/SYNC_INFRASTRUCTURE.md` §8); their numbers remain provisional.
   **S1 interpretation [Approved, provisional] 2026-11-25:** S1 is the Sync Core's emergency Reconstruction radius.
   Construction is permitted on contaminated ground within it, without passive stabilization or immunity to Desync.
   The three-cell radius stays configurable and provisional until playtesting. Future acceptance testing must include
   a prolonged network collapse where the original gap becomes contaminated and the player rebuilds progressively from
   the Sync Core; the test passes only if recovery is achievable (§8.5 there). Further balancing is deferred to
   playtesting. The test was implemented and run on 2026-11-25. Recovery is achievable by progressive rebuilding, but
   not with a single Stabilizer. At the shipped defaults it is only partial, and it raises supply-tuning findings
   F1–F5 (`SYNC_INFRASTRUCTURE.md` §8.5.2). They are inputs to decision 5; nothing was changed.
4. Reconstruction details: costs and build times; the behavior of blueprints waiting on contaminated ground.
5. Supply tuning: generation, consumption, storage and the effectiveness curve.
6. The December 20 Basin target: move it, reduce the scope, or a two-step plan.
7. Scheduling of the comparative suppression playtest and of the replacement research study.
8. ~~Gate B completion~~ — **passed 2026-11-25** (§6). Gate D authorization is still open.
9. Map-boundary behavior and natural decay remain open design and test decisions. `decayRate` stays.

## 8. Deferred features

- Player priority modes and manual Sync Charge allocation (issue #2).
- Selection of a suppression and recovery rule (after comparative playtesting).
- Multiple Emitters, more Emitter types, more complex reconfiguration sequences.
- Excluded from the first playable: multiple charge types, mining, factories, currency, new relay types, drone
  pathfinding, destruction and repair, complex logistics interfaces.
