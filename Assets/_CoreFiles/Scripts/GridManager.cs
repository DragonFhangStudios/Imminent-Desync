using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using System.Collections.Generic;

public class GridManager : MonoBehaviour
{
	[Header("Grid")]
	[SerializeField] private int width = 100;
	[SerializeField] private int height = 100;

	[Header("Simulation")]
	[SerializeField] private float spreadStrength = 0.25f;
	[SerializeField] private float decayRate = 0.05f;
	[SerializeField] private float growthRate = 0.02f;
	[SerializeField] private float tickRate = 0.05f;
	[SerializeField] private bool isPaused;
	[SerializeField] private long simulationTicks;

	[Header("Desync")]
	[SerializeField] private float sourceStrength = 250f;
	[SerializeField] private float maxDesync = 250f;
	[SerializeField] private float contaminationThreshold = 5f;

	[SerializeField]
	[Tooltip("Sandbox: create a permanent Desync source at the grid center on Start. Disable to start with no source and place one manually (Shift + Left Click).")]
	private bool startWithCenterSource = true;

	[Header("Containment")]
	[SerializeField] private float nodeRadius = 10f;
	[SerializeField] private float nodeStrength = 5f;

	[Header("Infrastructure")]
	[SerializeField, Min(1f)]
	[Tooltip("Max distance (cells) between two structures for them to connect. Cores and Stabilizers relay the connection onward; Nullifiers do not.")]
	private float connectionRange = 8f;

	[Header("Emitter / Nullifier")]
	[SerializeField, Min(1f)]
	[Tooltip("Starting integrity of each Emitter. A Nullifier drains it; at zero the Emitter stops sustaining Desync.")]
	private float emitterMaxIntegrity = 500f;

	[SerializeField, Min(1f)]
	[Tooltip("A connected Nullifier drains the nearest active Emitter within this distance (cells).")]
	private float nullifierRange = 6f;

	[SerializeField, Min(0f)]
	[Tooltip("Emitter integrity removed per simulation tick by each operational Nullifier.")]
	private float nullifierDrainPerTick = 1f;

	[Header("Containment Condition (experiment)")]
	[SerializeField]
	[Tooltip("On: Containment Status describes the containment network (worst operational Stabilizer). Off: legacy status from world-wide Reality Integrity.")]
	private bool networkContainmentStatus = true;

	[SerializeField, Range(0f, 1f)]
	[Tooltip("WARNING when at least this fraction of a Stabilizer's footprint is contaminated (Desync above Contamination Threshold).")]
	private float warningCoverage = 0.05f;

	[SerializeField, Range(0f, 1f)]
	[Tooltip("CRITICAL when at least this fraction of a Stabilizer's footprint is contaminated.")]
	private float criticalCoverage = 0.25f;

	[SerializeField, Range(0f, 1f)]
	[Tooltip("BREACHED when at least this fraction of a Stabilizer's footprint is contaminated.")]
	private float breachCoverage = 0.5f;

	[SerializeField]
	[Tooltip("Also mark a Stabilizer BREACHED if its own cell (strongest stabilization point) is contaminated.")]
	private bool breachOnCenterContamination = true;

	[SerializeField]
	[Tooltip("Gently pulse CRITICAL and BREACHED Stabilizer markers.")]
	private bool pulseStressedMarkers = true;

	[Header("Simulation Activity (experiment)")]
	[SerializeField, Min(0f)]
	[Tooltip("Cells whose per-tick change is within +/- this value count as Stable.")]
	private float activityEpsilon = 0.01f;

	[SerializeField, Min(1)]
	[Tooltip("Frontier advance/retreat counts are summed over this many recent ticks.")]
	private int activityWindowTicks = 20;

	[Header("Sandbox")]
	[SerializeField]
	[Tooltip("Development/sandbox controls: Shift+Left/Right place/remove Emitters, Source build tool, and the Desync paint/erase brush. Disable for mission gameplay.")]
	private bool sandboxDebugControls = true;

	[Header("Terrain Generation")]
	[SerializeField, FormerlySerializedAs("terrainSeed")]
	[Tooltip("Same seed and same settings always produce the same map.")]
	private int mapSeed = 12345;

	[SerializeField, FormerlySerializedAs("noiseScale"), Range(5f, 100f)]
	[Tooltip("Width of the biggest hills and valleys, in cells. Bigger = broad, gentle terrain. Smaller = many steep little hills, which also makes terrain influence stronger.")]
	private float landformSize = 25f;

	[SerializeField, FormerlySerializedAs("octaves"), Range(1, 5)]
	[Tooltip("Layers of smaller bumps on top of the main landforms. 1 = smooth shapes only. Above 5 adds nothing visible at this grid size.")]
	private int surfaceDetailLayers = 4;

	[SerializeField, FormerlySerializedAs("persistence"), Range(0f, 1f)]
	[Tooltip("How strong the small bumps are compared to the big landforms. Low = smooth, high = craggy. Higher roughness also makes terrain influence stronger.")]
	private float surfaceRoughness = 0.5f;

	[SerializeField, FormerlySerializedAs("flatTerrain")]
	[Tooltip("A/B baseline: forces all elevation to 0. With flat terrain, the simulation behaves exactly as if Terrain Influence were 0.")]
	private bool flatTerrainBaseline;

