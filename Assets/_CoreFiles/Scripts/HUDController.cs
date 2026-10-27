using UnityEngine;
using UnityEngine.UIElements;

public class HUDController : MonoBehaviour
{
	private Label integrityLabel;
	private Label brushRadiusLabel;
	private Label containmentStatusLabel;
	private Label anomalyCountLabel;
	private Label containmentNodeLabel;
	private Label totalDesyncLabel;
	private Label peakDesyncLabel;
	private Label totalStabilizationLabel;
	private Label pressureRatioLabel;
	private Label activeNodesLabel;
	private Label activeStabilizationLabel;
	private Label netDeltaLabel;
	private Label simulationTimeLabel;
	private Label contaminatedCellsLabel;
	private Label sourceOutputLabel;
	private Label decayAppliedLabel;
	private Label activeDesyncCellsLabel;
	private Label mouseCellLabel;
	private Label cellDesyncLabel;
	private Label cellStabilizationLabel;
	private Label simulationStateLabel;
	private Label simulationTicksLabel;
	private Label cellNetDeltaLabel;
	private Label cellElevationLabel;

	// Pressure-pass telemetry labels (optional: add to the UXML with these names).
	private Label buildupLabel;
	private Label activityLabel;
	private Label frontierLabel;

	// Unity 6.5+: UI Document is obsolete and replaced by Panel Renderer.
	// Panel Renderer has no rootVisualElement property; it hands us the root
	// through a reload callback (called immediately if the UI is already
	// initialized, and again whenever the UI reloads).
	private PanelRenderer panelRenderer;

	private void OnEnable()
	{
		panelRenderer = FindAnyObjectByType<PanelRenderer>();

		if (panelRenderer == null)
		{
			Debug.LogError("HUDController: No PanelRenderer found in the scene. HUD will not update.");
			return;
		}

		panelRenderer.RegisterUIReloadCallback(OnUIReload);
	}

	private void OnDisable()
	{
		if (panelRenderer != null)
		{
			panelRenderer.UnregisterUIReloadCallback(OnUIReload);
		}
	}

	private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
	{
		// Re-query every time: if the UI reloads, the old Label references are stale.
		integrityLabel = root.Q<Label>("IntegrityLabel");
		brushRadiusLabel = root.Q<Label>("BrushRadiusLabel");
		containmentStatusLabel = root.Q<Label>("ContainmentStatusLabel");
		anomalyCountLabel =	root.Q<Label>("AnomalyCountLabel");
		containmentNodeLabel = root.Q<Label>("ContainmentNodeLabel");
		totalDesyncLabel = root.Q<Label>("TotalDesyncLabel");
		peakDesyncLabel = root.Q<Label>("PeakDesyncLabel");
		totalStabilizationLabel = root.Q<Label>("TotalStabilizationLabel");
		pressureRatioLabel = root.Q<Label>("PressureRatioLabel");
		activeNodesLabel = root.Q<Label>("ActiveNodesLabel");
		activeStabilizationLabel = root.Q<Label>("ActiveStabilizationLabel");
		netDeltaLabel = root.Q<Label>("NetDeltaLabel");
		simulationTimeLabel = root.Q<Label>("SimulationTimeLabel");
		contaminatedCellsLabel = root.Q<Label>("ContaminatedCellsLabel");
		sourceOutputLabel = root.Q<Label>("SourceOutputLabel");
		decayAppliedLabel = root.Q<Label>("DecayAppliedLabel");
		activeDesyncCellsLabel = root.Q<Label>("ActiveDesyncCellsLabel");
		mouseCellLabel = root.Q<Label>("MouseCellLabel");
		cellDesyncLabel = root.Q<Label>("CellDesyncLabel");
		cellStabilizationLabel = root.Q<Label>("CellStabilizationLabel");
		simulationStateLabel = root.Q<Label>("SimulationStateLabel");
		simulationTicksLabel = root.Q<Label>("SimulationTicksLabel");
		cellNetDeltaLabel = root.Q<Label>("CellNetDeltaLabel");
		cellElevationLabel = root.Q<Label>("CellElevationLabel");
		buildupLabel = root.Q<Label>("BuildupLabel");
		activityLabel = root.Q<Label>("ActivityLabel");
		frontierLabel = root.Q<Label>("FrontierLabel");

		// HUD cleanup: retired fields are hidden here (no UXML edit required).
		// Their information is either merged into another line or no longer meaningful.
		Hide(totalStabilizationLabel);   // = operational nodes x Node Strength (redundant)
		Hide(pressureRatioLabel);        // world Desync / engaged stabilization (misleading)
		Hide(activeNodesLabel);          // replaced by per-node condition counts in Containment Status
		Hide(activeStabilizationLabel);  // only meaningful as Pressure Ratio's denominator
		Hide(netDeltaLabel);             // was broken (showed total Desync); real change now on Total Desync line
		Hide(simulationTimeLabel);       // counted while paused; Simulation line shows ticks
		Hide(sourceOutputLabel);         // = active Emitters x Source Strength (redundant)
		Hide(decayAppliedLabel);         // summed decay over empty cells (mostly constant)
		Hide(simulationTicksLabel);      // merged into Simulation line
		Hide(cellNetDeltaLabel);         // merged into Cell Desync line

		// Optional labels from the previous pass are now merged into existing lines.
		Hide(buildupLabel);
		Hide(activityLabel);
		Hide(frontierLabel);

	}

