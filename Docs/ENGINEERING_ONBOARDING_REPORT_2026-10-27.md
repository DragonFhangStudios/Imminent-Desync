# Imminent Desync — Engineering Onboarding Report

**Prepared by:** Claude Code (implementation/engineering partner)
**For:** Andrew (Game Director), DFS Jr. (design/architecture)
**Date:** 2026-10-27
> **Historical snapshot.** This report describes the project as found on 2026-10-27, *before* the Git baseline. Statements such as "not a repository" and "no CLAUDE.md in the project root" were true then and are superseded by the baseline commit. Machine-specific paths have been generalized.

**Mode:** Read-only discovery. No project files were created, modified, moved or deleted. Unity was not opened or run. Git was not initialized. No packages or settings were changed. No other project was opened.

---

## 0. What I inspected

| Area | Sources read |
|---|---|
| Instructions | `CLAUDE.md` supplied with the assignment from a local downloads folder (**at that time there was no CLAUDE.md in the project root**) |
| Design | `Imminent_Desync_GDD_v1.0_Final.docx` (same folder; now in `Docs/`), text extracted to a scratch copy |
| Code | All 12 scripts in `Assets/_CoreFiles/Scripts/` (2,352 lines total), read in full |
| Scenes / prefabs | `Sandbox.unity`, `The Basin.unity`, `CellVisual.prefab`, `StabilizerNode.prefab`, HUD UXML |
| Docs | `Assets/_CoreFiles/Scripts/CONTROLS.md`, `Assets/CONTROLS.md` |
| Settings | `ProjectVersion.txt`, `manifest.json`, `ProjectSettings.asset`, `EditorBuildSettings.asset`, `EditorSettings.asset`, `VersionControlSettings.asset` |
| History | `Logs/Editor.log` (last Editor session, 2026-10-04) |

**Not inspected:** anything in Library (it is absent), the binary PNGs in `Assets/` (they are preview screenshots), and the contents of other projects.

---

## 1. Project identity and structure

| Item | Finding |
|---|---|
| Unity version | **6000.5.4f1** (Unity 6.5). `HUDController` uses `PanelRenderer`, a 6.5 API, so the project cannot be opened in an older 6.x version. |
| Render pipeline | URP 17.5.0, 2D Renderer (`Assets/Settings/Renderer2D.asset`) |
| Input | Input System 1.19.0 only (`activeInputHandler: 1`). The `InputSystem_Actions.inputactions` asset exists **but is unused**. All input is read directly through `Keyboard.current` and `Mouse.current`. |
| Asset serialization | Force Text (`m_SerializationMode: 2`) with Visible Meta Files, so the project is already Git-friendly |
| Build scenes | Only `Assets/_CoreFiles/Scenes/Sandbox.unity` |
| Build target | Standalone (PC). Identity values are still template defaults: `companyName: DefaultCompany`, `applicationIdentifier: com.DefaultCompany.2D-URP`. |
| Assemblies | **No `.asmdef` files.** Everything compiles into `Assembly-CSharp`. |
| Tests | **None.** `com.unity.test-framework 1.7.0` is installed but has no test assemblies. |
| Git | **Not a repository.** There is no `.gitignore` or `.gitattributes`. `com.unity.collab-proxy` (Unity Version Control) is installed, but I found no evidence it is in use. |
| Library | **Missing.** This is a rescue copy. The last Editor log shows the project was opened from the original drive. The first open of the rescue copy will run a full re-import. |
| Last known compile | Clean on 2026-10-04, with one warning: `CS0414 GridManager.placementMode is assigned but never used` |

```
Imminent Desync/
├─ Assets/
│  ├─ _CoreFiles/
│  │  ├─ Scenes/        Sandbox.unity (the game) · The Basin.unity (empty: camera only)
│  │  ├─ Scripts/       12 .cs files + CONTROLS.md (current)
│  │  ├─ Prefabs/       CellVisual.prefab (used) · StabilizerNode.prefab (unused)
│  │  └─ UnityToolkit/  NewUXMLTemplate.uxml (the HUD layout)
│  ├─ Settings/ · UI Toolkit/ · URP assets
│  ├─ CONTROLS.md       ← STALE duplicate (pre-Nullifier)
│  └─ *_preview.png ×5  experiment screenshots
├─ Packages/ · ProjectSettings/ · UserSettings/ · Logs/
└─ Assembly-CSharp.csproj · Imminent Desync.slnx   (generated)
```

