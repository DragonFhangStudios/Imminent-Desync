using System;
using System.Collections.Generic;
using UnityEngine;

// Sync Charge generation, blueprint Reconstruction (roadmap increment 1) and U2 Stabilizer supply with
// the recovery safeguards S1–S3 (roadmap increment 2). S4 (Reality Integrity defeat) lives in GridManager.
//
// OPT-IN: GridManager runs this only when SyncInfrastructureSettings.enabled is true, and U2 supply only
// when stabilizerSupplyEnabled is ALSO true. Both default to false, and Sandbox.unity does not contain
// these settings, so the Sandbox keeps its instant, free building and unsupplied Stabilizers.
//
// Pure C# apart from Vector2Int and Mathf: no frame time, no randomness, so it is deterministic.
//
// Model (provisional; see Docs/SYNC_INFRASTRUCTURE.md):
// - Abstract throughput: each connected, completed Sync Core generates a fixed number of whole Sync
//   Charges per simulation tick. The Core stores nothing; charges not allocated in a tick are unused.
// - Blueprints request charges while connected AND on clean ground (S1: ground within the Sync Core's
//   safe zone always counts as buildable). Others request nothing, so they never block anyone.
// - U2: each completed Stabilizer needs an upkeep every tick. It keeps a small store (S3); connected
//   Stabilizers request upkeep plus refill. Effectiveness = share of the upkeep actually consumed this
//   tick (linear curve, provisional). With nothing supplied and the store empty, effectiveness is zero.
// - S2: the foothold (the connected Stabilizers nearest a Sync Core; increment 2A, finding F4) gets its
//   upkeep before anything else.
// - F3 (increment 2A): after the foothold's upkeep, a minimum share of generation is reserved for
//   construction, so construction never fully starves. A Sync Core always generates enough for both.
// - SyncChargeAllocator hands out the rest (Option A: round-robin across categories, placement order).

[Serializable]
public class SyncInfrastructureSettings
{
	[Tooltip("Opt-in. Off = current behavior: instant, free building and no Sync Charges. Affects structures placed after it is turned on; for a Sandbox test, tick it during Play mode before placing anything.")]
	public bool enabled = false;

	[Min(0)]
	[Tooltip("PROVISIONAL test value (increment 2A: 8, was 4). Whole Sync Charges each connected Sync Core generates per simulation tick.")]
	public int coreGenerationPerTick = 8;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Sync Charges needed to complete a Stabilizer blueprint.")]
	public int stabilizerConstructionCost = 40;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Sync Charges needed to complete a Nullifier blueprint.")]
	public int nullifierConstructionCost = 60;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Most charges one blueprint can absorb per tick, so construction takes time even with surplus supply.")]
	public int maxConstructionIntakePerTick = 2;

	[Range(0f, 0.9f)]
	[Tooltip("F3 safeguard (increment 2A). PROVISIONAL value. Share of each tick's generation reserved for construction after the foothold's upkeep, so construction never fully starves. Unused reserve goes back to the general allocation. 0 = off.")]
	public float constructionMinimumShare = 0.25f;

	[Min(0f)]
	[Tooltip("S1 safeguard. Blueprints within this distance (cells) of a Sync Core count as on buildable ground even when contaminated, so there is always somewhere to rebuild from. 0 = off. PROVISIONAL value.")]
	public float coreSafeZoneRadius = 3f;

	[Header("U2 Stabilizer supply (increment 2, opt-in)")]
	[Tooltip("Opt-in, and only with Sync Infrastructure enabled. Off = Stabilizers need no supply (increment 1 behavior).")]
	public bool stabilizerSupplyEnabled = false;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Sync Charges each completed Stabilizer consumes per tick at full effectiveness.")]
	public int stabilizerUpkeepPerTick = 2;

	[Min(0)]
	[Tooltip("S3 safeguard. PROVISIONAL test value. Sync Charges a Stabilizer can store, so a brief disconnection does not drop it to zero at once.")]
	public int stabilizerStoreCapacity = 20;

	[Min(1)]
	[Tooltip("PROVISIONAL test value. Most charges one Stabilizer can receive per tick (upkeep plus refill).")]
	public int maxSupplyIntakePerTick = 4;

	[Min(0)]
	[Tooltip("S2 safeguard. PROVISIONAL test value. The N connected, completed Stabilizers nearest a Sync Core are the foothold (ties: placement order): their upkeep is served first, and a Sync Core always generates at least their total upkeep plus the construction share.")]
	public int footholdStabilizerCount = 2;

	public bool SupplyActive => enabled && stabilizerSupplyEnabled;

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

