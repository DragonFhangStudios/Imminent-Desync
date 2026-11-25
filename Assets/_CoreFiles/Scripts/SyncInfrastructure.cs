using System;
using System.Collections.Generic;
using UnityEngine;

// Sync Charge generation and blueprint Reconstruction, roadmap increment 1.
//
// OPT-IN: GridManager runs this only when SyncInfrastructureSettings.enabled is true. The default is
// false, and Sandbox.unity does not contain these settings, so the Sandbox keeps its instant, free
// building and behaves exactly as before.
//
// Pure C# apart from Vector2Int and Mathf: no frame time, no randomness, so it is deterministic.
//
// Model (provisional; see Docs/SYNC_INFRASTRUCTURE.md):
// - Abstract throughput: each connected, completed Sync Core generates a fixed number of whole Sync
//   Charges per simulation tick. Nothing is stored; charges not allocated in a tick are unused.
// - Blueprints (Stabilizers and Nullifiers under construction) request charges while they are connected
//   to the Sync Network AND their own cell is clean. Others request nothing, so they never block anyone.
// - SyncChargeAllocator hands out the charges (Option A: automatic, placement order).
// - A blueprint completes when it has received its full cost. Until then it gives no stabilization,
//   no relaying and no Nullifier effect (enforced by GridManager and InfrastructureNetwork).

[Serializable]
public class SyncInfrastructureSettings
{
	[Tooltip("Opt-in. Off = current behavior: instant, free building and no Sync Charges. Affects structures placed after it is turned on; for a Sandbox test, tick it during Play mode before placing anything.")]
	public bool enabled = false;

	[Min(0)]
	[Tooltip("PROVISIONAL test value. Whole Sync Charges each connected Sync Core generates per simulation tick.")]
	public int coreGenerationPerTick = 4;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Sync Charges needed to complete a Stabilizer blueprint.")]
	public int stabilizerConstructionCost = 40;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Sync Charges needed to complete a Nullifier blueprint.")]
	public int nullifierConstructionCost = 60;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Most charges one blueprint can absorb per tick, so construction takes time even with surplus supply.")]
	public int maxConstructionIntakePerTick = 2;

	// Blueprint cost for a structure type, or 0 if the type is not built through Reconstruction.
	public int ConstructionCost(StructureType type)
	{
		switch (type)
		{
			case StructureType.Stabilizer: return Mathf.Max(1, stabilizerConstructionCost);
			case StructureType.Nullifier: return Mathf.Max(1, nullifierConstructionCost);
			default: return 0;
		}
	}
}

// What a structure's construction is doing. Completed (and never-blueprinted) structures are Operational.
public enum ConstructionStatus
{
	Operational,
	WaitingForConnection,   // blueprint not connected to the Sync Network
	WaitingForCleanGround,  // blueprint's cell is contaminated
	WaitingForCharges,      // eligible, but received no charge this tick (supply used elsewhere)
	Reconstructing          // received charges this tick
}

// Diagnostics: the last tick's figures, plus running totals.
public class SyncDiagnostics
{
	public long Ticks;

	public int Generated;
	public int Demand;
	public int Distributed;
	public int Unused;

	public int WaitingForConnection;
	public int WaitingForCleanGround;
	public int WaitingForCharges;
	public int Reconstructing;
	public int CompletedThisTick;

	public long TotalGenerated;
	public long TotalDistributed;
	public int TotalCompleted;

	public int Blueprints => WaitingForConnection + WaitingForCleanGround + WaitingForCharges + Reconstructing;
}

public static class SyncInfrastructure
{
	// Turns a newly placed structure into a blueprint if its type is built through Reconstruction.
	// The Sync Core is never a blueprint: it is the network root and the only generator.
	public static void MakeBlueprint(PlacedStructure structure, SyncInfrastructureSettings settings)
	{
		int cost = settings.ConstructionCost(structure.Type);

		if (cost <= 0)
			return;

		structure.UnderConstruction = true;
		structure.ChargesRequired = cost;
		structure.ChargesDelivered = 0;
		structure.Construction = ConstructionStatus.WaitingForConnection;
	}

