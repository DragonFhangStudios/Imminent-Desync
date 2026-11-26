using UnityEngine;

// Level-defined Desync source (previously a bare Vector2Int in GridManager.sourcePositions).
// While active it is pinned to GridManager's Source Strength every tick, exactly as before.
// A Nullifier drains Integrity; at zero the Emitter stops sustaining Desync.
public class Emitter
{
	public Vector2Int Position;
	public float MaxIntegrity;
	public float Integrity;

	public bool IsNullified => Integrity <= 0f;

	// Staged reconfiguration N1–N3 (increment 3, opt-in; see NullifierReconfiguration). Untouched while it is
	// off, so the Integrity drain behaves as before. The progress belongs to the Emitter, not to a Nullifier,
	// so extra Nullifiers cannot accelerate it and completed stages survive losing a Nullifier.
	public bool ReconfigurationStarted;   // the player initiated the sequence (once per sequence)
	public int CompletedStages;           // 0..NullifierReconfiguration.StageCount; completed stages are kept
	public int StageProgress;             // qualifying ticks in the current stage; reset when interrupted
	public bool AwaitingPulse;            // current stage's time is complete, waiting for a full pulse store
	public int Interruptions;             // times the current stage was reset (diagnostics)

	public bool IsReconfigured => CompletedStages >= NullifierReconfiguration.StageCount;

	public Emitter(Vector2Int position, float maxIntegrity)
	{
		Position = position;
		MaxIntegrity = maxIntegrity;
		Integrity = maxIntegrity;
	}
}
