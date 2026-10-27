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

	public Emitter(Vector2Int position, float maxIntegrity)
	{
		Position = position;
		MaxIntegrity = maxIntegrity;
		Integrity = maxIntegrity;
	}
}