**Packages:** the project carries several unused template packages: 2D Animation, Aseprite, PSD Importer, SpriteShape, Visual Scripting, Timeline, Multiplayer Center, and Collab Proxy. They are harmless. I recommend no change now, only a note for a future trim with your approval.

---

## 2. Systems inventory

### 2.1 Runtime architecture

`GridManager` (1,514 lines) is the hub. It owns input, the simulation, placement, the network, the Nullifier, telemetry and rendering. The other types are mostly plain data or stateless helpers.

```
Update() ──► input (Space / . / 1-4 / Esc / mouse / scroll)
   │
   └─ every tickRate s ─► Simulate()            Desync field + stabilization
                         UpdateNullifiers()     drain Emitter integrity, objective flag
                         CalculateIntegrity()   Reality Integrity, contaminated counts
                         CalculateActivity()    growth/recede/frontier telemetry
                         UpdateContainmentStatus()  network status string
                         RenderGrid()           10,000 CellVisual.SetCell()
   └─ every frame ──────► GetDebugStats() → HUDController.UpdateHUD()
```

| Script | Role | Notes |
|---|---|---|
| `GridManager.cs` | Simulation, input, placement, network refresh, Nullifier, markers, telemetry | Monolith; every rule lives here |
| `Cell.cs` | Per-cell data: Desync, NextDesync, Stabilization, NetDelta, Structure, Elevation, ClampedExcess | POCO |
| `Emitter.cs` | Position, MaxIntegrity, Integrity, `IsNullified` | POCO |
| `ContainmentNode.cs` | Stabilizer simulation data and `ContainmentCondition` enum | POCO; condition is telemetry only |
| `PlacedStructure.cs` | Player structure: type, position, IsConnected, Node, Marker | POCO + SpriteRenderer ref |
| `InfrastructureNetwork.cs` | BFS connectivity from Cores | **Static, pure, deterministic. The easiest code to test.** |
| `TerrainGenerator.cs` | Seeded multi-octave Perlin heightmap, normalized 0..1 | **Static, pure, deterministic** (`System.Random`) |
| `CellVisual.cs` | Per-cell color: terrain → Desync (Current/Bands) → cyan edge → stabilization | One GameObject per cell |
| `HUDController.cs` | UI Toolkit labels via `PanelRenderer` reload callback | Hides 13 retired labels at runtime |
| `DebugStats.cs` | Telemetry struct passed to the HUD each frame | |
| `BuildTool.cs` / `StructureType.cs` | Enums. Index order is preserved after the Neutralizer → Nullifier rename. | |

### 2.2 Desync simulation (implemented)

The `Simulate()` tick runs on **interior cells only** (1..W-2, 1..H-2). For each cell:

1. **Terrain-weighted neighbor average** (4-neighbor): `w = max(0, 1 + terrainInfluence·10·(elev_neighbor − elev_self))`. Desync flows downhill. This is the **Arcade terrain model**. When `terrainInfluence = 0` or the terrain is flat, it reduces to a plain average.
2. `next = lerp(current, average, spreadStrength)`
3. **Growth:** `+growthRate` if `current > contaminationThreshold`
4. **Local decay:** `−(decayRate + 0.01 · Σ Stabilization in the 5×5 neighborhood)`
5. **Pressure correction:** `+0.05 · (average − current)`
6. Clamp to `[0, maxDesync]`. Clamp overshoot is recorded as telemetry only.
7. Double-buffered apply (`NextDesync` → `Desync`), then **active Emitters are re-pinned** to `sourceStrength`.

The tick contains no `Time.deltaTime`, so **each tick is deterministic** for a given state. This is a strong foundation for characterization tests.

### 2.3 Terrain

`TerrainGenerator` (Phase 1) builds the heightmap once at Start. `Simulate()` uses it (Phase 2 experiment, `terrainInfluence`). The `flatTerrainBaseline` toggle gives an A/B baseline. The Physical terrain/fluid model is **not present**, which matches its *deferred* status.

### 2.4 Structures and connectivity

