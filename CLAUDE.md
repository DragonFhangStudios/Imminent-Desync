# Imminent Desync — Claude Code Project Instructions

## Team and authority
- Andrew is Game Director and final decision-maker. Do not approve design changes, broad refactors, releases, or merges on his behalf.
- ChatGPT / DFS Jr. collaborates on game design, architecture, and trade-offs. Claude Code is the implementation/engineering partner. Offer alternatives and await Director decisions.
- Do not represent a proposed mechanic as approved. Distinguish **implemented**, **documented**, **proposed**, and **experimental**.

## Project identity
- Unity 6, C#, grid-based real-time containment/infrastructure strategy game.
- Pillars: **The Simulation Is the Toy**; **Infrastructure Is Reach**; **Containment Before Destruction**; **Readable Spatial State**; **Teach Through Missions**; **Build the Smallest Fun Game First**.
- Loop: detect breach → establish foothold → connect infrastructure → contain frontier → extend reach → deploy Nullifier → neutralize Emitter → recover/secure.
- Existing Arcade terrain biases propagation by elevation. The Physical terrain/fluid model is **deferred**, not approved.
- Sandbox is a valuable experimental testbed. Preserve its controls, visuals, current behavior and reproducible test scenarios unless a change is explicitly approved.
- Current code/scene files are the source of truth for actual behavior. The GDD is the source of truth for design intent; report mismatches rather than silently changing either.

## Project location and source of truth
- `main` on GitHub (`DragonFhangStudios/Imminent-Desync`) is the shared canonical history.
- Andrew's authoritative local working copy is the rescued copy of the project. An older original copy also exists as an archive: never open, edit, or sync it. The exact local paths are kept in the gitignored `CLAUDE.local.md` on Andrew's machine, not in this public file.
- Version history starts at the baseline commit tagged `baseline/sandbox-2026-10`. Never rewrite, delete, or move that commit or tag.

## First-read sources
1. `Docs/Imminent_Desync_GDD_v1.0_Final.docx` (GDD v1.0). Amendment A is referenced but not yet in the repository; ask for it rather than assuming its contents.
2. `Assets/_CoreFiles/Scripts/GridManager.cs` and its related scripts: `Cell.cs`, `CellVisual.cs`, `InfrastructureNetwork.cs`, `Emitter.cs`, `ContainmentNode.cs`, `PlacedStructure.cs`, `TerrainGenerator.cs`, `HUDController.cs`.
3. `Assets/_CoreFiles/Scripts/CONTROLS.md` (current controls; `Assets/CONTROLS.md` is a stale older copy), `Assets/_CoreFiles/Scenes/Sandbox.unity`, and ProjectSettings.
4. `Docs/ENGINEERING_ONBOARDING_REPORT_2026-10-27.md` for the systems inventory, code-vs-GDD discrepancies, technical debt and open decisions.
- The Sandbox's tuned values live in `Sandbox.unity` and differ from the script defaults. Read the scene, not only the code, before reasoning about behavior.
- Do not assume archived script bundles or prior reports exactly match the current files. Record actual filenames, versions and discrepancies.

## Engineering discipline
- Before any implementation: propose a narrowly scoped plan, anticipated files, tests, risks, and rollback path; await approval.
- Do not run or open Unity, install or upgrade packages, change the Unity version, or change project settings without explicit approval.
- Maintain behavior parity when extracting or optimizing the simulation. Record characterization tests first, then distinguish intentional rule changes from refactors.
- Keep simulation rules independent of input devices, frame rendering, UI, and platform services. Prefer validated commands and read-only presentation state when architectural work is authorized.
- Do not replace the current visuals, Bands experiments, activity/telemetry or debug markers without Director review.
- Avoid arbitrary new mechanics (fixed mobile energy budget, stars, instant Core failure, structure shutdown, new units) unless approved.
- Treat decay and map-boundary behavior as **open design/test decisions**. Natural recovery is desired in principle; no specific decay rate or boundary correction has been approved. Do not remove `decayRate`.
- Don't implement the Physical model, a mobile spin-off, or console/mobile builds just because cross-platform potential has been discussed. PC-first testing; platform independence is the architectural goal, not a platform commitment.
- Report test results honestly: specify what was run, what was not, and what needs the Unity Editor or physical device.

## Repository and cloud development
- GitHub repository: `DragonFhangStudios/Imminent-Desync`, default branch `main`. The repository is **public** by Game Director decision. Do not change its visibility, settings, or default branch without Andrew's explicit approval.
- Claude Code may work in local sessions and in Claude Code cloud sessions against this repository, and may push its own scoped branches for authorized tasks.
- Workflow:
  - `main` is the stable branch. Never push directly to `main` or force-push any shared branch.
  - Work on scoped branches: `feature/…`, `fix/…`, `test/…`, `docs/…`.
  - Open a pull request for every change. Each PR states its scope, files changed, whether behavior changes (none, or intentional and approved), what tests ran and where, risks, rollback, and decisions needed from the Director.
  - DFS Jr. reviews design and architecture. Andrew approves and merges. Never merge on his behalf and never enable automatic merging.
- Cloud sessions cannot run the Unity Editor. State plainly which checks need Unity on Andrew's PC, and do not claim Editor verification that did not happen.
- Do not publish builds, create releases, or deploy without explicit Director approval.
- Git LFS is not configured. Do not add it, or commit large binaries, without approval.
- Unity hygiene: commit every asset together with its `.meta`; never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/` or IDE solution files; keep scene edits in their own PRs. When the Editor rewrites files on first open or import, review and commit those changes separately from feature work.
- Claude Code sessions, local or cloud, send prompts and relevant code to Anthropic's service; do not describe them as offline or air-gapped.
- Never commit or upload secrets, credentials, signing keys, `.env` files, generated Unity folders, or personal files. Check `git status` and the staged file list before every commit.
- Never touch other Unity projects (including Shrouded Mind or Pilot 0) without explicit instructions.

## Pre-push publication scan (required, the repository is public)
Before every push, scan everything the push would publish (new commits' files and messages) for:
1. **Secrets:** credentials, API keys, tokens, passwords, signing keys/keystores, certificates, `.env` files.
2. **Personal information:** email addresses, phone numbers, home addresses, account names, or personal details beyond the team roles already named in this file.
3. **Machine-specific information:** local drive paths, user-profile folders, machine names. These belong in the gitignored `CLAUDE.local.md`, not in tracked files.
4. **Third-party assets without redistribution rights:** Asset Store or purchased art, audio, models, fonts, or other copyrighted material. Unity's own template/default assets and package references are fine.
5. **Generated or oversized files:** anything `.gitignore` should exclude, or binaries large enough to need Git LFS.
- State the scan result in the pull request. If anything is found, do not push: stop and report it to Andrew.
