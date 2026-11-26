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
	// Real GridManager, real Sandbox.unity simulation values, opt-in systems turned on IN MEMORY (never saved).
	// No tuning is changed to make it pass: the "provisional defaults" run uses the shipped
	// SyncInfrastructureSettings defaults; the "test fixture" run uses the generation value of the existing
	// severed-connection test (8 per tick), for comparison only. Measurements follow the research metric
	// standard: the game's own threshold, with centre, footprint, effectiveness and connectivity separately.
	public class ProlongedCollapseSceneTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		private const float Threshold = 5f; // Sandbox contaminationThreshold

		private static readonly Vector2Int EmitterPos = new(50, 52);
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
			public int IntegrityBelowS4ThresholdAt = -1; // rebuild tick at which Reality Integrity first fell below 50 %
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

		private void Open(SyncInfrastructureSettings s)
		{
			EditorSceneManager.OpenScene("Assets/_CoreFiles/Scenes/Sandbox.unity", OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm);
			settings = s;
			typeof(GridManager).GetField("syncInfrastructure", Any).SetValue(gm, settings);
			SandboxSmokeHarness.Invoke(gm, "Start");
			grid = (Cell[,])SandboxSmokeHarness.Field(gm, "grid");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
		}

		internal static SyncInfrastructureSettings ProvisionalDefaults() =>
			new() { enabled = true, stabilizerSupplyEnabled = true };

		internal static SyncInfrastructureSettings TestFixture()
		{
			SyncInfrastructureSettings s = ProvisionalDefaults();
			s.coreGenerationPerTick = 8;
			return s;
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

		private void Tick() => SandboxSmokeHarness.Tick(gm);

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

		internal Result Run(string name, SyncInfrastructureSettings s, bool competent)
		{
			var r = new Result { Name = name };
			Open(s);
			r.Log.AppendLine($"== {name}: generation {s.coreGenerationPerTick}/tick (floor {s.CoreGeneration()}), upkeep {s.stabilizerUpkeepPerTick}, store {s.stabilizerStoreCapacity}, " +
				$"intake {s.maxSupplyIntakePerTick}, foothold {s.footholdStabilizerCount}, emergency radius {s.coreSafeZoneRadius}, Stabilizer cost {s.stabilizerConstructionCost} at {s.maxConstructionIntakePerTick}/tick");

			// 1. Supplied line holding a frontier.
			Assert.IsTrue((bool)SandboxSmokeHarness.Invoke(gm, "TryAddEmitter", EmitterPos));
			Place(CorePos, BuildTool.Core);
			Place(Gap, BuildTool.Stabilizer);
			Place(S2, BuildTool.Stabilizer);
			Place(S3, BuildTool.Stabilizer);
			for (int i = 0; i < HoldTicks; i++) Tick();
			Snapshot(r, "HELD", HoldTicks);
			r.S3CompletedInHold = !At(S3).UnderConstruction;
			if (!r.S3CompletedInHold)
				r.Log.AppendLine("-- NOTE: S3 is still a blueprint after the hold: the foothold reservation (2 x upkeep 2 = 4) takes the whole generation, so construction receives nothing");

			// 2. The player removes the gap Stabilizer; the collapse runs until the gap itself is contaminated, then longer.
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
				if (r.IntegrityBelowS4ThresholdAt < 0 && gm.RealityIntegrity < 50f)
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
			r.Log.AppendLine($"-- S2 reclaimed {r.S2ReclaimedAtEnd}, S3 reclaimed {r.S3ReclaimedAtEnd}; Reality Integrity {r.IntegrityAtReconnect:F2} at reconnection, first below 50 % (provisional S4 threshold) at rebuild t+{r.IntegrityBelowS4ThresholdAt} (S4 off in this test)");
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
		// beyond it. Recorded 2026-11-25: the gap blueprint never reaches clean ground (one Stabilizer does not clean a
		// cell 4 cells away against the advancing front), with charges left unused, so supply is not the limit.
		[Test, Timeout(1800000)]
		public void ProlongedCollapse_ProvisionalDefaults_NaivePlayer_Diagnostic()
		{
			Result r = Run("PROVISIONAL DEFAULTS, NAIVE PLAYER", ProvisionalDefaults(), competent: false);
			TestContext.WriteLine(r.Log.ToString());
			AssertSetup(r);
		}

		// Acceptance at the shipped provisional defaults (generation 4). Generation sustains two Stabilizers at full
		// strength, so the network reconnects and the gap and S2 are reclaimed; S3 (never completed in this setup) is not.
		[Test, Timeout(1800000)]
		public void ProlongedCollapse_ProvisionalDefaults_CompetentPlayer_Reconnects_AndReclaimsWithinSupply()
		{
			Result r = Run("PROVISIONAL DEFAULTS, COMPETENT PLAYER", ProvisionalDefaults(), competent: true);
			TestContext.WriteLine(r.Log.ToString());
			AssertSetup(r);
			Assert.IsTrue(r.Reconnected && r.ConnectedAtEnd, "The network reconnects the stranded Stabilizers.");
			Assert.IsTrue(r.GapCleanAtEnd, "The original gap is reclaimed.");
			Assert.IsTrue(r.S2ReclaimedAtEnd, "S2 is reclaimed: centre clean, fully supplied.");
			Assert.IsTrue(r.TerritoryReclaimed, "Contamination in the corridor and the stranded footprints is pushed back.");
			Assert.IsTrue(r.RecoveryAchieved);
		}

		// Acceptance at the test-fixture generation (8): full recovery, including S3.
		[Test, Timeout(1800000)]
		public void ProlongedCollapse_TestFixtureGeneration_CompetentPlayer_FullyRecovers()
		{
			Result r = Run("TEST FIXTURE (generation 8), COMPETENT PLAYER", TestFixture(), competent: true);
			TestContext.WriteLine(r.Log.ToString());
			AssertSetup(r);
			Assert.IsTrue(r.RecoveryAchieved, "Reconnected, gap clean, territory reclaimed.");
			Assert.IsTrue(r.S2ReclaimedAtEnd, "S2 reclaimed.");
			Assert.IsTrue(r.S3ReclaimedAtEnd, "S3 reclaimed.");
			Assert.Less(r.S3SumAtEnd, r.S3SumAtReconnect, "Desync in S3's footprint is pushed back after reconnection.");
		}

		[Test, Timeout(1800000)]
		public void ProlongedCollapse_IsDeterministic()
		{
			string a = Run("DETERMINISM A", ProvisionalDefaults(), competent: true).Fingerprint;
			string b = Run("DETERMINISM B", ProvisionalDefaults(), competent: true).Fingerprint;
			TestContext.WriteLine(a);
			Assert.AreEqual(a, b);
		}
	}
}
