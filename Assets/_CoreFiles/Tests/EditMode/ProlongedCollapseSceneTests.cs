using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Required future acceptance scenario (Docs/SYNC_INFRASTRUCTURE.md §8.5, Director 2026-11-25):
	// a prolonged network collapse in which the original connection gap itself becomes contaminated, followed by
	// progressive Reconstruction from inside the Sync Core's emergency radius (S1). It passes only if recovery is
	// achievable: the network reconnects the stranded Stabilizers AND territory is reclaimed.
	//
	// Increment 2A (Director, 2026-11-26): re-run at the new defaults in the increment 2A playtest scene, exactly
	// as saved (Sandbox values, Sync Charges + Reconstruction + U2 on, generation 8, construction share 25 %,
	// S4 defeat at 30 %, Emitter placed at the grid centre (50,50) at start), so the Director's manual playtest and
	// this test start identically. The increment 2 run (Sandbox, Emitter at (50,52), generation 4) is recorded in
	// Docs/SYNC_INFRASTRUCTURE.md §8.5.2. The only in-memory change is the generation-4 comparison run.
	// S4 stays on as in the scene: the test harness keeps ticking after a defeat, and the defeat tick is reported.
	// Measurements follow the research metric standard: the game's own threshold, with centre, footprint,
	// effectiveness and connectivity separately.
	public class ProlongedCollapseSceneTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		private const float Threshold = 5f; // Sandbox contaminationThreshold
		private const string ScenePath = "Assets/_CoreFiles/Scenes/Playtest_SyncSupply.unity";

		private static readonly Vector2Int EmitterPos = new(50, 50); // placed by the playtest scene at start
		private static readonly Vector2Int CorePos = new(50, 85);
		private static readonly Vector2Int Gap = new(50, 78);  // original S1: the connection that is severed
		private static readonly Vector2Int S2 = new(50, 71);
		private static readonly Vector2Int S3 = new(50, 64);
		private static readonly Vector2Int R1 = new(50, 82);   // 3 cells from the Core: inside the emergency radius
		private static readonly Vector2Int Step = new(50, 80); // competent player's intermediate step (5 from the Core)
		// Cells inside the 3-cell emergency radius, nearest the gap first (competent player's cluster).
		private static readonly Vector2Int[] ClusterCells = { new(50, 82), new(49, 83), new(51, 83), new(50, 83), new(48, 84), new(52, 84) };
		// The player then rebuilds the gap itself (50,78): 4 cells beyond R1, outside the emergency radius.

		private const int HoldTicks = 200;
		private const int MaxCollapseTicks = 8000;
		private const int ProlongTicks = 300;        // collapse continues after the gap is contaminated
		private const int MaxRebuildTicks = 4000;
		private const int AfterReconnectTicks = 600; // time allowed to reclaim territory once reconnected
		private const int SnapshotEvery = 250;

		private GridManager gm;
		private Cell[,] grid;
		private List<PlacedStructure> structures;
		private SyncInfrastructureSettings settings;

		internal class Result
		{
			public string Name;
			public StringBuilder Log = new();
			public int GapContaminatedAfter = -1;     // ticks after the cut
			public bool CoreZoneContaminated;
			public int R1CompletedAt = -1;            // ticks after the rebuild started
			public int GapCompletedAt = -1;
			public int ReconnectedAt = -1;
			public bool Reconnected;
			public bool ConnectedAtEnd;
			public int CorridorAtRebuildStart, CorridorAtEnd, CorridorAtReconnect;
			public int StrandedFootprintAtRebuildStart, StrandedFootprintAtEnd;
			public float S3SumAtReconnect, S3SumAtEnd;
			public bool GapCleanAtEnd;
			public float MinEffectivenessAfterReconnect = 1f;
			public int IntegrityBelowS4ThresholdAt = -1; // rebuild tick at which Reality Integrity first fell below the S4 threshold
			public float S4Threshold;
			public int DefeatAtTick = -1;                // ticks from the start of the run at which S4 latched MISSION FAILED
			public string DefeatPhase = "";
			public float IntegrityAtReconnect;
			public bool S3CompletedInHold;
			public bool S2ReclaimedAtEnd, S3ReclaimedAtEnd; // centre clean and fully supplied
			public string Fingerprint;

			public bool TerritoryReclaimed =>
				Reconnected && StrandedFootprintAtEnd < StrandedFootprintAtRebuildStart && CorridorAtEnd < CorridorAtRebuildStart;

			public bool RecoveryAchieved => Reconnected && ConnectedAtEnd && TerritoryReclaimed && GapCleanAtEnd;
		}

		// ---------------------------------------------------------------
		// Scenario
		// ---------------------------------------------------------------

		private int runTicks;
		private string phase = "";
		private Result current;

		// Opens the playtest scene as saved. generationOverride (comparison only) changes generation in memory.
		private void Open(int? generationOverride)
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm);
			settings = (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");
			Assert.IsTrue(settings.enabled && settings.stabilizerSupplyEnabled, "Playtest scene: Sync + U2 on.");
			if (generationOverride.HasValue)
				settings.coreGenerationPerTick = generationOverride.Value;
			SandboxSmokeHarness.Invoke(gm, "Start");
			grid = (Cell[,])SandboxSmokeHarness.Field(gm, "grid");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
			runTicks = 0;
		}

		private void Place(Vector2Int p, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { p.x, p.y, tool });

		private void Remove(Vector2Int p)
		{
			At(p).Marker = null; // Destroy() is not allowed in edit mode; the removal logic is the game's
			SandboxSmokeHarness.Invoke(gm, "RemoveNearestStructure", p.x, p.y);
			Assert.IsNull(At(p));
		}

		private PlacedStructure At(Vector2Int p) => structures.Find(s => s.Position == p);

		private void Tick()
		{
			SandboxSmokeHarness.Tick(gm);
			runTicks++;
			if (current != null && current.DefeatAtTick < 0 && gm.MissionDefeated)
			{
				current.DefeatAtTick = runTicks;
				current.DefeatPhase = phase;
				current.Log.AppendLine($"-- S4: MISSION FAILED latched at run tick {runTicks} ({phase}), Reality Integrity {gm.RealityIntegrity:F2}");
			}
		}

		private bool Contaminated(Vector2Int p) => grid[p.x, p.y].Desync > Threshold;

		private int FootprintContaminated(Vector2Int c, int radius = 5)
		{
			int n = 0;
			for (int dx = -radius; dx <= radius; dx++)
			for (int dy = -radius; dy <= radius; dy++)
			{
				if (Mathf.Sqrt(dx * dx + dy * dy) > radius) continue;
				if (grid[c.x + dx, c.y + dy].Desync > Threshold) n++;
			}
			return n;
		}

		private float FootprintSum(Vector2Int c, int radius = 5)
		{
			float sum = 0f;
			for (int dx = -radius; dx <= radius; dx++)
			for (int dy = -radius; dy <= radius; dy++)
			{
				if (Mathf.Sqrt(dx * dx + dy * dy) > radius) continue;
				sum += grid[c.x + dx, c.y + dy].Desync;
			}
			return sum;
		}

		// Territory: contaminated cells in the corridor between the Sync Core and the stranded Stabilizers.
		private int Corridor()
		{
			int n = 0;
			for (int x = 44; x <= 56; x++)
			for (int y = 59; y <= 88; y++)
				if (grid[x, y].Desync > Threshold) n++;
			return n;
		}

		private int TotalContaminated()
		{
			int n = 0;
			foreach (Cell c in grid)
				if (c.Desync > Threshold) n++;
			return n;
		}

		private string Describe(string name, Vector2Int p)
		{
			PlacedStructure s = At(p);
			string where = $"{name}({p.x},{p.y}) centre {grid[p.x, p.y].Desync:F1}{(Contaminated(p) ? "*" : "")} fp {FootprintContaminated(p)}/81 sum {FootprintSum(p):F0}";
			if (s == null) return where + " [empty]";
			if (s.UnderConstruction) return where + $" [blueprint {s.Construction} {s.ChargesDelivered}/{s.ChargesRequired} conn {s.IsConnected}]";
			return where + $" [eff {s.SupplyEffectiveness:P0} {s.Supply} store {s.StoredCharges} conn {s.IsConnected} {s.Node?.Condition}]";
		}

		private void Snapshot(Result r, string phase, int t)
		{
			SyncDiagnostics d = gm.SyncDiagnostics;
			r.Log.AppendLine(
				$"[{phase} t+{t}] gen {d.Generated} | constr {d.ConstructionDelivered}/{d.ConstructionDemand} | supply {d.SupplyDelivered}/{d.SupplyDemand} | unused {d.Unused} | " +
				$"stab sup/store/red/starved {d.StabilizersSupplied}/{d.StabilizersOnStore}/{d.StabilizersReduced}/{d.StabilizersStarved} stored {d.StoredCharges} | " +
				$"corridor {Corridor()} total {TotalContaminated()} integrity {gm.RealityIntegrity:F2}");
			r.Log.AppendLine("    " + Describe("R1", R1) + " | " + Describe("STEP", Step) + " | " + Describe("GAP", Gap));
			r.Log.AppendLine("    " + Describe("S2", S2) + " | " + Describe("S3", S3) + $" | core-zone centre {grid[CorePos.x, CorePos.y].Desync:F1}");
		}

		// Supply consumed this tick across Stabilizers (effectiveness x upkeep), for the consumption total.
		private float ConsumedThisTick()
		{
			float c = 0f;
			foreach (PlacedStructure s in structures)
				if (s.Type == StructureType.Stabilizer && !s.UnderConstruction)
					c += s.SupplyEffectiveness * settings.stabilizerUpkeepPerTick;
			return c;
		}

		// The rearmost player Stabilizer: farthest from the gap (largest y), then farthest from the line, then larger x.
		private static Vector2Int Rearmost(List<Vector2Int> built)
		{
			Vector2Int rear = built[0];
			foreach (Vector2Int p in built)
			{
				int c = p.y.CompareTo(rear.y);
				if (c == 0) c = Mathf.Abs(p.x - 50).CompareTo(Mathf.Abs(rear.x - 50));
				if (c == 0) c = p.x.CompareTo(rear.x);
				if (c > 0) rear = p;
			}
			return rear;
		}

		internal Result Run(string name, int? generationOverride, bool competent)
		{
			var r = new Result { Name = name };
			current = r;
			Open(generationOverride);
			SyncInfrastructureSettings s = settings;
			r.S4Threshold = (float)SandboxSmokeHarness.Field(gm, "defeatIntegrityThreshold");
			bool defeatOn = (bool)SandboxSmokeHarness.Field(gm, "realityIntegrityDefeat");
			r.Log.AppendLine($"== {name}: generation {s.coreGenerationPerTick}/tick (floor {s.CoreGeneration()}), construction share {s.constructionMinimumShare:P0}, upkeep {s.stabilizerUpkeepPerTick}, store {s.stabilizerStoreCapacity}, " +
				$"intake {s.maxSupplyIntakePerTick}, foothold {s.footholdStabilizerCount} (nearest the Core), emergency radius {s.coreSafeZoneRadius}, Stabilizer cost {s.stabilizerConstructionCost} at {s.maxConstructionIntakePerTick}/tick; " +
				$"S4 {(defeatOn ? "on" : "off")} at {r.S4Threshold:F0} %");

			// 1. Supplied line holding a frontier. The Emitter is the playtest scene's own (grid centre).
			var emitters = (System.Collections.IList)SandboxSmokeHarness.Field(gm, "emitters");
			Assert.AreEqual(1, emitters.Count);
			Assert.AreEqual(EmitterPos, ((Emitter)emitters[0]).Position);
			phase = "hold";
			Place(CorePos, BuildTool.Core);
			Place(Gap, BuildTool.Stabilizer);
			Place(S2, BuildTool.Stabilizer);
			Place(S3, BuildTool.Stabilizer);
			for (int i = 0; i < HoldTicks; i++) Tick();
			Snapshot(r, "HELD", HoldTicks);
			r.S3CompletedInHold = !At(S3).UnderConstruction;
			if (!r.S3CompletedInHold)
				r.Log.AppendLine("-- NOTE: S3 is still a blueprint after the hold");

			// 2. The player removes the gap Stabilizer; the collapse runs until the gap itself is contaminated, then longer.
			phase = "collapse";
			Remove(Gap);
			int t = 0;
			while (!Contaminated(Gap) && t < MaxCollapseTicks)
			{
				Tick();
				t++;
				if (t % (SnapshotEvery * 4) == 0) Snapshot(r, "COLLAPSE", t);
			}
			r.GapContaminatedAfter = Contaminated(Gap) ? t : -1;
			r.Log.AppendLine(r.GapContaminatedAfter >= 0
				? $"-- gap (50,78) contaminated {t} ticks after the cut"
				: $"-- gap NOT contaminated after {MaxCollapseTicks} ticks");
			for (int i = 0; i < ProlongTicks; i++) { Tick(); t++; }
			r.CoreZoneContaminated = Contaminated(R1);
			Snapshot(r, "COLLAPSED", t);

			// 3. Progressive Reconstruction from the Sync Core's emergency radius.
			//    Naive player: one Stabilizer in the radius, then the gap itself once it is complete.
			//    Competent player: as many Stabilizers in the radius as the supply sustains at full strength
			//    (generation / upkeep), then short 2-cell steps toward the gap; after each step completes, the
			//    rearmost Stabilizer is removed to stay within supply; once reconnected, the helpers are removed
			//    so the supply goes to the gap link and the stranded Stabilizers. Only existing player actions.
			phase = "rebuild";
			r.CorridorAtRebuildStart = Corridor();
			r.StrandedFootprintAtRebuildStart = FootprintContaminated(S2) + FootprintContaminated(S3);
			int sustainable = Mathf.Max(1, s.CoreGeneration() / Mathf.Max(1, s.stabilizerUpkeepPerTick));
			var cluster = new List<Vector2Int>();
			var steps = new Queue<Vector2Int>();
			if (competent)
			{
				for (int i = 0; i < sustainable && i < ClusterCells.Length; i++) cluster.Add(ClusterCells[i]);
				steps.Enqueue(Step);
			}
			else
			{
				cluster.Add(R1);
			}
			steps.Enqueue(Gap);
			r.Log.AppendLine($"-- rebuild ({(competent ? "competent" : "naive")} player): sustainable Stabilizers at full strength {sustainable}; " +
				$"emergency-radius cluster {string.Join(" ", cluster)}; then {string.Join(" ", steps)}");

			var playerBuilt = new List<Vector2Int>();
			foreach (Vector2Int p in cluster)
			{
				Assert.IsTrue(SyncInfrastructure.InCoreSafeZone(structures, p, s), $"{p} is inside the emergency radius");
				Place(p, BuildTool.Stabilizer);
				playerBuilt.Add(p);
			}

			float generated = 0f, constructionDelivered = 0f, supplyDelivered = 0f, consumed = 0f;
			bool helpersRemoved = false;
			int afterReconnect = 0;
			int rt;
			for (rt = 1; rt <= MaxRebuildTicks; rt++)
			{
				Tick();
				SyncDiagnostics d = gm.SyncDiagnostics;
				generated += d.Generated;
				constructionDelivered += d.ConstructionDelivered;
				supplyDelivered += d.SupplyDelivered;
				consumed += ConsumedThisTick();
				if (r.IntegrityBelowS4ThresholdAt < 0 && gm.RealityIntegrity < r.S4Threshold)
					r.IntegrityBelowS4ThresholdAt = rt;

				bool allComplete = playerBuilt.TrueForAll(p => !At(p).UnderConstruction);
				if (r.R1CompletedAt < 0 && allComplete)
				{
					r.R1CompletedAt = rt;
					Snapshot(r, "CLUSTER COMPLETE", rt);
				}

				// The player extends the line once the previous step is complete.
				if (allComplete && !r.Reconnected)
				{
					while (competent && playerBuilt.Count > sustainable)
					{
						Vector2Int rear = Rearmost(playerBuilt);
						Remove(rear);
						playerBuilt.Remove(rear);
						r.Log.AppendLine($"-- t+{rt}: player removes the rearmost Stabilizer {rear} to stay within supply");
					}

					if (steps.Count > 0)
					{
						Vector2Int next = steps.Dequeue();
						Place(next, BuildTool.Stabilizer);
						playerBuilt.Add(next);
						r.Log.AppendLine($"-- t+{rt}: blueprint placed at {next}, Desync there {grid[next.x, next.y].Desync:F1}");
					}
				}

				PlacedStructure gap = At(Gap);
				if (gap != null && r.GapCompletedAt < 0 && !gap.UnderConstruction)
				{
					r.GapCompletedAt = rt;
					Snapshot(r, "GAP COMPLETE", rt);
				}

				bool connected = At(S2).IsConnected && At(S3).IsConnected;
				if (!r.Reconnected && connected)
				{
					r.Reconnected = true;
					r.ReconnectedAt = rt;
					r.CorridorAtReconnect = Corridor();
					r.S3SumAtReconnect = FootprintSum(S3);
					r.IntegrityAtReconnect = gm.RealityIntegrity;
					Snapshot(r, "RECONNECTED", rt);
				}

				if (r.Reconnected && competent && !helpersRemoved)
				{
					foreach (Vector2Int p in playerBuilt.ToArray())
					{
						if (p == Gap) continue;
						Remove(p);
						playerBuilt.Remove(p);
					}
					helpersRemoved = true;
					r.Log.AppendLine($"-- t+{rt}: player removes the helper Stabilizers; the network is Core -> gap -> S2 -> S3");
				}

				if (r.Reconnected)
				{
					foreach (Vector2Int p in new[] { Gap, S2, S3 })
						r.MinEffectivenessAfterReconnect = Mathf.Min(r.MinEffectivenessAfterReconnect, At(p).SupplyEffectiveness);
					if (++afterReconnect >= AfterReconnectTicks) break;
				}

				if (rt % SnapshotEvery == 0) Snapshot(r, "REBUILD", rt);
			}

			Snapshot(r, "END", Mathf.Min(rt, MaxRebuildTicks));
			r.ConnectedAtEnd = At(S2).IsConnected && At(S3).IsConnected;
			r.CorridorAtEnd = Corridor();
			r.StrandedFootprintAtEnd = FootprintContaminated(S2) + FootprintContaminated(S3);
			r.S3SumAtEnd = FootprintSum(S3);
			r.GapCleanAtEnd = !Contaminated(Gap);
			r.S2ReclaimedAtEnd = !Contaminated(S2) && !At(S2).UnderConstruction && At(S2).SupplyEffectiveness == 1f;
			r.S3ReclaimedAtEnd = !Contaminated(S3) && !At(S3).UnderConstruction && At(S3).SupplyEffectiveness == 1f;

			r.Log.AppendLine($"-- rebuild totals: generated {generated}, delivered to construction {constructionDelivered}, delivered to supply {supplyDelivered}, " +
				$"upkeep consumed {consumed:F0}, unused {generated - constructionDelivered - supplyDelivered}");
			r.Log.AppendLine($"-- R1 complete t+{r.R1CompletedAt}, gap complete t+{r.GapCompletedAt}, reconnected t+{r.ReconnectedAt}; " +
				$"corridor contaminated {r.CorridorAtRebuildStart} -> {r.CorridorAtReconnect} (reconnect) -> {r.CorridorAtEnd}; " +
				$"S2+S3 footprints {r.StrandedFootprintAtRebuildStart} -> {r.StrandedFootprintAtEnd}; S3 sum {r.S3SumAtReconnect:F0} -> {r.S3SumAtEnd:F0}; " +
				$"gap clean at end {r.GapCleanAtEnd}; min effectiveness after reconnect {r.MinEffectivenessAfterReconnect:P0}");
			r.Log.AppendLine($"-- S2 reclaimed {r.S2ReclaimedAtEnd}, S3 reclaimed {r.S3ReclaimedAtEnd}; Reality Integrity {r.IntegrityAtReconnect:F2} at reconnection, {gm.RealityIntegrity:F2} at end; " +
				$"first below {r.S4Threshold:F0} % (S4 threshold) at rebuild t+{r.IntegrityBelowS4ThresholdAt}; S4 MISSION FAILED at run tick {r.DefeatAtTick} ({(r.DefeatAtTick < 0 ? "never" : r.DefeatPhase)}; the test keeps ticking to measure the rebuild)");
			r.Log.AppendLine($"-- RECOVERY ACHIEVED: {r.RecoveryAchieved} (reconnected {r.Reconnected}, territory reclaimed {r.TerritoryReclaimed}, gap clean {r.GapCleanAtEnd})");

			double gridSum = 0d;
			foreach (Cell c in grid) gridSum += c.Desync;
			r.Fingerprint = $"{gridSum:R}|{r.GapContaminatedAfter}|{r.R1CompletedAt}|{r.GapCompletedAt}|{r.ReconnectedAt}|{r.CorridorAtEnd}|{r.S3SumAtEnd:R}";
			return r;
		}

		// ---------------------------------------------------------------
		// Tests
		// ---------------------------------------------------------------

		private static void AssertSetup(Result r)
		{
			Assert.GreaterOrEqual(r.GapContaminatedAfter, 0, "Setup: the original connection gap became contaminated.");
			Assert.Greater(r.StrandedFootprintAtRebuildStart, 0, "Setup: the stranded Stabilizers' footprints are contaminated.");
		}

		// Diagnostic (no outcome assertion): one Stabilizer in the emergency radius, then straight to the gap 4 cells
		// beyond it. In increment 2 (generation 4) the gap blueprint never reached clean ground (finding F1, reach).
		[Test, Timeout(1800000)]
		public void ProlongedCollapse_2ADefaults_NaivePlayer_Diagnostic()
		{
			Result r = Run("2A DEFAULTS (playtest scene), NAIVE PLAYER", null, competent: false);
			TestContext.WriteLine(r.Log.ToString());
			AssertSetup(r);
		}

		// Acceptance at the increment 2A defaults: construction is never starved during the hold (F3 fixed), and the
		// competent rebuild recovers.
		[Test, Timeout(1800000)]
		public void ProlongedCollapse_2ADefaults_CompetentPlayer_Recovers()
		{
			Result r = Run("2A DEFAULTS (playtest scene), COMPETENT PLAYER", null, competent: true);
			TestContext.WriteLine(r.Log.ToString());
			AssertSetup(r);
			Assert.IsTrue(r.S3CompletedInHold, "F3: S3 completes during the hold while two Stabilizers stand.");
			Assert.IsTrue(r.Reconnected && r.ConnectedAtEnd, "The network reconnects the stranded Stabilizers.");
			Assert.IsTrue(r.GapCleanAtEnd, "The original gap is reclaimed.");
			Assert.IsTrue(r.TerritoryReclaimed, "Contamination in the corridor and the stranded footprints is pushed back.");
			Assert.IsTrue(r.RecoveryAchieved);
		}

		// Comparison (in memory only): the increment 2 generation of 4 with the 2A rules. F3 no longer starves
		// construction during the hold. Outcome otherwise reported, not asserted.
		[Test, Timeout(1800000)]
		public void ProlongedCollapse_Generation4Comparison_CompetentPlayer_ConstructionNotStarved()
		{
			Result r = Run("COMPARISON generation 4 with 2A rules, COMPETENT PLAYER", 4, competent: true);
			TestContext.WriteLine(r.Log.ToString());
			AssertSetup(r);
			Assert.IsTrue(r.S3CompletedInHold, "F3: S3 completes during the hold even at generation 4.");
		}

		[Test, Timeout(1800000)]
		public void ProlongedCollapse_IsDeterministic()
		{
			string a = Run("DETERMINISM A", null, competent: true).Fingerprint;
			string b = Run("DETERMINISM B", null, competent: true).Fingerprint;
			TestContext.WriteLine(a);
			Assert.AreEqual(a, b);
		}
	}
}