| Structure | Implemented behavior |
|---|---|
| **Emitter** | Level source with 500 integrity. Pins its cell to `sourceStrength` each tick while integrity > 0. Can be placed in the sandbox (tool 1 or Shift+LMB, gated by `sandboxDebugControls`). **Has no visual marker.** |
| **Core** | Network root, always connected. Limited to **one per map**. Relays. |
| **Stabilizer** | Owns a `ContainmentNode` (radius/strength with linear falloff). Applies stabilization **only when connected**. **Relays the network.** |
| **Nullifier** | Must be connected. Drains the **nearest active Emitter within 6 cells** by 1 per tick. **Does not relay.** Several Nullifiers **stack**. |

**Connectivity:** distance-only BFS from Cores, with `connectionRange` = 8 cells and an inclusive boundary. It is independent of list order. It is recomputed on placement and removal, not every tick.

**Objective:** `PrimaryObjectiveComplete` becomes true when every Emitter is nullified. Today its only effect is a `Debug.Log`.

### 2.5 Containment conditions (experiment B)

Each connected Stabilizer gets a condition based on what share of its footprint is contaminated:

- **Warning** at 5% or more
- **Critical** at 25% or more
- **Breached** at 50% or more, or if its center cell is contaminated

The marker color changes with the condition, and Critical/Breached markers pulse in scale. The HUD's *Containment Status* shows the worst condition across the network. A legacy integrity-based status is available behind `networkContainmentStatus = false`. The conditions are **telemetry only and never feed back into the simulation.**

### 2.6 HUD and debug controls

- **HUD:** one UI Toolkit panel built from `NewUXMLTemplate.uxml`. It shows Reality Integrity, containment status, anomalies with Emitter integrity, nodes, total and peak Desync with clamp telemetry, contaminated/frontier counts, activity breakdown, tool/brush, simulation state/tick, and hovered-cell data. It is a single engineering HUD. There is **no player/debug split** yet.
- **Controls** (`Scripts/CONTROLS.md` is accurate against the code):
  - Space: pause
  - `.`: step one tick while paused
  - 1–4: tools; Esc: no tool
  - LMB: place the selected structure, or paint Desync when no tool is selected (sandbox)
  - Shift+LMB / Shift+RMB: add or remove an Emitter (sandbox)
  - RMB hold: erase Desync (sandbox)
  - MMB: place a Stabilizer; Shift+MMB: remove the nearest structure
  - Scroll: brush radius 1–25
- **Placement ghost:** green = valid and operational, yellow = valid but inactive, red = invalid

### 2.7 How to run the Sandbox today

1. In Unity Hub, add the project folder and open it with **6000.5.4f1**. The first open re-imports everything because Library is missing.
2. Open `Assets/_CoreFiles/Scenes/Sandbox.unity` and press Play.
3. The scene starts with **no Emitter** (`startWithCenterSource = 0`) and the **Stabilizer** tool selected. Typical loop:
   - `1` + click to place an Emitter
   - `4` to place a Core
   - `2` to chain Stabilizers toward the Emitter
   - `3` to place a Nullifier within 6 cells of the Emitter
4. There are **no automated tests** to run.

---

## 3. Discrepancies

### 3.1 Script defaults vs. Sandbox scene values (the scene is the source of truth for behavior)

| Field | Script default | **Sandbox.unity** | Impact |
|---|---|---|---|
| `decayRate` | 0.05 | **0** | No natural decay. Contamination only recedes under connected Stabilizers. |
| `growthRate` | 0.02 | **0.1** | 5× faster self-growth of contaminated cells |
| `tickRate` | 0.05 | **0.1** | Half the tick frequency |
| `startWithCenterSource` | true | **false** | Starts empty |
| `nodeRadius` / `nodeStrength` | 10 / 5 | **5 / 10** | Smaller, stronger Stabilizers |
| `landformSize` / `surfaceDetailLayers` | 25 / 4 | **50 / 1** | Broad, smooth terrain |
| `terrainInfluence` | 0.1 | **0.5** | 5× stronger downhill bias |
| `currentTool` | Stabilizer | Stabilizer (2) | — |
| CellVisual `desyncDisplayMode` | Current | **Bands** (prefab) | The Bands experiment is the live visual |

**Risk:** a GridManager added to any new scene (for example a Mission 1 scene) inherits the *script* defaults and behaves very differently from the Sandbox everyone has been testing. The tuned baseline exists **only** in `Sandbox.unity`.

