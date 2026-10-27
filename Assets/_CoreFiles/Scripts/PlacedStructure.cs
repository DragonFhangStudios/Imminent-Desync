using UnityEngine;

// A player structure on the grid (gameplay layer).
// Stabilizers keep their simulation data in the linked ContainmentNode, so the
// stabilization simulation stays separate from placement and connectivity.
public class PlacedStructure
{
	public StructureType Type;
	public Vector2Int Position;

	// True when connected to an Operational Core through the infrastructure network.
	public bool IsConnected;

	// Stabilizer only: the simulation node this structure drives.
	public ContainmentNode Node;

	// Prototype visual marker (a single tinted sprite).
	public SpriteRenderer Marker;

	public PlacedStructure(StructureType type, Vector2Int position)
	{
		Type = type;
		Position = position;
	}
}
