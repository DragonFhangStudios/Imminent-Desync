# Imminent Desync — Prototype Controls Index

Current controls, based on the implementation in `GridManager.cs`.
Developer reference, not the player-facing Controls/Help UI.

Controls marked **(sandbox)** only work while **Sandbox Debug Controls** is enabled on the GridManager (default: on).

---

## Simulation Controls

| Input | Action |
|---|---|
| **Space** | Pause / resume the simulation. |
| **. (Period)** | While paused: advance exactly one tick. |

## Build Tool

| Input | Action |
|---|---|
| **1** | Select **Source** tool — places an Emitter **(sandbox)**. |
| **2** | Select **Stabilizer** tool. |
| **3** | Select **Nullifier** tool. |
| **4** | Select **Core** tool (one Operational Core per map). |
| **Esc** | Deselect tool (returns Left Click to the Desync brush). |
| **Left Click** | Place the selected structure at the cursor. |

**Placement ghost** (follows the cursor while a tool is selected):

| Ghost | Meaning |
|---|---|
| 🟩 Green | Valid, and the structure will be operational. |
| 🟨 Yellow | Valid, but the structure will be **inactive** (out of network reach; for a Nullifier, also no Emitter in range). |
| 🟥 Red | Invalid (cell occupied, second Core, or tool unavailable). |

**Structure markers:** Core = white · Nullifier = violet (draining an Emitter) / gray (inactive).

**Stabilizer condition** (marker color): light green = Normal · amber = Warning · orange = Critical (slow pulse) · red = Breached (faster pulse) · gray = inactive (not connected).
Condition = share of the Stabilizer's footprint that is contaminated (thresholds tunable under *Containment Condition* on the GridManager).

## Mouse Controls

| Input | Action |
|---|---|
| **Left Click** (no tool) | Paint Desync (250) in Brush Radius **(sandbox)**. |
| **Shift + Left Click** | Place a permanent **Emitter** on the cell **(sandbox)**. |
| **Right Click (hold)** | Erase Desync in Brush Radius **(sandbox)**. Does not remove Emitters. |
| **Shift + Right Click** | Remove the nearest **Emitter** within 5 cells **(sandbox)**. |
| **Middle Click** | Place a **Stabilizer** (same rules as the build tool). |
| **Shift + Middle Click** | Remove the nearest **player structure** (Core, Stabilizer, Nullifier) within 5 cells. |
| **Hover** | HUD shows the hovered cell's coordinates, Desync, Stabilization, Net Delta, and Elevation. |

## Brush Controls

| Input | Action |
|---|---|
| **Scroll Up / Down** | Increase / decrease Brush Radius (1–25, default 3). |

The brush only affects Desync paint (Left Click with no tool) and erase (Right Click hold).

---

## Network Rules (current prototype)

- The **Core** is always connected.
- A structure connects if it is within **Connection Range** (default 8 cells) of a connected **Core or Stabilizer**.
- **Stabilizers** relay the connection onward; **Nullifiers** connect but do not relay.
- Unconnected Stabilizers apply **no** stabilization. Unconnected Nullifiers do nothing.
- A connected Nullifier drains the nearest active Emitter within **Nullifier Range** (default 6 cells). At 0 integrity the Emitter stops sustaining Desync.

## Known Prototype Quirks

- **Edits while paused don't redraw the grid until the next tick.** Press **.** to step once. (Markers and the ghost update immediately.)
- **Current Tool is saved in the scene.** If it's set to Stabilizer, Left Click places Stabilizers; press **Esc** to get the Desync brush back.
- There are no connection lines yet; connectivity is shown by marker color only.