### 3.2 Code vs. GDD v1.0 (§21 work plan)

| GDD item | Status in code |
|---|---|
| Separate level Emitter from free Source | **Partial.** The `Emitter` class exists. Source placement remains but is gated by `sandboxDebugControls`. |
| Finish BuildTool path / retire legacy mouse | **Partial.** BuildTool works. Paint/erase/Emitter shortcuts are sandbox-gated, but MMB place/remove is **not** gated. |
| True infrastructure/relay structure | **Not implemented.** There is no Relay type. Core and Stabilizers act as relays. |
| Simple connectivity and operational status | **Implemented** (distance BFS) |
| Separate *Containment Coverage* from Stabilization (§10) | **Not implemented.** Note the **name collision**: `ContainmentNode.Coverage` means *contaminated share of footprint*, not the GDD's *infrastructure reach*. |
| Stabilizers require operational infrastructure | **Implemented** |
| Nullifier as a real structure | **Implemented** |
| Emitter integrity and nullification | **Implemented** (500 integrity, 1 per tick per Nullifier) |
| Mission 1 map data | **Not implemented.** `The Basin.unity` is empty (camera only) and not in the build. |
| Tutorial prompts | Not implemented |
| Mission success state (§15.3 Claim Victory / Continue) | **Hook only** (`PrimaryObjectiveComplete` + log; no UI) |
| Mission failure state | **Not implemented.** Reality Integrity has no threshold. |
| Player HUD vs. Engineering HUD | **Not implemented.** One HUD, with retired labels hidden. |
| §8 Emitter "highly recognizable marker" | **Missing.** Emitters have no marker. In Bands mode their cell shows white, which looks the same as any other cell at the cap. A nullified Emitter does not look different. |
| §8 visible network connections | Missing (marker color only, as CONTROLS.md notes) |
| §17 Sandbox: Emitter strength, simulation speed | Inspector only. There are no in-game controls. |

### 3.3 The GDD trails the code

These features were built **after** GDD v1.0 (2026-09-27) and are not documented in it:

- Arcade terrain generation and propagation (2026-09-30)
- Containment conditions (experiment B)
- Clamp telemetry (A)
- Activity "binoculars" (D)
- Bands display (Experiment 1)

**GDD Amendment A was not found** in the project or the Desktop folder. It probably covers terrain. Please supply it.

Some GDD §26 open questions are **answered de facto by the code** and should be confirmed or labeled *experimental*:

- **Q1:** connection is distance-only, 8 cells
- **Q3:** Stabilizers relay; there is no dedicated Relay
- **Q4:** the Nullifier drains continuously at no cost, nearest target only, and several Nullifiers stack

### 3.4 Documentation vs. code

- `Assets/CONTROLS.md` is **stale**. It describes "Neutralizer", says the tool keys "have no gameplay effect", and says sources cannot be removed. `Assets/_CoreFiles/Scripts/CONTROLS.md` is current. Two files with the same name could mislead future readers.
- The `TerrainGenerator` class summary says *"no simulation system reads Elevation"*. That is stale since Phase 2.
- `CellVisual` hard-codes copies of the threshold (5) and cap (250). They currently match the scene. It uses `>=` where GridManager uses `>`, so a cell at exactly 5.0 is drawn as contaminated but not counted as contaminated.

---

## 4. Behavior notes and open design/test decisions (not bugs to fix without approval)

1. **The map boundary is an absorbing sink.** Border cells are never simulated, and their `NextDesync` is always 0, so every tick resets the border to 0. Desync painted on the border vanishes on the next tick. CLAUDE.md lists this as an *open decision*.
2. **Post-Emitter recovery depends on scene tuning.** With the scene's `decayRate = 0` and `growthRate = 0.1`, contamination keeps sustaining itself after the Emitter is nullified. It recedes only under connected Stabilizers. This bears directly on GDD §15.2 ("allow Desync to decay") and on "natural recovery is desired in principle".
3. **Tick rate depends on frame rate.** At most one tick runs per frame, and leftover time is discarded (`timer = 0f`). Real ticks per second therefore vary with frame rate: roughly 8.5–10 per second at `tickRate` 0.1 instead of 10. Per-tick results are unaffected.
4. **Reality Integrity has a floor.** The 396 border cells (always 0) count in the denominator, so the floor is about 3.96%.
5. **Removing the Core** (Shift+MMB, not sandbox-gated) disconnects the whole network at once.