	[Header("Terrain Propagation (Phase 2 experiment)")]
	[SerializeField, Range(0f, 1f)] private float terrainInfluence = 0.1f;

	// Scales raw elevation differences (typically small between adjacent
	// cells) into a usable weight range. Fixed; not a tuning parameter.
	private const float SlopeGain = 10f;

	[Header("Input")]
	[SerializeField] private int brushRadius = 3;

	[Header("References")]
	[SerializeField] private CellVisual cellPrefab;
	[SerializeField] private HUDController hudController;
	[SerializeField] private BuildTool currentTool = BuildTool.Stabilizer;

	private Cell[,] grid;
	private CellVisual[,] visuals;
	private float totalDesync;
	private float previousTotalDesync;
	private bool placementMode = true;

	private float simulationTime;
	private float activeStabilization;

	private readonly List<Emitter> emitters = new();
	private readonly List<ContainmentNode> containmentNodes = new();
	private readonly List<PlacedStructure> structures = new();

	// Sandbox removal (Emitters and structures) picks the nearest target within this distance.
	private const float SandboxRemoveRadius = 5f;

	// Prototype visuals for structures and the placement ghost.
	private SpriteRenderer markerTemplate;
	private SpriteRenderer placementGhost;

	private static readonly Color GhostValidColor = new(0.30f, 1.00f, 0.30f, 0.60f);
	private static readonly Color GhostInactiveColor = new(1.00f, 0.85f, 0.20f, 0.60f);
	private static readonly Color GhostInvalidColor = new(1.00f, 0.20f, 0.20f, 0.60f);

	private static readonly Color CoreMarkerColor = new(1.00f, 1.00f, 1.00f, 1.00f);
	private static readonly Color StabilizerActiveMarkerColor = new(0.60f, 1.00f, 0.60f, 1.00f);
	private static readonly Color NullifierActiveMarkerColor = new(0.75f, 0.50f, 1.00f, 1.00f);
	private static readonly Color InactiveMarkerColor = new(0.35f, 0.35f, 0.35f, 1.00f);

	// Experiment A: clamp telemetry (never read by the simulation)
	private float tickExcessClipped;
	private float peakExcess;
	private float totalExcessClipped;
	private int cellsAtCap;

	// Experiment B
	private string containmentStatusText = "";

	// Experiment D
	private int growingCells;
	private int stableCells;
	private int recedingCells;
	private float maxGrowth;
	private float maxLoss;
	private int frontierCells;
	private readonly Queue<Vector2Int> frontWindow = new(); // x = advanced, y = retreated
	private int frontAdvancedSum;
	private int frontRetreatedSum;

	// HUD cleanup: world Desync change per tick
	private double previousWorldDesync;
	private bool hasPreviousWorldDesync;
	private float worldDesyncDelta;

	private static readonly Color StabilizerWarningMarkerColor = new(1.00f, 0.80f, 0.25f, 1.00f);
	private static readonly Color StabilizerCriticalMarkerColor = new(1.00f, 0.50f, 0.10f, 1.00f);
	private static readonly Color StabilizerBreachedMarkerColor = new(1.00f, 0.15f, 0.15f, 1.00f);
	private Vector3 structureMarkerScale = Vector3.one;

	// Primary completion hook: true once every Emitter has been reduced to zero.
	public bool PrimaryObjectiveComplete { get; private set; }

	private float timer;
	private float realityIntegrity;

	public float RealityIntegrity => realityIntegrity;

	public int BrushRadius => brushRadius;

	public int ActiveDesyncCells { get; private set; }

	public int ActiveNodes { get; private set; }
	public int ContaminatedCells { get; private set; }

	public float SourceOutput { get; private set; }

	public float TotalDecayApplied { get; private set; }
	
	private int hoveredX = -1;
	private int hoveredY = -1;

	public float NetDeltaPerSecond
	{
		get
		{
			return totalDesync - previousTotalDesync;
		}
	}

	private void Start()
	{
		CreateGrid();
		GenerateTerrain();
		CreatePlacementGhost();

		emitters.Clear();

		if (startWithCenterSource)
		{
			TryAddEmitter(new Vector2Int(width / 2, height / 2));
		}

		foreach (var emitter in emitters)
		{
			grid[emitter.Position.x, emitter.Position.y].Desync =
				sourceStrength;
		}

		RenderGrid();
	}

	private void Update()
	{
		if (Keyboard.current.spaceKey.wasPressedThisFrame)
		{
			isPaused = !isPaused;
		}
		timer += Time.deltaTime;
		simulationTime += Time.deltaTime;

		if (!isPaused && timer >= tickRate)
		{
			timer = 0f;
			simulationTicks++;

			Simulate();
			UpdateNullifiers();
			CalculateIntegrity();
			CalculateActivity();
			UpdateContainmentStatus();
			RenderGrid();
		}

		HandleBrushSize();
		AnimateStressedMarkers();
		HandleMouseInput();
		if (hudController != null)
		{
			hudController.UpdateHUD(realityIntegrity, brushRadius, GetDebugStats());
		}
		if (isPaused &&	Keyboard.current.periodKey.wasPressedThisFrame)
		{
			simulationTicks++;
			Simulate();
			UpdateNullifiers();
			CalculateIntegrity();
			CalculateActivity();
			UpdateContainmentStatus();
			RenderGrid();
		}

		HandleToolSelection();
	}

