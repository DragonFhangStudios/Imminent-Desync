using UnityEngine;

public class Cell
{
    public float Desync;
    public float NextDesync;
	public float Stabilization;
	public float NetDelta;
	public StructureType Structure;

	// Terrain elevation, normalized 0..1. Rendered by CellVisual and used by
	// GridManager.Simulate() for terrain-weighted Desync propagation.
	public float Elevation;

	// Telemetry only (never read by the simulation): how much this cell's
	// last update overshot Max Desync before the clamp discarded it.
	public float ClampedExcess;

	public Cell()
    {
        Desync = 0f;
        NextDesync = 0f;
		Structure = StructureType.None;
		Elevation = 0f;
	}
}
