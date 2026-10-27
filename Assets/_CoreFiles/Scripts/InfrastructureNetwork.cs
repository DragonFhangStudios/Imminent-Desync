using System.Collections.Generic;
using UnityEngine;

// Minimal "Infrastructure = Reach" connectivity.
//
// Rules:
// - Cores are always connected (network roots).
// - A structure is connected if it is within connectionRange of a connected
//   structure that can relay.
// - Relays: Core and Stabilizer. Nullifiers connect but do not extend reach.
//
// The result depends only on positions, types and range (not list order),
// so connectivity is deterministic.
public static class InfrastructureNetwork
{
	public static bool IsNetworkStructure(StructureType type)
	{
		return type == StructureType.Core ||
			type == StructureType.Stabilizer ||
			type == StructureType.Nullifier;
	}

	public static bool CanRelay(StructureType type)
	{
		return type == StructureType.Core ||
			type == StructureType.Stabilizer;
	}

	public static void Recalculate(List<PlacedStructure> structures, float connectionRange)
	{
		float rangeSq = connectionRange * connectionRange;
		Queue<PlacedStructure> frontier = new();

		foreach (var structure in structures)
		{
			structure.IsConnected = structure.Type == StructureType.Core;

			if (structure.IsConnected)
			{
				frontier.Enqueue(structure);
			}
		}

		while (frontier.Count > 0)
		{
			PlacedStructure relay = frontier.Dequeue();

			foreach (var candidate in structures)
			{
				if (candidate.IsConnected)
					continue;

				if (!IsNetworkStructure(candidate.Type))
					continue;

				if ((candidate.Position - relay.Position).sqrMagnitude > rangeSq)
					continue;

				candidate.IsConnected = true;

				if (CanRelay(candidate.Type))
				{
					frontier.Enqueue(candidate);
				}
			}
		}
	}

	// Would a new structure of this type, placed here, be connected?
	// (Used by the placement ghost; does not modify anything.)
	public static bool WouldConnect(List<PlacedStructure> structures, Vector2Int position, StructureType type, float connectionRange)
	{
		if (type == StructureType.Core)
			return true;

		if (!IsNetworkStructure(type))
			return false;

		float rangeSq = connectionRange * connectionRange;

		foreach (var structure in structures)
		{
			if (!structure.IsConnected || !CanRelay(structure.Type))
				continue;

			if ((position - structure.Position).sqrMagnitude <= rangeSq)
				return true;
		}

		return false;
	}
}
