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

	// Placement sequence number (1, 2, 3 ...). Reconstruction serves blueprints in this order.
	public long PlacementOrder;

	// Reconstruction (increment 1, opt-in). Only true when Sync Infrastructure is enabled.
	// An unfinished blueprint connects to the network but gives no stabilization, no relaying
	// and no Nullifier effect until it has received ChargesRequired Sync Charges.
	public bool UnderConstruction;
	public int ChargesRequired;
	public int ChargesDelivered;
	public ConstructionStatus Construction = ConstructionStatus.Operational;

	// U2 Stabilizer supply (increment 2, opt-in). Untouched while U2 supply is off,
	// so effectiveness stays 1 and Stabilizers behave as before.
	// StoredCharges is also a completed Nullifier's pulse store while reconfiguration (increment 3) is on.
	public int StoredCharges;
	public float SupplyEffectiveness = 1f;
	public SupplyStatus Supply = SupplyStatus.NotApplicable;

	// Nullifier reconfiguration (increment 3, opt-in). NotApplicable while it is off.
	public NullifierStatus NullifierState = NullifierStatus.NotApplicable;

	public PlacedStructure(StructureType type, Vector2Int position)
	{
		Type = type;
		Position = position;
	}
}
