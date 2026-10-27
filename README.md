# Imminent Desync

A real-time containment-infrastructure strategy game built around a living Desync simulation.
DragonFhang Studios (DFS). Public repository; development is managed by the DFS team through reviewed pull requests.

- **Game Director:** Andrew (final decision-maker on design, merges and releases)
- **Design / architecture collaborator:** DFS Jr.
- **Implementation / engineering partner:** Claude Code

> Before making any change, read [`CLAUDE.md`](CLAUDE.md). It defines authority, engineering discipline, and what is implemented, documented, proposed and experimental.

## Current state

The project is a **playable prototype Sandbox**, not yet a mission. Preserve the Sandbox's controls, visuals, current behavior and reproducible test scenarios unless the Director approves a change.

| Item | Value |
|---|---|
| Unity | **6000.5.4f1** (Unity 6.5). Do not upgrade without Director approval. |
| Render pipeline | URP 2D (17.5.0) |
| Input | Input System 1.19.0 |
| Playable scene | `Assets/_CoreFiles/Scenes/Sandbox.unity` (the only scene in Build Settings) |
| Tests | None yet. `com.unity.test-framework` is installed. |
| Platform | PC-first testing. No other platform is authorized. |

## Opening the project

1. Install Unity **6000.5.4f1** through Unity Hub.
2. Add this folder in Unity Hub and open it. The first open on a fresh clone re-imports everything (Library is not versioned) and takes several minutes.
3. Open `Assets/_CoreFiles/Scenes/Sandbox.unity` and press Play.

The Sandbox starts with no Emitter and the Stabilizer tool selected. A typical loop:

1. `1` + click to place an Emitter
2. `4` + click to place the Core
3. `2` to chain Stabilizers toward the Emitter
4. `3` to place a Nullifier within 6 cells of the Emitter

All controls are documented in [`Assets/_CoreFiles/Scripts/CONTROLS.md`](Assets/_CoreFiles/Scripts/CONTROLS.md). `Assets/CONTROLS.md` is an older, stale copy kept untouched in the baseline.

## Repository layout

```
Assets/_CoreFiles/
  Scenes/       Sandbox.unity (playable), The Basin.unity (empty placeholder)
  Scripts/      Simulation, network, structures, HUD, visuals
  Prefabs/      CellVisual (used), StabilizerNode (unused)
  UnityToolkit/ HUD layout (UXML)
Packages/       Package manifest and lock file
ProjectSettings/
Docs/           Design and engineering documents
CLAUDE.md       Project instructions for Claude Code
```

## Documents

| Document | Purpose |
|---|---|
| [`Docs/Imminent_Desync_GDD_v1.0_Final.docx`](Docs/Imminent_Desync_GDD_v1.0_Final.docx) | Game Design Document v1.0 (design intent). Amendment A is not yet in the repository. |
| [`Docs/ENGINEERING_ONBOARDING_REPORT_2026-10-27.md`](Docs/ENGINEERING_ONBOARDING_REPORT_2026-10-27.md) | Systems inventory, code-vs-GDD discrepancies, technical debt and open decisions as of 2026-10-27 |
| [`Assets/_CoreFiles/Scripts/CONTROLS.md`](Assets/_CoreFiles/Scripts/CONTROLS.md) | Current Sandbox controls |

The code and scene files are the source of truth for **actual behavior**. The GDD is the source of truth for **design intent**. Mismatches are reported, not silently fixed.

## Version control notes

- `.gitignore` excludes Unity-generated folders (`Library/`, `Temp/`, `Logs/`, `UserSettings/`, `UIElementsSchema/`), IDE solution files, builds, and signing/secret files.
- `.gitattributes` stores text with LF line endings and keeps Unity YAML (scenes, prefabs, assets, `.meta`) as LF on every platform. Images, documents and other binaries are marked binary.
- Git LFS is not used yet. The repository is about 5 MB. Revisit LFS before adding large art, audio or model files.
- Every asset under `Assets/` must be committed together with its `.meta` file.
