using System;
using System.Collections.Generic;
using UnityEngine;

// Nullifier reconfiguration N1–N3 with amendment N1-A (roadmap increment 3).
//
// OPT-IN: GridManager runs this only when SyncInfrastructureSettings.ReconfigurationActive is true
// (Sync Infrastructure enabled AND nullifierReconfigurationEnabled). Both default to false, and
// Sandbox.unity does not contain these settings, so the Sandbox keeps the Integrity drain.
//
// Pure C# apart from Vector2Int and Mathf: no frame time, no input, no randomness, so it is deterministic.
//
// Model (PROVISIONAL: decision 2, stage conditions, durations, effects, redundancy and costs, is open;
// see Docs/SYNC_INFRASTRUCTURE.md §9):
// - N1 / N1-A preparation: a completed, connected Nullifier with an Emitter in range fills a pulse store
//   with Sync Charges (SyncInfrastructure.Step). It is Ready when the store holds one pulse.
// - N1 manual initiation: the player initiates once per sequence from a Ready Nullifier (TryInitiate).
// - N2 staged pulses: three stages (Synchronize, Disrupt, Reconfigure). Each needs reconfigurationStageTicks
//   qualifying ticks, then a pulse that uses one full Nullifier store. With no full store the stage waits
//   (progress held). Synchronize and Disrupt have no simulation effect yet; each completed stage lowers the
//   Emitter's Integrity by a third, and the final stage sets it to 0, the drain's existing end state, so
//   production stops and the existing objective flag fires exactly as before.
// - N3: a tick with no qualifying Nullifier resets the current stage's progress; completed stages are kept.
//   Progress lives on the Emitter and advances at most once per tick, so extra Nullifiers never accelerate it.
//   Any qualifying Nullifier sustains a started sequence (provisional redundancy choice), without re-initiation.
// - A qualifying Nullifier is completed, connected and targeting that Emitter (nearest active Emitter in range).
// N4 (victory latch, Restoration, Emitter removal) and recovery mathematics belong to increment 4.
public enum NullifierStatus
{
	NotApplicable,  // reconfiguration off, not a Nullifier, or still a blueprint
	Disconnected,   // not connected to the Sync Network
	NoTarget,       // no active Emitter in range
	Preparing,      // filling its pulse store
	Ready,          // store full: the player can initiate
	Engaged         // its target's sequence has been initiated: it sustains the stages
}

public static class NullifierReconfiguration
{
	public const int StageCount = 3;

	private static readonly string[] StageNames = { "Synchronize", "Disrupt", "Reconfigure" };

	// Name of the stage in progress, given the number of completed stages.
	public static string CurrentStageName(int completedStages) =>
		completedStages >= 0 && completedStages < StageCount ? StageNames[completedStages] : "Reconfigured";

	public static bool IsOperationalNullifier(PlacedStructure structure, SyncInfrastructureSettings settings)
	{
		return settings.ReconfigurationActive && structure.Type == StructureType.Nullifier && !structure.UnderConstruction;
	}

	// One simulation tick: advance each initiated Emitter's current stage, fire pulses, then set the
	// Nullifier statuses. Connectivity must be current. findTarget returns a position's nearest active
	// Emitter in Nullifier range, or null.
	public static void Step(
		List<PlacedStructure> structures,
		List<Emitter> emitters,
		Func<Vector2Int, Emitter> findTarget,
		SyncInfrastructureSettings settings,
		SyncDiagnostics diagnostics)
	{
		int pulseCost = Mathf.Max(1, settings.nullifierPulseCost);
		int stageTicks = Mathf.Max(1, settings.reconfigurationStageTicks);

		diagnostics.PulsesFired = 0;

		// --- Stage progress (owned by the Emitter) ---
		foreach (var emitter in emitters)
		{
			if (!emitter.ReconfigurationStarted || emitter.IsReconfigured || emitter.IsNullified)
				continue;

			bool qualified = false;
			PlacedStructure pulseSource = null;

			// Structures are kept in placement order, so the oldest full store pulses first.
			foreach (var structure in structures)
			{
				if (!IsOperationalNullifier(structure, settings) || !structure.IsConnected)
					continue;

				if (findTarget(structure.Position) != emitter)
					continue;

				qualified = true;

				if (pulseSource == null && structure.StoredCharges >= pulseCost)
					pulseSource = structure;
			}

			if (!qualified)
			{
				// N3: an interrupted, unfinished stage restarts. Completed stages are kept.
				if (emitter.StageProgress > 0)
					emitter.Interruptions++;

				emitter.StageProgress = 0;
				emitter.AwaitingPulse = false;
				continue;
			}

			// At most one tick of progress per tick, however many Nullifiers qualify.
			if (emitter.StageProgress < stageTicks)
				emitter.StageProgress++;

			if (emitter.StageProgress < stageTicks)
				continue;

			if (pulseSource == null)
			{
				emitter.AwaitingPulse = true;
				continue;
			}

			pulseSource.StoredCharges -= pulseCost;
			diagnostics.PulsesFired++;
			diagnostics.TotalPulses++;

			emitter.CompletedStages++;
			emitter.StageProgress = 0;
			emitter.AwaitingPulse = false;
			emitter.Integrity = emitter.IsReconfigured
				? 0f
				: emitter.MaxIntegrity * (1f - (float)emitter.CompletedStages / StageCount);
		}

		// --- Nullifier statuses ---
		diagnostics.NullifiersPreparing = 0;
		diagnostics.NullifiersReady = 0;
		diagnostics.NullifiersEngaged = 0;
		diagnostics.NullifiersInactive = 0;

		foreach (var structure in structures)
		{
			if (structure.Type != StructureType.Nullifier)
				continue;

			structure.NullifierState = EvaluateStatus(structure, findTarget, settings);

			switch (structure.NullifierState)
			{
				case NullifierStatus.Preparing: diagnostics.NullifiersPreparing++; break;
				case NullifierStatus.Ready: diagnostics.NullifiersReady++; break;
				case NullifierStatus.Engaged: diagnostics.NullifiersEngaged++; break;
				case NullifierStatus.Disconnected:
				case NullifierStatus.NoTarget: diagnostics.NullifiersInactive++; break;
			}
		}
	}

	public static NullifierStatus EvaluateStatus(
		PlacedStructure structure,
		Func<Vector2Int, Emitter> findTarget,
		SyncInfrastructureSettings settings)
	{
		if (!IsOperationalNullifier(structure, settings))
			return NullifierStatus.NotApplicable;

		if (!structure.IsConnected)
			return NullifierStatus.Disconnected;

		Emitter target = findTarget(structure.Position);

		if (target == null)
			return NullifierStatus.NoTarget;

		if (target.ReconfigurationStarted)
			return NullifierStatus.Engaged;

		return structure.StoredCharges >= Mathf.Max(1, settings.nullifierPulseCost)
			? NullifierStatus.Ready
			: NullifierStatus.Preparing;
	}

	// N1 manual initiation, as a simulation command (input-independent). Succeeds only from a Ready
	// Nullifier; starts its target's sequence. Returns false and changes nothing otherwise.
	public static bool TryInitiate(
		PlacedStructure nullifier,
		Func<Vector2Int, Emitter> findTarget,
		SyncInfrastructureSettings settings)
	{
		if (nullifier == null || EvaluateStatus(nullifier, findTarget, settings) != NullifierStatus.Ready)
			return false;

		findTarget(nullifier.Position).ReconfigurationStarted = true;
		nullifier.NullifierState = NullifierStatus.Engaged;
		return true;
	}
}