	private void HandleToolSelection()
	{
		if (Keyboard.current.digit1Key.wasPressedThisFrame && sandboxDebugControls)
		{
			currentTool = BuildTool.Source;
		}

		if (Keyboard.current.digit2Key.wasPressedThisFrame)
		{
			currentTool = BuildTool.Stabilizer;
		}

		if (Keyboard.current.digit3Key.wasPressedThisFrame)
		{
			currentTool = BuildTool.Nullifier;
		}

		if (Keyboard.current.digit4Key.wasPressedThisFrame)
		{
			currentTool = BuildTool.Core;
		}

		if (Keyboard.current.escapeKey.wasPressedThisFrame)
		{
			currentTool = BuildTool.None;
		}
	}

	private void CreateGrid()
	{
		grid = new Cell[width, height];
		visuals = new CellVisual[width, height];

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				grid[x, y] = new Cell();

				CellVisual visual =
					Instantiate(
						cellPrefab,
						new Vector3(x, y, 0),
						Quaternion.identity,
						transform);

				visuals[x, y] = visual;
			}
		}
	}

	// Generates terrain elevation once at startup.
	// Cell.Elevation is rendered by CellVisual and used by Simulate()
	// for terrain-weighted Desync propagation (see terrainInfluence).
	private void GenerateTerrain()
	{
		TerrainGenerator.Generate(
			grid,
			mapSeed,
			landformSize,
			surfaceDetailLayers,
			surfaceRoughness,
			flatTerrainBaseline);

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				visuals[x, y].SetElevation(grid[x, y].Elevation);
			}
		}
	}

	private void Simulate()
	{
		previousTotalDesync = totalDesync;
		totalDesync = 0f;
		ActiveNodes = 0;
		activeStabilization = 0f;
		ContaminatedCells = 0;
		SourceOutput = 0f;
		TotalDecayApplied = 0f;
		tickExcessClipped = 0f;
		peakExcess = 0f;
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				grid[x, y].Stabilization = 0f;
			}
		}
		foreach (var node in containmentNodes)
		{
			// Gameplay gate: only Stabilizers connected to the network apply stabilization.
			if (!node.IsActive)
			{
				node.Condition = ContainmentCondition.Inactive;
				continue;
			}

			ApplyContainmentNode(node);
		}
		// Calculate next values
		for (int x = 1; x < width - 1; x++)
		{
			for (int y = 1; y < height - 1; y++)
			{
				float current = grid[x, y].Desync;

				// Phase 2 terrain experiment: terrain-weighted neighbor average.
				// Higher neighbors weigh more (desync flows downhill into this cell),
				// lower neighbors weigh less (desync resists climbing into this cell).
				// terrainInfluence = 0 -> every weight is exactly 1 -> plain average.
				float currentElevation = grid[x, y].Elevation;
				float terrainFactor = terrainInfluence * SlopeGain;

				float weightRight =
					Mathf.Max(0f, 1f + terrainFactor * (grid[x + 1, y].Elevation - currentElevation));

				float weightLeft =
					Mathf.Max(0f, 1f + terrainFactor * (grid[x - 1, y].Elevation - currentElevation));

				float weightUp =
					Mathf.Max(0f, 1f + terrainFactor * (grid[x, y + 1].Elevation - currentElevation));

				float weightDown =
					Mathf.Max(0f, 1f + terrainFactor * (grid[x, y - 1].Elevation - currentElevation));

				float weightSum =
					weightRight + weightLeft + weightUp + weightDown;

				float average;

				if (weightSum > 0f)
				{
					average =
					(
						grid[x + 1, y].Desync * weightRight +
						grid[x - 1, y].Desync * weightLeft +
						grid[x, y + 1].Desync * weightUp +
						grid[x, y - 1].Desync * weightDown
					) / weightSum;
				}
				else
				{
					// Fallback: all neighbors fully suppressed (sharp isolated peak).
					average =
					(
						grid[x + 1, y].Desync +
						grid[x - 1, y].Desync +
						grid[x, y + 1].Desync +
						grid[x, y - 1].Desync
					) / 4f;
				}

				float next =
					Mathf.Lerp(current, average, spreadStrength);				
				if (current > contaminationThreshold)
				{
					next += growthRate;
				}
				float stabilizationInfluence = 0f;

				for (int offsetX = -2; offsetX <= 2; offsetX++)
				{
					for (int offsetY = -2; offsetY <= 2; offsetY++)
					{
						int checkX = x + offsetX;
						int checkY = y + offsetY;

						if (checkX < 0 || checkX >= width)
							continue;

						if (checkY < 0 || checkY >= height)
							continue;

						stabilizationInfluence +=
							grid[checkX, checkY].Stabilization;
					}
				}

				float localDecay = decayRate + stabilizationInfluence * 0.01f;
				next -= localDecay;
				TotalDecayApplied += localDecay;
				float pressure = average - current;

				next += pressure * 0.05f;

				// Telemetry only: record how much the clamp discards (does not affect the result).
				float clippedExcess = Mathf.Max(0f, next - maxDesync);
				grid[x, y].ClampedExcess = clippedExcess;
				tickExcessClipped += clippedExcess;

				if (clippedExcess > peakExcess)
					peakExcess = clippedExcess;

				next = Mathf.Clamp(next, 0f, maxDesync);

				grid[x, y].NextDesync = next;
				grid[x, y].NetDelta = next - current;
			}
		}

		// Apply next values
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				grid[x, y].Desync =
					grid[x, y].NextDesync;
			}
		}

		totalExcessClipped += tickExcessClipped;

		// Permanent anomaly source (Emitters). Nullified Emitters no longer sustain Desync.
		foreach (var emitter in emitters)
		{
			if (emitter.IsNullified)
				continue;

			grid[emitter.Position.x, emitter.Position.y].Desync =
				sourceStrength;

			SourceOutput += sourceStrength;
		}

	}

	private void RenderGrid()
	{
		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				visuals[x, y].SetCell(grid[x, y].Desync, grid[x, y].Stabilization);
			}
		}
	}

	private void CalculateIntegrity()
	{
		ContaminatedCells = 0;
		ActiveDesyncCells = 0;

		int totalCells = width * height;

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				float desync =
					grid[x, y].Desync;

				if (desync > contaminationThreshold)
				{
					ContaminatedCells++;
				}

				if (desync > 0.01f)
				{
					ActiveDesyncCells++;
				}
			}
		}

		realityIntegrity =
			100f -
			(
				(float)ContaminatedCells /
				totalCells *
				100f
			);
	}

	private void HandleMouseInput()
	{
		if (Mouse.current == null)
			return;

		bool shiftHeld = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

		Vector2 mousePosition = Mouse.current.position.ReadValue();

		Vector3 worldPosition = Camera.main.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, 0f));
		int x = Mathf.RoundToInt(worldPosition.x);
		int y = Mathf.RoundToInt(worldPosition.y);
		hoveredX = x;
		hoveredY = y;

		bool inGrid = x >= 0 && x < width && y >= 0 && y < height;

		UpdatePlacementGhost(x, y, inGrid);

		if (!inGrid)
			return;

		// LEFT: Shift = place Emitter (sandbox) | tool selected = place structure | no tool = paint Desync (sandbox)
		if (Mouse.current.leftButton.wasPressedThisFrame)
		{
			if (shiftHeld)
			{
				if (sandboxDebugControls)
				{
					TryAddEmitter(new Vector2Int(x, y));
				}
			}
			else if (currentTool != BuildTool.None)
			{
				PlaceStructure(x, y);
			}
			else if (sandboxDebugControls)
			{
				PaintDesync(
					x,
					y,
					sourceStrength);
			}
		}

		// RIGHT: Shift = remove nearest Emitter (sandbox) | hold = erase Desync (sandbox)
		if (shiftHeld)
		{
			if (Mouse.current.rightButton.wasPressedThisFrame && sandboxDebugControls)
			{
				RemoveNearestEmitter(x, y);
			}
		}
		else if (Mouse.current.rightButton.isPressed && sandboxDebugControls)
		{
			PaintDesync(x, y, 0f);
		}

		// MIDDLE: place Stabilizer (same placement path as the build tool) | Shift = remove nearest structure
		if (Mouse.current.middleButton.wasPressedThisFrame)
		{
			if (shiftHeld)
			{
				RemoveNearestStructure(x, y);
			}
			else
			{
				PlaceStructure(x, y, BuildTool.Stabilizer);
			}
		}
	}

	private void PaintDesync(int centerX, int centerY, float value)
	{
		for (int x = -brushRadius; x <= brushRadius; x++)
		{
			for (int y = -brushRadius; y <= brushRadius; y++)
			{
				float distance =
					Mathf.Sqrt(
						x * x +
						y * y);

				if (distance > brushRadius)
					continue;

				int targetX =
					centerX + x;

				int targetY =
					centerY + y;

				if (targetX < 0 ||
					targetX >= width)
					continue;

				if (targetY < 0 ||
					targetY >= height)
					continue;

				grid[targetX, targetY].Desync =
					value;
			}
		}
	}

	private void HandleBrushSize()
	{
		if (Mouse.current == null)
			return;

		float scroll =
			Mouse.current.scroll.ReadValue().y;

		if (scroll > 0)
		{
			brushRadius++;
		}
		else if (scroll < 0)
		{
			brushRadius--;
		}

		brushRadius =
			Mathf.Clamp(
				brushRadius,
				1,
				25);
	}

	private ContainmentNode PlaceContainmentNode(int x, int y)
	{
		ContainmentNode node =
			new(new Vector2Int(x, y))
			{
				Radius = nodeRadius,
				Strength = nodeStrength
			};

		containmentNodes.Add(node);

		return node;
	}

	private void ApplyContainmentNode(ContainmentNode node)
	{
		bool nodeWasActive = false;
		int radius =
			Mathf.RoundToInt(node.Radius);

		// Condition telemetry (counts only; does not change stabilization).
		int footprintCells = 0;
		int contaminatedFootprintCells = 0;

		for (int x = -radius; x <= radius; x++)
		{
			for (int y = -radius; y <= radius; y++)
			{
				int targetX =
					node.Position.x + x;

				int targetY =
					node.Position.y + y;

				if (targetX < 0 ||
					targetX >= width)
					continue;

				if (targetY < 0 ||
					targetY >= height)
					continue;

				float distance =
					Mathf.Sqrt(
						x * x +
						y * y);

				if (distance > radius)
					continue;

				footprintCells++;

				if (grid[targetX, targetY].Desync > contaminationThreshold)
					contaminatedFootprintCells++;

				float falloff =
					1f -
					(distance / radius);

				float stabilizationAmount =	node.Strength * falloff;

				grid[targetX, targetY]
					.Stabilization +=
					stabilizationAmount;

				if (grid[targetX, targetY].Desync >
					contaminationThreshold)
				{
					nodeWasActive = true;

					activeStabilization +=
						stabilizationAmount;
				}
			}
		}
		if (nodeWasActive)
		{
			ActiveNodes++;
		}

		node.FootprintCells = footprintCells;
		node.ContaminatedFootprintCells = contaminatedFootprintCells;
		node.CenterContaminated =
			grid[node.Position.x, node.Position.y].Desync > contaminationThreshold;
		node.Condition = EvaluateNodeCondition(node);
	}

	// ----------------------------
	// CONTAINMENT CONDITION (experiment)
	// ----------------------------

	private ContainmentCondition EvaluateNodeCondition(ContainmentNode node)
	{
		node.Coverage =
			node.FootprintCells > 0
				? (float)node.ContaminatedFootprintCells / node.FootprintCells
				: 0f;

		if ((breachOnCenterContamination && node.CenterContaminated) ||
			node.Coverage >= breachCoverage)
			return ContainmentCondition.Breached;

		if (node.Coverage >= criticalCoverage)
			return ContainmentCondition.Critical;

		if (node.ContaminatedFootprintCells > 0 &&
			node.Coverage >= warningCoverage)
			return ContainmentCondition.Warning;

		return ContainmentCondition.Normal;
	}

	// Network status = worst condition among operational Stabilizers.
	private void UpdateContainmentStatus()
	{
		if (!networkContainmentStatus)
		{
			containmentStatusText = "";
			return;
		}

		int operational = 0;
		int warning = 0;
		int critical = 0;
		int breached = 0;

		foreach (var node in containmentNodes)
		{
			switch (node.Condition)
			{
				case ContainmentCondition.Normal: operational++; break;
				case ContainmentCondition.Warning: operational++; warning++; break;
				case ContainmentCondition.Critical: operational++; critical++; break;
				case ContainmentCondition.Breached: operational++; breached++; break;
			}
		}

		if (operational == 0)
		{
			containmentStatusText = "NO ACTIVE CONTAINMENT";
			return;
		}

		string worst =
			breached > 0 ? "BREACH" :
			critical > 0 ? "CRITICAL" :
			warning > 0 ? "WARNING" :
			"STABLE";

		List<string> parts = new();

		if (breached > 0) parts.Add($"{breached} breached");
		if (critical > 0) parts.Add($"{critical} critical");
		if (warning > 0) parts.Add($"{warning} warning");

		containmentStatusText =
			parts.Count > 0
				? $"{worst} ({string.Join(", ", parts)} of {operational} nodes)"
				: $"{worst} ({operational} nodes)";
	}

	// ----------------------------
	// SIMULATION ACTIVITY (experiment, "binoculars")
	// ----------------------------

	// Reads values the simulation already produced; changes nothing.
	private void CalculateActivity()
	{
		growingCells = 0;
		stableCells = 0;
		recedingCells = 0;
		maxGrowth = 0f;
		maxLoss = 0f;
		frontierCells = 0;
		cellsAtCap = 0;

		int advanced = 0;
		int retreated = 0;

		// Interior cells only: border cells are not simulated.
		for (int x = 1; x < width - 1; x++)
		{
			for (int y = 1; y < height - 1; y++)
			{
				Cell cell = grid[x, y];

				// Emitter cells are re-pinned every tick, so their NetDelta is not meaningful.
				if (cell.Structure == StructureType.Source)
					continue;

				float desync = cell.Desync;
				float delta = cell.NetDelta;
				bool contaminated = desync > contaminationThreshold;
				bool wasContaminated = desync - delta > contaminationThreshold;

				if (contaminated && !wasContaminated)
					advanced++;
				else if (!contaminated && wasContaminated)
					retreated++;

				if (desync >= maxDesync)
					cellsAtCap++;

				// Ignore cells that are not part of the Desync field.
				if (desync <= 0.01f && Mathf.Abs(delta) <= activityEpsilon)
					continue;

				if (delta > activityEpsilon)
					growingCells++;
				else if (delta < -activityEpsilon)
					recedingCells++;
				else
					stableCells++;

				if (delta > maxGrowth)
					maxGrowth = delta;

				if (delta < maxLoss)
					maxLoss = delta;

				if (contaminated &&
					(grid[x + 1, y].Desync <= contaminationThreshold ||
					 grid[x - 1, y].Desync <= contaminationThreshold ||
					 grid[x, y + 1].Desync <= contaminationThreshold ||
					 grid[x, y - 1].Desync <= contaminationThreshold))
				{
					frontierCells++;
				}
			}
		}

		frontWindow.Enqueue(new Vector2Int(advanced, retreated));
		frontAdvancedSum += advanced;
		frontRetreatedSum += retreated;

		while (frontWindow.Count > activityWindowTicks)
		{
			Vector2Int old = frontWindow.Dequeue();
			frontAdvancedSum -= old.x;
			frontRetreatedSum -= old.y;
		}

		// World Desync change since the previous tick (includes any sandbox paint/erase in between).
		double worldDesync = 0d;

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				worldDesync += grid[x, y].Desync;
			}
		}

		worldDesyncDelta = hasPreviousWorldDesync ? (float)(worldDesync - previousWorldDesync) : 0f;
		previousWorldDesync = worldDesync;
		hasPreviousWorldDesync = true;
	}

	private void RemoveNearestStructure(int x, int y)
	{
		Vector2Int clickPos =
			new(x, y);

		float closestDistance =
			float.MaxValue;

		PlacedStructure structureToRemove =
			null;

		foreach (var structure in structures)
		{
			float distance =
				Vector2Int.Distance(
					clickPos,
					structure.Position);

			if (distance < closestDistance)
			{
				closestDistance = distance;
				structureToRemove = structure;
			}
		}

		if (structureToRemove == null ||
			closestDistance > SandboxRemoveRadius)
			return;

		if (structureToRemove.Node != null)
		{
			containmentNodes.Remove(structureToRemove.Node);
		}

		if (structureToRemove.Marker != null)
		{
			Destroy(structureToRemove.Marker.gameObject);
		}

		grid[structureToRemove.Position.x, structureToRemove.Position.y].Structure =
			StructureType.None;

		structures.Remove(structureToRemove);

		RefreshNetwork();
	}

	// Sandbox only (caller checks sandboxDebugControls).
	private void RemoveNearestEmitter(int x, int y)
	{
		Vector2Int clickPos =
			new(x, y);

		float closestDistance =
			float.MaxValue;

		Emitter emitterToRemove =
			null;

		foreach (var emitter in emitters)
		{
			float distance =
				Vector2Int.Distance(
					clickPos,
					emitter.Position);

			if (distance < closestDistance)
			{
				closestDistance = distance;
				emitterToRemove = emitter;
			}
		}

		if (emitterToRemove == null ||
			closestDistance > SandboxRemoveRadius)
			return;

		grid[emitterToRemove.Position.x, emitterToRemove.Position.y].Structure =
			StructureType.None;

		emitters.Remove(emitterToRemove);

		RefreshStructureMarkers();
	}

	private bool TryAddEmitter(Vector2Int position)
	{
		if (!CanPlaceStructure(position.x, position.y))
			return false;

		emitters.Add(new Emitter(position, emitterMaxIntegrity));

		grid[position.x, position.y].Structure =
			StructureType.Source;

		RefreshStructureMarkers();

		return true;
	}

	private DebugStats GetDebugStats()
	{
		int activeEmitters = 0;
		float integritySum = 0f;
		float maxIntegritySum = 0f;

		foreach (var emitter in emitters)
		{
			if (!emitter.IsNullified)
				activeEmitters++;

			integritySum += emitter.Integrity;
			maxIntegritySum += emitter.MaxIntegrity;
		}

		int operationalNodes = 0;

		foreach (var node in containmentNodes)
		{
			if (node.IsActive)
				operationalNodes++;
		}

		DebugStats stats =
			new()
			{
				anomalyCount = activeEmitters,
				totalEmitters = emitters.Count,
				emitterIntegrityPercent = maxIntegritySum > 0f ? integritySum / maxIntegritySum * 100f : 0f,
				containmentNodeCount = containmentNodes.Count,
				operationalNodeCount = operationalNodes,
				currentToolName = currentTool.ToString(),
				cellsAtCap = cellsAtCap,
				peakExcess = peakExcess,
				tickExcessClipped = tickExcessClipped,
				totalExcessClipped = totalExcessClipped,
				containmentStatusText = containmentStatusText,
				growingCells = growingCells,
				stableCells = stableCells,
				recedingCells = recedingCells,
				maxGrowth = maxGrowth,
				maxLoss = maxLoss,
				frontierCells = frontierCells,
				frontAdvanced = frontAdvancedSum,
				frontRetreated = frontRetreatedSum,
				activityWindowTicks = activityWindowTicks,
				worldDesyncDelta = worldDesyncDelta
			};

		float totalDesync = 0f;
		float peakDesync = 0f;

		for (int x = 0; x < width; x++)
		{
			for (int y = 0; y < height; y++)
			{
				float value =
					grid[x, y].Desync;

				totalDesync += value;

				if (value > peakDesync)
					peakDesync = value;
			}
		}

		stats.totalDesync =	totalDesync;

		stats.peakDesync = peakDesync;

		stats.totalStabilization = operationalNodes * nodeStrength;
		stats.activeNodes =	ActiveNodes;

		stats.activeStabilization =	activeStabilization;

		stats.netDelta = totalDesync - previousTotalDesync;

		stats.simulationTime = simulationTime;
		stats.sourceOutput = SourceOutput;

		stats.totalDecayApplied = TotalDecayApplied;

		stats.pressureRatio = stats.activeStabilization > 0	? totalDesync / stats.activeStabilization : totalDesync;

		stats.contaminatedCells = ContaminatedCells;

		stats.activeDesyncCells = ActiveDesyncCells;

		if (hoveredX >= 0 && hoveredX < width && hoveredY >= 0 && hoveredY < height)
		{
			stats.mouseX = hoveredX;
			stats.mouseY = hoveredY;

			stats.hoveredDesync = grid[hoveredX, hoveredY].Desync;

			stats.hoveredStabilization = grid[hoveredX, hoveredY].Stabilization;
			stats.hoveredNetDelta =	grid[hoveredX, hoveredY].NetDelta;
			stats.hoveredElevation = grid[hoveredX, hoveredY].Elevation;
			stats.hoveredExcess = grid[hoveredX, hoveredY].ClampedExcess;
		}
		stats.isPaused = isPaused;

		stats.simulationTicks = simulationTicks;
		return stats;
	}

	private bool CanPlaceStructure(int x, int y)
	{
		if (x < 0 || x >= width)
			return false;

		if (y < 0 || y >= height)
			return false;

		return grid[x, y].Structure == StructureType.None;
	}

	private bool CanPlaceStructure(int x, int y, BuildTool tool)
	{
		if (tool == BuildTool.None)
			return false;

		if (tool == BuildTool.Source && !sandboxDebugControls)
			return false;

		if (!CanPlaceStructure(x, y))
			return false;

		// One Operational Core.
		if (tool == BuildTool.Core && HasCore())
			return false;

		return true;
	}

	private void PlaceStructure(int x,	int y)
	{
		PlaceStructure(x, y, currentTool);
	}

	private void PlaceStructure(int x, int y, BuildTool tool)
	{
		if (!CanPlaceStructure(x, y, tool))
			return;

		Vector2Int position = new(x, y);

		switch (tool)
		{
			case BuildTool.Source:

				TryAddEmitter(position);

				break;

			case BuildTool.Stabilizer:

				AddStructure(
					new PlacedStructure(StructureType.Stabilizer, position)
					{
						Node = PlaceContainmentNode(x, y)
					});

				break;

			case BuildTool.Nullifier:

				AddStructure(
					new PlacedStructure(StructureType.Nullifier, position));

				break;

			case BuildTool.Core:

				AddStructure(
					new PlacedStructure(StructureType.Core, position));

				break;
		}
	}

	private void AddStructure(PlacedStructure structure)
	{
		structures.Add(structure);

		grid[structure.Position.x, structure.Position.y].Structure =
			structure.Type;

		structure.Marker = CreateSpriteMarker(structure.Position, structure.Type + "Marker", 0.6f, 10);

		RefreshNetwork();
	}

	private bool HasCore()
	{
		foreach (var structure in structures)
		{
			if (structure.Type == StructureType.Core)
				return true;
		}

		return false;
	}

	// ----------------------------
	// INFRASTRUCTURE CONNECTIVITY
	// ----------------------------

	private void RefreshNetwork()
	{
		InfrastructureNetwork.Recalculate(structures, connectionRange);

		// Stabilizer operational requirement: the stabilization simulation only
		// runs for nodes whose structure is connected to the network.
		foreach (var structure in structures)
		{
			if (structure.Node != null)
			{
				structure.Node.IsActive = structure.IsConnected;
			}
		}

		RefreshStructureMarkers();
	}

	// ----------------------------
	// NULLIFIER
	// ----------------------------

	private void UpdateNullifiers()
	{
		foreach (var structure in structures)
		{
			if (structure.Type != StructureType.Nullifier || !structure.IsConnected)
				continue;

			Emitter target = FindNullifierTarget(structure.Position);

			if (target == null)
				continue;

			target.Integrity =
				Mathf.Max(0f, target.Integrity - nullifierDrainPerTick);
		}

		UpdateObjectiveState();
		RefreshStructureMarkers();
	}

	// Nearest active Emitter within nullifierRange, or null.
	private Emitter FindNullifierTarget(Vector2Int position)
	{
		Emitter best = null;
		float bestDistanceSq = nullifierRange * nullifierRange;

		foreach (var emitter in emitters)
		{
			if (emitter.IsNullified)
				continue;

			float distanceSq = (emitter.Position - position).sqrMagnitude;

			if (distanceSq <= bestDistanceSq)
			{
				bestDistanceSq = distanceSq;
				best = emitter;
			}
		}

		return best;
	}

	private void UpdateObjectiveState()
	{
		bool complete =
			emitters.Count > 0 &&
			emitters.TrueForAll(emitter => emitter.IsNullified);

		if (complete && !PrimaryObjectiveComplete)
		{
			Debug.Log("Primary objective complete: all Emitters reduced to zero.");
		}

		PrimaryObjectiveComplete = complete;
	}

	// ----------------------------
	// STRUCTURE / GHOST VISUALS (prototype)
	// ----------------------------

	private void CreatePlacementGhost()
	{
		markerTemplate = cellPrefab.GetComponentInChildren<SpriteRenderer>(true);

		if (markerTemplate == null)
		{
			Debug.LogWarning("GridManager: Cell prefab has no SpriteRenderer; structure markers and placement ghost are disabled.");
			return;
		}

		structureMarkerScale = markerTemplate.transform.lossyScale * 0.6f;

		placementGhost = CreateSpriteMarker(Vector2Int.zero, "PlacementGhost", 1f, 20);
		placementGhost.enabled = false;
	}

	private SpriteRenderer CreateSpriteMarker(Vector2Int position, string markerName, float scale, int sortingOffset)
	{
		if (markerTemplate == null)
			return null;

		GameObject markerObject = new(markerName);
		markerObject.transform.SetParent(transform, false);
		markerObject.transform.position = new Vector3(position.x, position.y, 0f);
		markerObject.transform.localScale = markerTemplate.transform.lossyScale * scale;

		SpriteRenderer marker = markerObject.AddComponent<SpriteRenderer>();
		marker.sprite = markerTemplate.sprite;
		marker.sharedMaterial = markerTemplate.sharedMaterial;
		marker.sortingLayerID = markerTemplate.sortingLayerID;
		marker.sortingOrder = markerTemplate.sortingOrder + sortingOffset;

		return marker;
	}

	private void UpdatePlacementGhost(int x, int y, bool inGrid)
	{
		if (placementGhost == null)
			return;

		bool show =
			inGrid &&
			currentTool != BuildTool.None &&
			!(currentTool == BuildTool.Source && !sandboxDebugControls);

		placementGhost.enabled = show;

		if (!show)
			return;

		placementGhost.transform.position = new Vector3(x, y, 0f);
		placementGhost.color = GetGhostColor(x, y, currentTool);
	}

	// Green = valid and will be operational. Yellow = valid but will be inactive. Red = invalid.
	private Color GetGhostColor(int x, int y, BuildTool tool)
	{
		if (!CanPlaceStructure(x, y, tool))
			return GhostInvalidColor;

		Vector2Int position = new(x, y);

		switch (tool)
		{
			case BuildTool.Stabilizer:

				return InfrastructureNetwork.WouldConnect(structures, position, StructureType.Stabilizer, connectionRange)
					? GhostValidColor
					: GhostInactiveColor;

			case BuildTool.Nullifier:

				return InfrastructureNetwork.WouldConnect(structures, position, StructureType.Nullifier, connectionRange) &&
					FindNullifierTarget(position) != null
					? GhostValidColor
					: GhostInactiveColor;

			default:

				return GhostValidColor;
		}
	}

	private void RefreshStructureMarkers()
	{
		foreach (var structure in structures)
		{
			if (structure.Marker != null)
			{
				structure.Marker.color = GetMarkerColor(structure);
			}
		}
	}

	private Color GetMarkerColor(PlacedStructure structure)
	{
		switch (structure.Type)
		{
			case StructureType.Core:
				return CoreMarkerColor;

			case StructureType.Stabilizer:

				if (!structure.IsConnected)
					return InactiveMarkerColor;

				switch (structure.Node != null ? structure.Node.Condition : ContainmentCondition.Normal)
				{
					case ContainmentCondition.Warning: return StabilizerWarningMarkerColor;
					case ContainmentCondition.Critical: return StabilizerCriticalMarkerColor;
					case ContainmentCondition.Breached: return StabilizerBreachedMarkerColor;
					default: return StabilizerActiveMarkerColor;
				}

			case StructureType.Nullifier:
				return structure.IsConnected && FindNullifierTarget(structure.Position) != null
					? NullifierActiveMarkerColor
					: InactiveMarkerColor;

			default:
				return InactiveMarkerColor;
		}
	}

	// Gentle scale pulse for CRITICAL / BREACHED Stabilizers (no color flashing).
	private void AnimateStressedMarkers()
	{
		foreach (var structure in structures)
		{
			if (structure.Marker == null || structure.Type != StructureType.Stabilizer)
				continue;

			ContainmentCondition condition =
				structure.IsConnected && structure.Node != null
					? structure.Node.Condition
					: ContainmentCondition.Inactive;

			float pulse = 0f;

			if (pulseStressedMarkers)
			{
				if (condition == ContainmentCondition.Breached)
					pulse = 0.25f * Mathf.Sin(Time.time * Mathf.PI * 2f * 1.5f);
				else if (condition == ContainmentCondition.Critical)
					pulse = 0.15f * Mathf.Sin(Time.time * Mathf.PI * 2f * 0.75f);
			}

			structure.Marker.transform.localScale = structureMarkerScale * (1f + pulse);
		}
	}
}


	/*private void PaintStabilization(int centerX, int centerY, float value)
	{
		for (int x = -brushRadius; x <= brushRadius; x++)
		{
			for (int y = -brushRadius; y <= brushRadius; y++)
			{
				float distance =
					Mathf.Sqrt(
						x * x +
						y * y);

				if (distance > brushRadius)
					continue;

				int targetX = centerX + x;
				int targetY = centerY + y;

				if (targetX < 0 ||
					targetX >= width)
					continue;

				if (targetY < 0 ||
					targetY >= height)
					continue;

				grid[targetX, targetY].Stabilization =
					value;
			}
		}
	}*/