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
- Current code/scene files in the local Unity project are the source of truth for actual behavior. The GDD is the source of truth for design intent; report mismatches rather than silently changing either.

## First-read sources
1. The local project's latest GDD, ideally `Imminent Desync GDD v1.0 + Amendment A.md` if present.
2. `Assets/_CoreFiles/Scripts/GridManager.cs` and its related scripts: `Cell.cs`, `CellVisual.cs`, `InfrastructureNetwork.cs`, `Emitter.cs`, `ContainmentNode.cs`, `PlacedStructure.cs`, `TerrainGenerator.cs`, `HUDController.cs`, and any actual local equivalents.
3. `CONTROLS.md` and Unity scenes / ProjectSettings, where present.
4. Inspect for existing Git history, assembly definitions, tests, documentation and project-specific Claude instructions before proposing setup changes.
- Do not assume archived script bundles or prior reports exactly match the current local files. Record actual filenames, versions and discrepancies.

## Engineering discipline
- First session: **read-only discovery**. Do not write files, run Unity, install packages, initialize Git, connect a remote, or change settings without explicit approval.
- Before any implementation: propose a narrowly scoped plan, anticipated files, tests, risks, and rollback path; await approval.
- Maintain behavior parity when extracting or optimizing the simulation. Record characterization tests first, then distinguish intentional rule changes from refactors.
- Keep simulation rules independent of input devices, frame rendering, UI, and platform services. Prefer validated commands and read-only presentation state when architectural work is authorized.
- Do not replace the current visuals, Bands experiments, activity/telemetry or debug markers without Director review.
- Avoid arbitrary new mechanics (fixed mobile energy budget, stars, instant Core failure, structure shutdown, new units) unless approved.
- Treat decay and map-boundary behavior as **open design/test decisions**. Natural recovery is desired in principle; no specific decay rate or boundary correction has been approved. Do not remove `decayRate`.
- Don't implement the Physical model, a mobile spin-off, or console/mobile builds just because cross-platform potential has been discussed. PC-first testing; platform independence is the architectural goal, not a platform commitment.
- Report test results honestly: specify what was run, what was not, and what needs the Unity Editor or physical device.

## Local-only project and privacy
- Project is currently intended to be worked on using **local sessions**, not remote/cloud development environments. Do not initiate remote sessions, create GitHub repositories, push code, upload archives, enable sync, or publish builds without explicit Director approval.
- Local Claude Code sessions still communicate prompts and relevant code context to Anthropic's service; do not describe this as offline or air-gapped.
- Never commit/upload secrets, credentials, signing keys, `.env` files, generated Unity folders, or personal files. If setting up Git is authorized, inspect `.gitignore` and status before committing.
- Never touch other Unity projects (including Shrouded Mind or Pilot 0) without explicit instructions.

## Current first assignment
Produce a read-only onboarding report:
1. Actual project structure, Unity version, scenes, build targets and packages.
2. Systems inventory and key dependencies.
3. How to run the existing Sandbox and what tests, if any, are available.
4. Discrepancies between code, scene defaults and documented design.
5. Local Git state and safe baseline proposal (without making one).
6. Top 3 *small, behavior-preserving* first engineering tasks, with risks and acceptance criteria.
7. Any decisions that need the Game Director.
Stop after the report. Do not implement anything.