	// One simulation tick of generation, allocation and construction.
	// Connectivity (IsConnected) must be current. isCellClean answers "is this cell clean ground?".
	// Returns true if at least one blueprint completed, so the caller must refresh the network.
	public static bool Step(
		List<PlacedStructure> structures,
		Func<Vector2Int, bool> isCellClean,
		SyncInfrastructureSettings settings,
		SyncDiagnostics diagnostics)
	{
		diagnostics.Ticks++;
		diagnostics.Generated = 0;
		diagnostics.Demand = 0;
		diagnostics.Distributed = 0;
		diagnostics.Unused = 0;
		diagnostics.WaitingForConnection = 0;
		diagnostics.WaitingForCleanGround = 0;
		diagnostics.WaitingForCharges = 0;
		diagnostics.Reconstructing = 0;
		diagnostics.CompletedThisTick = 0;

		// --- Generation ---
		int generated = 0;

		foreach (var structure in structures)
		{
			if (structure.Type == StructureType.Core && structure.IsConnected && !structure.UnderConstruction)
			{
				generated += Mathf.Max(0, settings.coreGenerationPerTick);
			}
		}

		// --- Demand: only connected blueprints on clean ground ask for charges ---
		var requests = new List<SyncChargeRequest>();
		var requesters = new List<PlacedStructure>();
		int intake = Mathf.Max(1, settings.maxConstructionIntakePerTick);

		foreach (var structure in structures)
		{
			if (!structure.UnderConstruction)
				continue;

			if (!structure.IsConnected)
			{
				structure.Construction = ConstructionStatus.WaitingForConnection;
				continue;
			}

			if (!isCellClean(structure.Position))
			{
				structure.Construction = ConstructionStatus.WaitingForCleanGround;
				continue;
			}

			int remaining = structure.ChargesRequired - structure.ChargesDelivered;
			int demand = Mathf.Min(intake, Mathf.Max(0, remaining));

			requests.Add(new SyncChargeRequest(SyncChargeCategory.Construction, structure.PlacementOrder, demand));
			requesters.Add(structure);
			diagnostics.Demand += demand;
		}

		// --- Allocation and construction ---
		int[] grants = SyncChargeAllocator.Allocate(generated, requests);
		int distributed = 0;
		bool completed = false;

		for (int i = 0; i < requesters.Count; i++)
		{
			PlacedStructure structure = requesters[i];
			int grant = grants[i];

			if (grant <= 0)
			{
				structure.Construction = ConstructionStatus.WaitingForCharges;
				continue;
			}

			structure.ChargesDelivered += grant;
			distributed += grant;

			if (structure.ChargesDelivered >= structure.ChargesRequired)
			{
				structure.ChargesDelivered = structure.ChargesRequired;
				structure.UnderConstruction = false;
				structure.Construction = ConstructionStatus.Operational;
				diagnostics.CompletedThisTick++;
				completed = true;
			}
			else
			{
				structure.Construction = ConstructionStatus.Reconstructing;
			}
		}

		// --- Diagnostics ---
		foreach (var structure in structures)
		{
			switch (structure.Construction)
			{
				case ConstructionStatus.WaitingForConnection: diagnostics.WaitingForConnection++; break;
				case ConstructionStatus.WaitingForCleanGround: diagnostics.WaitingForCleanGround++; break;
				case ConstructionStatus.WaitingForCharges: diagnostics.WaitingForCharges++; break;
				case ConstructionStatus.Reconstructing: diagnostics.Reconstructing++; break;
			}
		}

		diagnostics.Generated = generated;
		diagnostics.Distributed = distributed;
		diagnostics.Unused = generated - distributed;
		diagnostics.TotalGenerated += generated;
		diagnostics.TotalDistributed += distributed;
		diagnostics.TotalCompleted += diagnostics.CompletedThisTick;

		return completed;
	}
}