	// F3 (increment 2A): charges reserved for construction out of a tick's generation (rounded up, so any
	// positive share reserves at least one charge).
	public int ConstructionReserve(int generated)
	{
		float share = Mathf.Clamp(constructionMinimumShare, 0f, 0.9f);

		if (share <= 0f || generated <= 0)
			return 0;

		return Mathf.Min(generated, Mathf.CeilToInt(share * generated - 0.0001f));
	}

	// The foothold's total upkeep (S2).
	public int FootholdUpkeep() => Mathf.Max(0, footholdStabilizerCount) * Mathf.Max(1, stabilizerUpkeepPerTick);

	// Per-Core generation. With U2 active, S2 guarantees at least the foothold's upkeep, and (F3) enough on
	// top of it for the construction share: the smallest generation g with g - ConstructionReserve(g) >= upkeep.
	public int CoreGeneration()
	{
		int generation = Mathf.Max(0, coreGenerationPerTick);

		if (SupplyActive)
		{
			int upkeep = FootholdUpkeep();
			int floor = upkeep;

			while (upkeep > 0 && floor - ConstructionReserve(floor) < upkeep)
				floor++;

			generation = Mathf.Max(generation, floor);
		}

		return generation;
	}
}

// What a structure's construction is doing. Completed (and never-blueprinted) structures are Operational.
public enum ConstructionStatus
{
	Operational,
	WaitingForConnection,   // blueprint not connected to the Sync Network
	WaitingForCleanGround,  // blueprint's cell is contaminated (and outside the S1 safe zone)
	WaitingForCharges,      // eligible, but received no charge this tick (supply used elsewhere)
	Reconstructing          // received charges this tick
}

// U2 supply state of a completed Stabilizer.
public enum SupplyStatus
{
	NotApplicable,  // not a completed Stabilizer, or U2 supply is off
	Supplied,       // full upkeep met from this tick's charges
	OnStore,        // full upkeep met, partly or wholly from its store
	Reduced,        // only part of the upkeep met: reduced effectiveness
	Starved         // nothing met: zero stabilization
}

// Diagnostics: the last tick's figures, plus running totals.
public class SyncDiagnostics
{
	public long Ticks;

	public int Generated;
	public int Demand;
	public int Distributed;
	public int Unused;

	public int ConstructionDemand;
	public int ConstructionDelivered;
	public int SupplyDemand;
	public int SupplyDelivered;

	// Increment 2A: charges set aside before the general allocation this tick.
	public int FootholdReserved;      // S2: foothold upkeep
	public int ConstructionReserved;  // F3: construction share actually used by eligible blueprints

	public int WaitingForConnection;
	public int WaitingForCleanGround;
	public int WaitingForCharges;
	public int Reconstructing;
	public int CompletedThisTick;

	public int StabilizersSupplied;
	public int StabilizersOnStore;
	public int StabilizersReduced;
	public int StabilizersStarved;
	public int StoredCharges;

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

	// S1: within the safe zone of any Sync Core.
	public static bool InCoreSafeZone(List<PlacedStructure> structures, Vector2Int position, SyncInfrastructureSettings settings)
	{
		if (settings.coreSafeZoneRadius <= 0f)
			return false;

		float radiusSq = settings.coreSafeZoneRadius * settings.coreSafeZoneRadius;

		foreach (var structure in structures)
		{
			if (structure.Type == StructureType.Core && (structure.Position - position).sqrMagnitude <= radiusSq)
				return true;
		}

		return false;
	}

	// Is this structure a completed Stabilizer that takes part in U2 supply?
	public static bool IsSuppliedStabilizer(PlacedStructure structure, SyncInfrastructureSettings settings)
	{
		return settings.SupplyActive && structure.Type == StructureType.Stabilizer && !structure.UnderConstruction;
	}