	private static void Hide(Label label)
	{
		if (label != null)
		{
			label.style.display = DisplayStyle.None;
		}
	}

	public void UpdateHUD(float integrity, int brushRadius, DebugStats stats)
	{
		// The UI may not be bound yet on the first frames (or if no PanelRenderer exists).
		if (integrityLabel == null)
			return;

		// ---- World condition ----
		integrityLabel.text = $"Reality Integrity: {integrity:F1}%";

		if (!string.IsNullOrEmpty(stats.containmentStatusText))
		{
			// Experiment B: status describes the containment network's condition.
			containmentStatusLabel.text = $"Containment Status: {stats.containmentStatusText}";
		}
		else
		{
			// Legacy: status derived from world-wide Reality Integrity.
			if (integrity > 90f)
			{
				containmentStatusLabel.text = "Containment Status: Stable";
			}
			else if (integrity > 75f)
			{
				containmentStatusLabel.text = "Containment Status: Warning";
			}
			else if (integrity > 50f)
			{
				containmentStatusLabel.text = "Containment Status: Critical";
			}
			else
			{
				containmentStatusLabel.text = "Containment Status: COLLAPSE IMMINENT";
			}
		}

		anomalyCountLabel.text = stats.totalEmitters > 0
			? $"Anomalies: {stats.anomalyCount} active  |  Emitter Integrity: {stats.emitterIntegrityPercent:F0}%"
			: "Anomalies: 0";

		containmentNodeLabel.text = $"Containment Nodes: {stats.containmentNodeCount} ({stats.operationalNodeCount} operational)";

		// ---- Desync field ----
		totalDesyncLabel.text = $"Total Desync: {stats.totalDesync:N0}  ({stats.worldDesyncDelta:+0.0;-0.0;0.0}/tick)";

		// Buildup / clamp telemetry merged into the Peak line.
		peakDesyncLabel.text =
			$"Peak Desync: {stats.peakDesync:F1}  |  At Cap: {stats.cellsAtCap:N0}  |  Excess: {stats.peakExcess:F3}/tick peak, {stats.tickExcessClipped:N1}/tick clipped";

		// Frontier telemetry merged into the Contaminated line.
		contaminatedCellsLabel.text =
			$"Contaminated: {stats.contaminatedCells:N0} cells  |  Frontier: {stats.frontierCells:N0}  |  Last {stats.activityWindowTicks} ticks: +{stats.frontAdvanced} / -{stats.frontRetreated}";

		// "Binoculars": activity breakdown replaces the bare Active Desync Cells count.
		int fieldCells = stats.growingCells + stats.stableCells + stats.recedingCells;

		activeDesyncCellsLabel.text =
			$"Desync Activity: {fieldCells:N0} cells  |  Growing {stats.growingCells:N0}  |  Stable {stats.stableCells:N0}  |  Receding {stats.recedingCells:N0}  |  Max {stats.maxGrowth:+0.00;-0.00;0.00} / {stats.maxLoss:+0.00;-0.00;0.00}";

		// ---- Controls / simulation ----
		brushRadiusLabel.text = $"Tool: {stats.currentToolName}  |  Brush Radius: {brushRadius}";

		simulationStateLabel.text = $"Simulation: {(stats.isPaused ? "Paused" : "Running")}  |  Tick {stats.simulationTicks:N0}";

		// ---- Hovered cell ----
		mouseCellLabel.text = $"Mouse Cell: ({stats.mouseX}, {stats.mouseY})";

		cellDesyncLabel.text = stats.hoveredExcess > 0f
			? $"Cell Desync: {stats.hoveredDesync:F3}  ({stats.hoveredNetDelta:+0.000;-0.000;0.000}/tick, +{stats.hoveredExcess:F3} clipped at cap)"
			: $"Cell Desync: {stats.hoveredDesync:F3}  ({stats.hoveredNetDelta:+0.000;-0.000;0.000}/tick)";

		cellStabilizationLabel.text = $"Cell Stabilization: {stats.hoveredStabilization:F3}";

		// Debug readout for terrain validation (Test 5).
		// Null-guarded: requires a Label named "CellElevationLabel" in the HUD UXML.
		if (cellElevationLabel != null)
		{
			cellElevationLabel.text = $"Cell Elevation: {stats.hoveredElevation:F3}";
		}
	}
}