---

## 5. Technical debt

| # | Item | Severity |
|---|---|---|
| 1 | **Monolithic `GridManager`**: rules, input, rendering and telemetry are interleaved, and `Simulate()` is private. It cannot be tested without extraction. This is the main obstacle to platform independence. | High (architectural) |
| 2 | **No version control, no tests, no asmdefs.** There is no rollback path today. | High (process) |
| 3 | **Input is hard-wired** (`Keyboard.current` with no null check in `Update`, so it would throw `NullReferenceException` on a device without a keyboard). The Input Actions asset is unused. | Medium |
| 4 | **Rendering cost:** 10,000 GameObjects; all 10k `SetCell` calls run each tick; `GetDebugStats()` scans the full grid **every frame**, not every tick. Fine on PC, a concern for mobile. | Medium (perf) |
| 5 | The **tuned baseline lives only in scene YAML**, and script defaults diverge (see §3.1) | Medium |
| 6 | **Dead or broken telemetry:** `totalDesync`/`previousTotalDesync`/`NetDeltaPerSecond` are never accumulated (always 0); `placementMode` is unused (CS0414); `simulationTime` counts while paused; `TotalDecayApplied` sums nominal decay over empty cells. These are already hidden in the HUD. | Low |
| 7 | **Dead assets/code:** `StabilizerNode.prefab` is unreferenced; a commented-out `PaintStabilization` sits after the class's closing brace | Low |
| 8 | Duplicated constants in `CellVisual` (see §3.4) | Low |
| 9 | Template identity (`DefaultCompany`, `com.DefaultCompany.2D-URP`) | Low (pre-build) |
| 10 | The HUD UXML is still named `NewUXMLTemplate.uxml` and the scene object is called `UIDocument` although it is a `PanelRenderer` | Cosmetic |

### Possible architectural boundaries (for later, with approval)

```
SimulationCore (pure C#, no UnityEngine.Object)
  DesyncField · Stabilization · TerrainBias · EmitterSet · Network · ConditionEvaluator
  └─ Step(state, commands) → deterministic
Commands: PlaceStructure / RemoveStructure / PaintDesync(sandbox) / AddEmitter(sandbox)
Telemetry: read-only snapshot (today's DebugStats)
Presentation: GridRenderer (CellVisual or a texture), markers, HUD
Input adapter: Keyboard/Mouse → Commands (later Input Actions / touch / gamepad)
```

`InfrastructureNetwork` and `TerrainGenerator` already fit this shape.

---

## 6. Local Git state and safe baseline proposal (not performed)

**Current state:** there is no `.git` in the project or its parent folder, no `.gitignore`, and no `.gitattributes`. Serialization is Force Text with Visible Meta Files, so the project is suitable for Git.

**Proposal (local only, no remote):**

1. **Before Unity is opened on the rescue copy**, run `git init` in the project root. The first open will re-import and may rewrite scenes or ProjectSettings; a baseline taken first makes every such change visible as a diff.
2. Add a Unity `.gitignore` that excludes:
   - `Library/`, `Temp/`, `Obj/`, `Build*/`, `Logs/`, `UserSettings/`, `MemoryCaptures/`, `Recordings/`
   - `*.csproj`, `*.sln`, `*.slnx`, `.vs/`, `.idea/`
   - crash dumps, `*.apk`, `*.aab`, `*.keystore`, `.env*`
3. Add a minimal `.gitattributes`. Mark `*.png` as binary. Do **not** enable EOL normalization in the baseline commit, so no file content changes.
4. Commit everything else as *"Sandbox baseline (rescue copy, pre-first-open)"* and tag it `baseline/sandbox-2026-10`.
5. Verify:
   - `git status` is clean
   - no ignored folders are tracked
   - every asset has its `.meta`
   - `git remote -v` is empty

---

## 7. Recommended first engineering tasks (small, behavior-preserving)

### Task 1: Local Git baseline (no remote)

