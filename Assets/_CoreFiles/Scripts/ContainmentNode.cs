// ContainmentNode.cs

using UnityEngine;

// Experimental per-node containment condition (telemetry/feedback only).
public enum ContainmentCondition
{
	Inactive,   // not connected: applies no stabilization
	Normal,     // operational, little/no contamination inside its footprint
	Warning,    // contamination has entered its footprint
	Critical,   // a significant share of its footprint is contaminated
	Breached    // most of its footprint is contaminated, or its own cell is
}

public class ContainmentNode
{
	public Vector2Int Position;

	public float Radius = 10f;
	public float Strength = 5f;

	// Gameplay gate, set by GridManager from infrastructure connectivity.
	// Inactive nodes apply no stabilization. The stabilization math itself is unchanged.
	public bool IsActive = true;

	// Condition telemetry, refreshed each tick while active. Never feeds back into the simulation.
	public int FootprintCells;
	public int ContaminatedFootprintCells;
	public bool CenterContaminated;
	public float Coverage;
	public ContainmentCondition Condition = ContainmentCondition.Inactive;

	public ContainmentNode(Vector2Int position)
	{
		Position = position;
	}
}
