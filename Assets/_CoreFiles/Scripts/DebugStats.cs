public struct DebugStats
{
	public int anomalyCount;
	public int containmentNodeCount;

	public int activeNodes;

	public float totalDesync;
	public float peakDesync;

	public float totalStabilization;
	public float activeStabilization;

	public float pressureRatio;

	public float netDelta;

	public float simulationTime;

	public int contaminatedCells;

	public float sourceOutput;

	public float totalDecayApplied;
	
	public int activeDesyncCells;

	public int mouseX;
	public int mouseY;

	public float hoveredDesync;
	public float hoveredStabilization;

	public bool isPaused;
	public long simulationTicks;
	public float hoveredNetDelta;
	public float hoveredElevation;

	// Vertical slice telemetry
	public int totalEmitters;
	public float emitterIntegrityPercent;
	public int operationalNodeCount;
	public string currentToolName;

	// Experiment A: buildup / clamp telemetry
	public int cellsAtCap;
	public float peakExcess;
	public float tickExcessClipped;
	public float totalExcessClipped;
	public float hoveredExcess;

	// Experiment B: network containment status (empty = use legacy integrity-based status)
	public string containmentStatusText;

	// Experiment D: simulation activity
	public int growingCells;
	public int stableCells;
	public int recedingCells;
	public float maxGrowth;
	public float maxLoss;
	public int frontierCells;
	public int frontAdvanced;
	public int frontRetreated;
	public int activityWindowTicks;

	// HUD cleanup: real change in world Desync since the previous tick
	// (replaces the old netDelta, which duplicated total Desync).
	public float worldDesyncDelta;
}