- **What:** steps 1–5 of §6.
- **Files:** new `.gitignore` and `.gitattributes` at the project root. Nothing under `Assets/` changes.
- **Risks:**
  - Line-ending rewrites. Mitigation: no EOL normalization in the first commit; confirm the diff is empty.
  - An over-broad ignore could drop needed files. Mitigation: review `git ls-files` before committing.
- **Acceptance:**
  - Clean `git status`
  - `.meta` count equals asset count
  - No Library, Logs or UserSettings tracked
  - Tag exists
  - No remote
- **Rollback:** delete `.git/` and the two files.

### Task 2: Test scaffold and characterization tests for the pure classes

- **What:**
  - Add `ImminentDesync.Runtime.asmdef` in `Scripts/`, referencing `Unity.InputSystem`.
  - Add `Assets/_CoreFiles/Tests/EditMode/ImminentDesync.Tests.EditMode.asmdef`.
  - Write NUnit tests that need **no production code changes**:
    - `InfrastructureNetwork`: Core is always connected; Stabilizer chains relay; Nullifier does not relay; inclusive range boundary; result is the same for a shuffled list; `WouldConnect` matches `Recalculate`.
    - `TerrainGenerator`: same seed gives identical output; the scene's settings (seed 12345, landform 50, 1 layer) produce a recorded golden checksum; flat terrain gives all zeros; output is normalized to 0..1.
    - `Emitter.IsNullified` boundary.
- **Why an asmdef:** test assemblies cannot reference `Assembly-CSharp`. Scene references use script GUIDs, so scenes should keep working, but this needs Editor verification.
- **Risks:**
  - The asmdef can expose a missing assembly reference (Input System is the known one).
  - It requires opening Unity, which needs your approval.
  - `Simulate()` is **out of scope**. Characterizing it needs extraction, which is the natural Task 4.
- **Acceptance:**
  - All EditMode tests pass in Test Runner
  - Sandbox loads with no missing scripts
  - Smoke checklist unchanged
  - No `.cs` files under `Scripts/` modified
- **Rollback:** revert the commit (requires Task 1).

### Task 3: Sandbox Baseline Record (documentation only)

- **What:**
  - Add `Docs/SANDBOX_BASELINE.md` at the project root. It sits outside `Assets/`, so Unity does not import it.
  - Record the scene-serialized values against script defaults (§3.1), the CellVisual prefab values, how to run the Sandbox, and 3–5 **reproducible manual scenarios** with expected observations:
    - Emitter at a fixed cell with no structures, observed for N ticks
    - Core + 3 Stabilizers + Nullifier at fixed coordinates
    - Flat-terrain A/B comparison
  - Andrew fills in the observed results on the first run.
  - Propose (not perform) retiring the stale `Assets/CONTROLS.md`.
  - Move `CLAUDE.md` into the project root so future sessions load it automatically.
- **Risks:** essentially none. The docs could drift, so they should be re-checked whenever scene values change.
- **Acceptance:**
  - Reviewed by Andrew and DFS Jr.
  - Every value matches `Sandbox.unity`
  - Scenarios can be reproduced by someone new to the project

**Natural follow-on (needs separate approval):** extract a pure `DesyncSimulation` step from `GridManager` behind characterization (golden-master) tests. Run N ticks from a scripted setup and compare checksums before and after. This is the foundation for platform independence and for any later decay or boundary tuning.

---

## 8. Decisions that need the Game Director

1. **GDD Amendment A:** where is it? Should GDD v1.0 Final be treated as current until then?
2. **Approve Task 1** (local Git baseline), ideally **before** Unity is first opened on the rescue copy.
3. **Approve the asmdef introduction** (Task 2) and approve opening Unity to run tests, or run them yourself and report back.
4. **Canonical tuning:** is the Sandbox scene's `decayRate = 0` / `growthRate = 0.1` the intended baseline? It currently prevents natural post-Emitter recovery.
5. **Map boundary:** keep the absorbing sink, or treat it as an open test item?
6. **Confirm or label as experimental** the de facto rules: Stabilizers relay; distance-only connection (8 cells); Nullifiers stack and drain the nearest Emitter at no cost; one Core per map.
7. **Mission 1 home:** is `The Basin.unity` the intended Mission 1 scene?
8. Should `CLAUDE.md` live in the project root?

---

*End of report. No implementation has been performed. Awaiting Director approval.*