	// One simulation tick of generation, allocation, construction and U2 supply.
	// Connectivity (IsConnected) must be current. isCellClean answers "is this cell clean ground?".
	// Returns true if at least one blueprint completed, so the caller must refresh the network.
	public static bool Step(
		List<PlacedStructure> structures,
		Func<Vector2Int, bool> isCellClean,
		SyncInfrastructureSettings settings,
		SyncDiagnostics diagnostics)
	{
		ResetTickDiagnostics(diagnostics);

		// --- Generation ---
		int generated = 0;

		foreach (var structure in structures)
		{
			if (structure.Type == StructureType.Core && structure.IsConnected && !structure.UnderConstruction)
			{
				generated += settings.CoreGeneration();
			}
		}

		int available = generated;

		// --- Requests ---
		var requests = new List<SyncChargeRequest>();
		var requesters = new List<PlacedStructure>();
		int constructionIntake = Mathf.Max(1, settings.maxConstructionIntakePerTick);

		foreach (var structure in structures)
		{
			if (!structure.UnderConstruction)
				continue;

			if (!structure.IsConnected)
			{
				structure.Construction = ConstructionStatus.WaitingForConnection;
				continue;
			}

			if (!isCellClean(structure.Position) && !InCoreSafeZone(structures, structure.Position, settings))
			{
				structure.Construction = ConstructionStatus.WaitingForCleanGround;
				continue;
			}

			int remaining = structure.ChargesRequired - structure.ChargesDelivered;
			int demand = Mathf.Min(constructionIntake, Mathf.Max(0, remaining));

			requests.Add(new SyncChargeRequest(SyncChargeCategory.Construction, structure.PlacementOrder, demand));
			requesters.Add(structure);
			diagnostics.ConstructionDemand += demand;
		}

		// U2: connected, completed Stabilizers ask for upkeep plus refill. S2 reserves the foothold's upkeep first.
		var reserved = new Dictionary<PlacedStructure, int>();
		int upkeep = Mathf.Max(1, settings.stabilizerUpkeepPerTick);
		int capacity = Mathf.Max(0, settings.stabilizerStoreCapacity);
		int supplyIntake = Mathf.Max(1, settings.maxSupplyIntakePerTick);

		if (settings.SupplyActive)
		{
			// Foothold reservations, nearest the Sync Core first, before the general allocation.
			foreach (var structure in FindFoothold(structures, settings))
			{
				if (!structure.IsConnected || available <= 0)
					continue;

				int demand = Mathf.Min(supplyIntake, Mathf.Max(0, capacity - structure.StoredCharges) + upkeep);
				int grant = Mathf.Min(available, Mathf.Min(upkeep, demand));
				reserved[structure] = grant;
				available -= grant;
				diagnostics.FootholdReserved += grant;
			}
		}

		// F3 (increment 2A): after the foothold's upkeep, a minimum share of generation goes to construction,
		// within construction in placement order. Only eligible blueprints use it; the rest stays available.
		// Only construction requests exist in the list at this point. With construction as the only consumer
		// this gives exactly the same result as a single allocation (the allocator fills in placement order).
		int[] constructionPreGrants = SyncChargeAllocator.Allocate(Mathf.Min(available, settings.ConstructionReserve(generated)), requests);

		for (int i = 0; i < constructionPreGrants.Length; i++)
		{
			if (constructionPreGrants[i] <= 0)
				continue;

			SyncChargeRequest request = requests[i];
			request.Demand -= constructionPreGrants[i];
			requests[i] = request;
			available -= constructionPreGrants[i];
			diagnostics.ConstructionReserved += constructionPreGrants[i];
		}

		if (settings.SupplyActive)
		{
			foreach (var structure in structures)
			{
				if (!IsSuppliedStabilizer(structure, settings) || !structure.IsConnected)
					continue;

				int demand = Mathf.Min(supplyIntake, Mathf.Max(0, capacity - structure.StoredCharges) + upkeep);
				diagnostics.SupplyDemand += demand;

				reserved.TryGetValue(structure, out int already);
				int rest = demand - already;

				requests.Add(new SyncChargeRequest(SyncChargeCategory.StabilizerSupply, structure.PlacementOrder, rest));
				requesters.Add(structure);
			}
		}

		diagnostics.Demand = diagnostics.ConstructionDemand + diagnostics.SupplyDemand;

		// --- Allocation ---
		int[] grants = SyncChargeAllocator.Allocate(available, requests);

		for (int i = 0; i < constructionPreGrants.Length; i++)
			grants[i] += constructionPreGrants[i];

		bool completed = false;

		// --- Construction ---
		for (int i = 0; i < requesters.Count; i++)
		{
			PlacedStructure structure = requesters[i];

			if (!structure.UnderConstruction)
				continue;

			int grant = grants[i];

			if (grant <= 0)
			{
				structure.Construction = ConstructionStatus.WaitingForCharges;
				continue;
			}

			structure.ChargesDelivered += grant;
			diagnostics.ConstructionDelivered += grant;

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

		// --- U2 supply: receive, consume upkeep, set effectiveness ---
		if (settings.SupplyActive)
		{
			var received = new Dictionary<PlacedStructure, int>(reserved);

			for (int i = 0; i < requesters.Count; i++)
			{
				if (requests[i].Category != SyncChargeCategory.StabilizerSupply)
					continue;

				received.TryGetValue(requesters[i], out int already);
				received[requesters[i]] = already + grants[i];
			}

			// Every completed Stabilizer consumes upkeep from what it received plus its store.
			// Disconnected ones receive nothing and run down their store. A Stabilizer completed this
			// tick has an empty store and received nothing yet, so its supply starts next tick.
			foreach (var structure in structures)
			{
				if (!IsSuppliedStabilizer(structure, settings))
					continue;

				received.TryGetValue(structure, out int got);
				ApplySupply(structure, got, upkeep, diagnostics);
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
		diagnostics.Distributed = diagnostics.ConstructionDelivered + diagnostics.SupplyDelivered;
		diagnostics.Unused = generated - diagnostics.Distributed;
		diagnostics.TotalGenerated += generated;
		diagnostics.TotalDistributed += diagnostics.Distributed;
		diagnostics.TotalCompleted += diagnostics.CompletedThisTick;

		return completed;
	}

	// S2 foothold (increment 2A, finding F4): the footholdStabilizerCount connected, completed Stabilizers
	// nearest a connected Sync Core (straight-line distance; ties by placement order), nearest first.
	// Disconnected (stranded) Stabilizers never hold a foothold slot.
	public static List<PlacedStructure> FindFoothold(List<PlacedStructure> structures, SyncInfrastructureSettings settings)
	{
		var cores = new List<Vector2Int>();
		var candidates = new List<PlacedStructure>();
		var distance = new Dictionary<PlacedStructure, int>();

		foreach (var structure in structures)
		{
			if (structure.Type == StructureType.Core && structure.IsConnected && !structure.UnderConstruction)
				cores.Add(structure.Position);
		}

		foreach (var structure in structures)
		{
			if (!IsSuppliedStabilizer(structure, settings) || !structure.IsConnected || cores.Count == 0)
				continue;

			int nearest = int.MaxValue;

			foreach (Vector2Int core in cores)
				nearest = Mathf.Min(nearest, (structure.Position - core).sqrMagnitude);

			distance[structure] = nearest;
			candidates.Add(structure);
		}

		candidates.Sort((a, b) =>
		{
			int byDistance = distance[a].CompareTo(distance[b]);
			return byDistance != 0 ? byDistance : a.PlacementOrder.CompareTo(b.PlacementOrder);
		});

		if (candidates.Count > settings.footholdStabilizerCount)
			candidates.RemoveRange(Mathf.Max(0, settings.footholdStabilizerCount), candidates.Count - Mathf.Max(0, settings.footholdStabilizerCount));

		return candidates;
	}

	private static void ApplySupply(PlacedStructure structure, int received, int upkeep, SyncDiagnostics diagnostics)
	{
		structure.StoredCharges += received;
		diagnostics.SupplyDelivered += received;

		int consumed = Mathf.Min(upkeep, structure.StoredCharges);
		structure.StoredCharges -= consumed;

		structure.SupplyEffectiveness = (float)consumed / upkeep;

		if (consumed >= upkeep)
			structure.Supply = received >= upkeep ? SupplyStatus.Supplied : SupplyStatus.OnStore;
		else if (consumed > 0)
			structure.Supply = SupplyStatus.Reduced;
		else
			structure.Supply = SupplyStatus.Starved;

		if (structure.Node != null)
			structure.Node.SupplyEffectiveness = structure.SupplyEffectiveness;

		switch (structure.Supply)
		{
			case SupplyStatus.Supplied: diagnostics.StabilizersSupplied++; break;
			case SupplyStatus.OnStore: diagnostics.StabilizersOnStore++; break;
			case SupplyStatus.Reduced: diagnostics.StabilizersReduced++; break;
			case SupplyStatus.Starved: diagnostics.StabilizersStarved++; break;
		}

		diagnostics.StoredCharges += structure.StoredCharges;
	}

	private static void ResetTickDiagnostics(SyncDiagnostics diagnostics)
	{
		diagnostics.Ticks++;
		diagnostics.Generated = 0;
		diagnostics.Demand = 0;
		diagnostics.Distributed = 0;
		diagnostics.Unused = 0;
		diagnostics.ConstructionDemand = 0;
		diagnostics.ConstructionDelivered = 0;
		diagnostics.SupplyDemand = 0;
		diagnostics.SupplyDelivered = 0;
		diagnostics.FootholdReserved = 0;
		diagnostics.ConstructionReserved = 0;
		diagnostics.WaitingForConnection = 0;
		diagnostics.WaitingForCleanGround = 0;
		diagnostics.WaitingForCharges = 0;
		diagnostics.Reconstructing = 0;
		diagnostics.CompletedThisTick = 0;
		diagnostics.StabilizersSupplied = 0;
		diagnostics.StabilizersOnStore = 0;
		diagnostics.StabilizersReduced = 0;
		diagnostics.StabilizersStarved = 0;
		diagnostics.StoredCharges = 0;
	}
}
