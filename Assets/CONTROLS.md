# Imminent Desync — Prototype Controls Index

Current sandbox controls, based on the implementation in `GridManager.cs`.
This is a developer reference, not the player-facing Controls/Help UI.

---

## Simulation Controls

| Input | Action |
|---|---|
| **Space** | Pause / resume the simulation. |
| **. (Period)** | While paused: advance the simulation by exactly one tick. |

The simulation runs one tick every `tickRate` seconds (default 0.05s) while unpaused.

## Mouse Controls

| Input | Action |
|---|---|
| **Left Click** | Paint Desync at full source strength (250) in a circle of Brush Radius. One-time injection: it spreads and decays normally afterward. |
| **Shift + Left Click** | Place a **permanent Desync source** on the clicked cell (re-set to 250 every tick). Ignored if a source already exists there. Brush Radius does not apply. |
| **Right Click (hold)** | Erase Desync (set to 0) in a circle of Brush Radius. Continuous while held; drag to erase an area. Does **not** remove permanent sources. |
| **Middle Click** | Place a **Stabilizer / containment node** on the clicked cell (radius and strength from the Inspector: default 10 / 5). |
| **Shift + Middle Click** | Remove the nearest Stabilizer within 5 cells of the click. |
| **Hover** | HUD shows the hovered cell's coordinates, Desync, Stabilization, and Net Delta. |

Either Shift key works as the modifier. Clicks outside the grid are ignored.

## Brush Controls

| Input | Action |
|---|---|
| **Scroll Up** | Increase Brush Radius. |
| **Scroll Down** | Decrease Brush Radius. |

- Range: **1–25** cells (default 3). Current value is shown in the HUD.
- The brush only affects **Left Click (paint)** and **Right Click (erase)**. Sources and Stabilizers are always single-cell placements.
- Radius changes per frame of scroll input, so one wheel notch may change it by more than 1 on some mice.

## Tool Selection

| Input | Action |
|---|---|
| **1** | Select Source tool. |
| **2** | Select Stabilizer tool. |
| **3** | Select Neutralizer tool. |

> ⚠️ **Currently has no gameplay effect.** These keys change the selected tool, but no click handler uses it yet (`PlaceStructure()` exists but is never called). All placement is done with the mouse controls above. The Neutralizer has no behavior implemented.

---

## Known Prototype Quirks

- **No way to remove a permanent source** in-game. Right Click erases Desync around it, but the source re-sets to 250 on the next tick. Restart Play mode to clear sources.
- **Edits while paused don't redraw until the next tick.** Painting, erasing, or placing works while paused, but the grid only re-renders on a tick. Press **.** to step once and see the change.
- **Stabilizers can stack.** Middle-clicking the same cell repeatedly places multiple nodes there.
