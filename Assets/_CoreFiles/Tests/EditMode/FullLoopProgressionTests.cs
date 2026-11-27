using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Full gameplay loop from the map rim (Director's overnight assignment, 2026-11-26, Work Package 2):
	// Observe -> Establish -> Defend -> Advance -> Neutralize, in the combined playtest scene exactly as saved
	// (2A + Nullifier reconfiguration, Emitter at (50,50) at start, shipped provisional tuning, S4 at 30 %).
	//
	// The existing combined tests build next to the Emitter, inside the Sync Core's S1 emergency radius. Here the
	// Sync Core goes near the map rim and a scripted player advances a Stabilizer chain toward the Emitter using only
	// legitimate player actions: placing a structure (the game's own PlaceStructure, so its placement rules apply)
	// and pressing N (the game's own TryInitiateReconfiguration). The player chooses cells from what the map shows
	// (contamination colour = the game's threshold, structure states = HUD/overlay); it never edits state. No tuning
	// is changed. The harness keeps ticking after S4 latches, so a defeat tick is recorded rather than ending the run.
	//
	// Strategies:
	// - Idle control: Sync Core + the two-Stabilizer foothold, then nothing.
	// - Direct advance: the next chain Stabilizer is placed as soon as the front one is complete.
	// - Defense-first: each new front is consolidated first (fully supplied, full store, no contamination in its
	//   footprint; at most HoldCap ticks) before the next step.
	// - Lean direct (best case): no separate two-Stabilizer foothold; the chain starts from the Sync Core itself with
	//   links a full connection range apart, so its first two links are the foothold and no upkeep is spent on a
	//   redundant relay.
	// The advancing strategies place a Nullifier on the cleanest buildable cell within Nullifier range of the Emitter
	// that a completed relay reaches, pause the chain while it is built, and press N as soon as it is Ready.
	// "Buildable" is the game's rule: clean ground, or inside the Sync Core's S1 emergency radius. Chain cells also
	// keep EdgeMargin cells from visible contamination, so a blueprint is not placed where it will be overrun.
	// The Core-distance sweep runs the best case with the Sync Core at increasing distances from the Emitter.
	// All tests are diagnostics that print their measurements (results: Docs/SYNC_INFRASTRUCTURE.md §9.6); they assert
	// only the idle control and determinism, so a future tuning change does not break them.
	public class FullLoopProgressionTests
	{
		private const string ScenePath = "Assets/_CoreFiles/Scenes/Playtest_Combined.unity";
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

		private static readonly Vector2Int EmitterPos = new(50, 50);
		private const int RimDistance = 44;       // Sync Core 44 cells from the Emitter: 5-6 cells in from the rim
		private const int MaxTicks = 3000;
		private const int HoldCap = 150;          // defense-first: longest consolidation per step
		private const int StallTicks = 30;        // a blueprint waiting this long for clean ground counts as stalled
		private const int MaxStalledBlueprints = 3;
		private const int EdgeMargin = 2;         // chain cells keep this many cells from visible contamination
		private const int TraceEvery = 100;

		internal enum Strategy { Idle, Direct, DefenseFirst, LeanDirect }

		private GridManager gm;
		private Cell[,] grid;
		private List<PlacedStructure> structures;
		private List<Emitter> emitters;
		private SyncInfrastructureSettings settings;
		private float connectionRange, nullifierRange, threshold;

		internal class Result
		{
			public string Name;
			public StringBuilder Log = new();
			public int CorePlaced = -1, FootholdBuilt = -1, NullifierPlaced = -1, NullifierBuilt = -1, Ready = -1, Initiated = -1, Reconfigured = -1, DefeatAt = -1;
			public int[] StageDone = { -1, -1, -1 };
			public int ChainBuilt;                 // completed advance Stabilizers (beyond the foothold)
			public int StabilizersPlaced, NullifiersPlaced;
			public int ClosestRelay = int.MaxValue; // completed relay's distance to the Emitter (cells, rounded down)
			public int ReachedNullifierRangeAt = -1; // a completed relay within connection + Nullifier range of the Emitter
			public float MinIntegrity = 100f, IntegrityAtReconfigured = -1f;
			public long Generated, ToConstruction, ToSupply, ToNullifier, Unused;
			public int SupplyShortTicks, NullifierShortTicks, StabilizerCleanGroundWaitTicks, NullifierCleanGroundWaitTicks;
			public int NoChainCandidateTicks, NoNullifierCandidateTicks, Interruptions;
			public int NullifierRangeLastCleanAt = -1;  // last tick with any clean cell within Nullifier range of the Emitter
			public string Limit = "";
			public string Fingerprint;
			public bool Won => Reconfigured > 0 && (DefeatAt < 0 || Reconfigured < DefeatAt);
		}

		// ---------------------------------------------------------------
		// Tests
		// ---------------------------------------------------------------

		[Test, Timeout(900000)]
		public void FullLoop_FromTheRim_DirectVsDefenseFirstVsIdle_Diagnostic()
		{
			var sb = new StringBuilder();
			var results = new List<Result>();

			foreach (Strategy s in new[] { Strategy.Idle, Strategy.Direct, Strategy.DefenseFirst, Strategy.LeanDirect })
			{
				Result r = Play(s, new Vector2Int(0, -1), observeTicks: 0);
				results.Add(r);
				sb.AppendLine(Summary(r));
				sb.AppendLine(r.Log.ToString());
			}

			TestContext.WriteLine(sb.ToString());

			Result idle = results[0];
			Assert.Greater(idle.FootholdBuilt, 0, "Idle control: the foothold is built from the rim.");
			Assert.Greater(idle.DefeatAt, 0, "Idle control: S4 ends the mission.");
			Assert.Less(idle.Reconfigured, 0);
		}

		// Sensitivity (diagnostic): the best case (lean direct advance) from the four rim directions (terrain biases
		// spread by elevation), and the F6 question: the same advance started later.
		[Test, Timeout(900000)]
		public void FullLoop_LeanAdvance_Directions_AndLateStarts_Diagnostic()
		{
			var sb = new StringBuilder();
			var dirs = new[] { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
			var names = new[] { "from the north rim", "from the south rim", "from the east rim", "from the west rim" };

			for (int i = 0; i < dirs.Length; i++)
			{
				Result r = Play(Strategy.LeanDirect, dirs[i], 0);
				r.Name += " " + names[i];
				sb.AppendLine(Summary(r));
			}

			foreach (int observe in new[] { 150, 300 })
			{
				Result r = Play(Strategy.LeanDirect, new Vector2Int(0, -1), observe);
				r.Name += $" from the north rim, first action at tick {observe}";
				sb.AppendLine(Summary(r));
			}

			TestContext.WriteLine(sb.ToString());
		}

		// How far from the Emitter can the Sync Core be and still win? The best case (lean direct advance, north)
		// with the Sync Core placed at increasing distances. Diagnostic: it records, it does not tune.
		[Test, Timeout(900000)]
		public void FullLoop_CoreDistanceSweep_Diagnostic()
		{
			var sb = new StringBuilder();
			int farthestWin = -1;

			foreach (int distance in new[] { 9, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 36, 38, 40, 42, 44 })
			{
				Result r = Play(Strategy.LeanDirect, new Vector2Int(0, -1), 0, distance);
				r.Name += $" Sync Core {distance} cells from the Emitter";
				if (r.Won) farthestWin = Mathf.Max(farthestWin, distance);
				sb.AppendLine(Summary(r));
				if (distance == 9 || distance == 22 || distance == 40 || distance == 44) sb.AppendLine(r.Log.ToString());
			}

			sb.Insert(0, $"Farthest Sync Core distance that won: {farthestWin} cells (-1 = none)\n");
			TestContext.WriteLine(sb.ToString());
		}

		[Test, Timeout(900000)]
		public void FullLoop_DirectAdvance_IsDeterministic()
		{
			string a = Play(Strategy.Direct, new Vector2Int(0, -1), 0).Fingerprint;
			string b = Play(Strategy.Direct, new Vector2Int(0, -1), 0).Fingerprint;
			Assert.AreEqual(a, b);
		}

		// ---------------------------------------------------------------
		// Scenario
		// ---------------------------------------------------------------

		private void Open()
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm);
			SandboxSmokeHarness.Invoke(gm, "Start");
			grid = (Cell[,])SandboxSmokeHarness.Field(gm, "grid");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
			emitters = (List<Emitter>)SandboxSmokeHarness.Field(gm, "emitters");
			settings = (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");
			connectionRange = (float)SandboxSmokeHarness.Field(gm, "connectionRange");
			nullifierRange = (float)SandboxSmokeHarness.Field(gm, "nullifierRange");
			threshold = (float)SandboxSmokeHarness.Field(gm, "contaminationThreshold");
			Assert.IsTrue(settings.ReconfigurationActive && settings.SupplyActive, "Combined scene as saved.");
			Assert.AreEqual(1, emitters.Count);
			Assert.AreEqual(EmitterPos, emitters[0].Position);
		}

		private void Place(Vector2Int p, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { p.x, p.y, tool });

		private PlacedStructure At(Vector2Int p) => structures.Find(s => s.Position == p);
		private bool Clean(Vector2Int p) => grid[p.x, p.y].Desync <= threshold;
		// A blueprint here can be built: clean ground, or inside the Sync Core's S1 emergency radius.
		private bool Buildable(Vector2Int p) => Clean(p) || SyncInfrastructure.InCoreSafeZone(structures, p, settings);
		private bool Free(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < grid.GetLength(0) && p.y < grid.GetLength(1) && grid[p.x, p.y].Structure == StructureType.None;
		private static int Sq(Vector2Int a, Vector2Int b) => (a - b).sqrMagnitude;
		private static bool IsRelay(PlacedStructure s) => InfrastructureNetwork.CanRelay(s) && s.IsConnected;

		// approach: unit step from the Sync Core toward the Emitter.
		// coreDistance: how far from the Emitter the Sync Core is placed (default: near the rim).
		private Result Play(Strategy strategy, Vector2Int approach, int observeTicks, int coreDistance = RimDistance)
		{
			Open();
			var r = new Result { Name = strategy.ToString() };
			Vector2Int lateral = new(Mathf.Abs(approach.y), Mathf.Abs(approach.x));
			Vector2Int core = EmitterPos - approach * coreDistance;
			// Lean direct places no separate foothold: its chain starts from the Sync Core, and the first two links
			// are the foothold (S2 picks the two nearest the Core).
			Vector2Int[] foothold = strategy == Strategy.LeanDirect
				? new Vector2Int[0]
				: new[] { core + approach * 4 + lateral * 2, core + approach * 4 - lateral * 2 };
			float connSq = connectionRange * connectionRange;
			float nullSq = nullifierRange * nullifierRange;
			float reachSq = (connectionRange + nullifierRange) * (connectionRange + nullifierRange);
			Emitter emitter = emitters[0];

			var chain = new List<Vector2Int>();
			var nullifiers = new List<Vector2Int>();
			var waitingSince = new Dictionary<Vector2Int, int>();
			PlacedStructure front = null;
			int frontSince = -1;
			int t = 0;

			void Log(string line) => r.Log.AppendLine($"  t{t}: {line}");

			// --- Observe ---
			while (t < observeTicks) { Step(); }

			// --- Establish ---
			Place(core, BuildTool.Core);
			r.CorePlaced = t;
			foreach (Vector2Int f in foothold) Place(f, BuildTool.Stabilizer);
			r.StabilizersPlaced += foothold.Length;
			Log($"Sync Core {core}, first Stabilizer(s) {string.Join(" ", foothold)} placed");

			while (t < observeTicks + MaxTicks && r.Reconfigured < 0)
			{
				Step();

				if (r.FootholdBuilt < 0 && (foothold.Length > 0
						? System.Array.TrueForAll(foothold, f => !At(f).UnderConstruction)
						: r.ChainBuilt >= 2))
				{
					r.FootholdBuilt = t;
					Log(foothold.Length == 0 ? "first two chain links (the foothold) built" : "foothold built");
				}

				if (strategy == Strategy.Idle || (strategy != Strategy.LeanDirect && r.FootholdBuilt < 0))
					continue;

				// The front: the completed, connected relay nearest the Emitter.
				PlacedStructure newFront = null;
				foreach (PlacedStructure s in structures)
					if (IsRelay(s) && (newFront == null || Sq(s.Position, EmitterPos) < Sq(newFront.Position, EmitterPos)))
						newFront = s;
				if (newFront != front) { front = newFront; frontSince = t; }

				int frontSq = Sq(front.Position, EmitterPos);
				r.ClosestRelay = Mathf.Min(r.ClosestRelay, Mathf.FloorToInt(Mathf.Sqrt(frontSq)));
				if (r.ReachedNullifierRangeAt < 0 && frontSq <= reachSq) { r.ReachedNullifierRangeAt = t; Log($"a relay {front.Position} reaches Nullifier range"); }

				// --- Neutralize: place a Nullifier where a completed relay reaches, within range of the Emitter ---
				bool nullifierPending = false;
				int stalledNullifiers = 0;
				foreach (Vector2Int n in nullifiers)
				{
					PlacedStructure ns = At(n);
					if (ns == null) continue;
					if (!ns.UnderConstruction) nullifierPending = true;
					else if (t - waitingSince.GetValueOrDefault(n, t) < StallTicks) nullifierPending = true;
					else stalledNullifiers++;
				}

				if (!nullifierPending && stalledNullifiers < MaxStalledBlueprints && nullifiers.Count < MaxStalledBlueprints + 1 && frontSq <= reachSq)
				{
					Vector2Int? best = BestNullifierCell(connSq, nullSq);
					if (best.HasValue)
					{
						Place(best.Value, BuildTool.Nullifier);
						nullifiers.Add(best.Value);
						r.NullifiersPlaced++;
						if (r.NullifierPlaced < 0) r.NullifierPlaced = t;
						Log($"Nullifier blueprint {best.Value} ({Mathf.Sqrt(Sq(best.Value, EmitterPos)):F1} from the Emitter, Desync {grid[best.Value.x, best.Value.y].Desync:F1})");
						nullifierPending = true;
					}
					else r.NoNullifierCandidateTicks++;
				}

				// --- Advance: the chain pauses while a Nullifier is being built (or stands), so it gets the charges ---
				if (!nullifierPending)
				{
					bool chainPending = false;
					int stalledChain = 0;
					foreach (Vector2Int c in chain)
					{
						PlacedStructure cs = At(c);
						if (cs == null || !cs.UnderConstruction) continue;
						if (t - waitingSince.GetValueOrDefault(c, t) < StallTicks) chainPending = true;
						else stalledChain++;
					}

					bool consolidated = strategy != Strategy.DefenseFirst || IsConsolidated(front) || t - frontSince >= HoldCap;

					if (!chainPending && stalledChain < MaxStalledBlueprints && consolidated)
					{
						Vector2Int? next = BestChainCell(front.Position, approach, lateral, connSq);
						if (next.HasValue)
						{
							Place(next.Value, BuildTool.Stabilizer);
							chain.Add(next.Value);
							r.StabilizersPlaced++;
							Log($"chain Stabilizer blueprint {next.Value} ({Mathf.Sqrt(Sq(next.Value, EmitterPos)):F1} from the Emitter){(strategy == Strategy.DefenseFirst ? $" after holding the front {t - frontSince} ticks" : "")}");
						}
						else r.NoChainCandidateTicks++;
					}
				}

				// Blueprint waiting bookkeeping (what the hover text shows: "waiting for clean ground").
				foreach (PlacedStructure s in structures)
				{
					if (!s.UnderConstruction) { waitingSince.Remove(s.Position); continue; }
					if (s.Construction == ConstructionStatus.WaitingForCleanGround)
					{
						if (!waitingSince.ContainsKey(s.Position)) waitingSince[s.Position] = t;
					}
					else waitingSince.Remove(s.Position);
				}

				// --- N: initiate from the Ready Nullifier ---
				foreach (Vector2Int n in nullifiers)
				{
					PlacedStructure ns = At(n);
					if (ns == null) continue;
					if (r.NullifierBuilt < 0 && !ns.UnderConstruction) { r.NullifierBuilt = t; Log($"Nullifier {n} built"); }
					if (r.Ready < 0 && ns.NullifierState == NullifierStatus.Ready) { r.Ready = t; Log($"Nullifier {n} Ready"); }
					if (r.Initiated < 0 && ns.NullifierState == NullifierStatus.Ready && (bool)SandboxSmokeHarness.Invoke(gm, "TryInitiateReconfiguration", n))
					{
						r.Initiated = t;
						Log("N pressed: reconfiguration initiated");
					}
				}
			}

			r.Interruptions = emitter.Interruptions;
			r.Limit = Limit(r, strategy);
			Log($"end: {(r.Won ? "WON" : "not won")}; S4 MISSION FAILED at {r.DefeatAt}; limit: {r.Limit}");
			Log("overlay at the end:\n" + gm.SyncDebugOverlayText);

			double sum = 0d;
			foreach (Cell c in grid) sum += c.Desync;
			r.Fingerprint = $"{sum:R}|{r.FootholdBuilt}|{r.ChainBuilt}|{r.NullifierPlaced}|{r.NullifierBuilt}|{r.Ready}|{r.Initiated}|{r.Reconfigured}|{r.DefeatAt}|{r.ToNullifier}";
			return r;

			void Step()
			{
				SandboxSmokeHarness.Tick(gm);
				t++;
				SyncDiagnostics d = gm.SyncDiagnostics;
				r.Generated += d.Generated;
				r.ToConstruction += d.ConstructionDelivered;
				r.ToSupply += d.SupplyDelivered;
				r.ToNullifier += d.NullifierDelivered;
				r.Unused += d.Unused;
				if (d.StabilizersReduced + d.StabilizersStarved > 0) r.SupplyShortTicks++;
				if (d.NullifierDemand > d.NullifierDelivered) r.NullifierShortTicks++;
				foreach (PlacedStructure s in structures)
				{
					if (!s.UnderConstruction || s.Construction != ConstructionStatus.WaitingForCleanGround) continue;
					if (s.Type == StructureType.Nullifier) r.NullifierCleanGroundWaitTicks++;
					else r.StabilizerCleanGroundWaitTicks++;
				}

				int built = 0;
				foreach (Vector2Int c in chain) if (At(c) != null && !At(c).UnderConstruction) built++;
				if (built > r.ChainBuilt) { r.ChainBuilt = built; r.Log.AppendLine($"  t{t}: chain Stabilizer {built} built"); }

				for (int i = 0; i < 3; i++)
					if (r.StageDone[i] < 0 && emitters[0].CompletedStages > i) { r.StageDone[i] = t; r.Log.AppendLine($"  t{t}: stage {i + 1} ({NullifierReconfiguration.CurrentStageName(i)}) complete; Reality Integrity {gm.RealityIntegrity:F1} %"); }

				if (emitters[0].IsReconfigured && r.Reconfigured < 0) { r.Reconfigured = t; r.IntegrityAtReconfigured = gm.RealityIntegrity; }

				int cleanInRange = CleanCellsInNullifierRange(out float minDesyncInRange);
				if (cleanInRange > 0) r.NullifierRangeLastCleanAt = t;

				r.MinIntegrity = Mathf.Min(r.MinIntegrity, gm.RealityIntegrity);
				if (r.DefeatAt < 0 && gm.MissionDefeated) { r.DefeatAt = t; r.Log.AppendLine($"  t{t}: S4 MISSION FAILED (Reality Integrity {gm.RealityIntegrity:F1} %)"); }

				if (t % TraceEvery == 0 || (t <= 200 && t % 20 == 0))
					r.Log.AppendLine($"  t{t}: integrity {gm.RealityIntegrity:F1} %, contamination edge on the approach {EdgeDistance(approach)} cells from the Emitter, " +
						$"clean cells within Nullifier range {cleanInRange} (lowest Desync there {minDesyncInRange:F1}), " +
						$"charges gen {d.Generated} foothold {d.FootholdReserved} share {d.ConstructionReserved} build {d.ConstructionDelivered} supply {d.SupplyDelivered} nullifier {d.NullifierDelivered} unused {d.Unused}; " +
						$"Stabilizers supplied {d.StabilizersSupplied} on-store {d.StabilizersOnStore} reduced {d.StabilizersReduced} starved {d.StabilizersStarved}; waiting for clean ground {d.WaitingForCleanGround}");
			}
		}

		// Furthest contaminated cell from the Emitter along the approach axis, walking out from the Emitter
		// (how far the visible contamination reaches toward the Sync Core).
		private int EdgeDistance(Vector2Int approach)
		{
			int edge = 0;
			for (int k = 1; k <= RimDistance + 5; k++)
			{
				Vector2Int p = EmitterPos - approach * k;
				if (p.x < 0 || p.y < 0 || p.x >= grid.GetLength(0) || p.y >= grid.GetLength(1)) break;
				if (!Clean(p)) edge = k;
			}
			return edge;
		}

		// Is any cell within `margin` cells (square) of p contaminated?
		private bool ContaminationWithin(Vector2Int p, int margin)
		{
			for (int dx = -margin; dx <= margin; dx++)
			for (int dy = -margin; dy <= margin; dy++)
			{
				int x = p.x + dx, y = p.y + dy;
				if (x < 0 || y < 0 || x >= grid.GetLength(0) || y >= grid.GetLength(1)) continue;
				if (grid[x, y].Desync > threshold) return true;
			}
			return false;
		}

		// Clean cells (the game's threshold) within Nullifier range of the Emitter, and the lowest Desync among them all.
		private int CleanCellsInNullifierRange(out float minDesync)
		{
			int r = Mathf.CeilToInt(nullifierRange), clean = 0;
			float nullSq = nullifierRange * nullifierRange;
			minDesync = float.MaxValue;
			for (int dx = -r; dx <= r; dx++)
			for (int dy = -r; dy <= r; dy++)
			{
				Vector2Int p = EmitterPos + new Vector2Int(dx, dy);
				if (p == EmitterPos || Sq(p, EmitterPos) > nullSq) continue;
				float desync = grid[p.x, p.y].Desync;
				minDesync = Mathf.Min(minDesync, desync);
				if (desync <= threshold) clean++;
			}
			return clean;
		}

		// Defense-first: the front is fully supplied, its store is full and its footprint shows no contamination.
		private bool IsConsolidated(PlacedStructure front)
		{
			if (front.Type != StructureType.Stabilizer) return true; // the Sync Core itself
			return front.SupplyEffectiveness >= 1f && front.StoredCharges >= settings.stabilizerStoreCapacity &&
				front.Node != null && front.Node.ContaminatedFootprintCells == 0;
		}

		// Next chain cell: clean, free, within connection range of the front, no more than 2 cells off the approach
		// axis, at least EdgeMargin cells from visible contamination, and at least 3 cells nearer the Emitter. The nearest to the Emitter wins (ties: off-axis less, then
		// lower Desync).
		private Vector2Int? BestChainCell(Vector2Int front, Vector2Int approach, Vector2Int lateral, float connSq)
		{
			int frontDist = Sq(front, EmitterPos);
			int reach = Mathf.FloorToInt(connectionRange);
			Vector2Int? best = null;
			int bestSq = int.MaxValue, bestOff = int.MaxValue;
			float bestDesync = float.MaxValue;

			for (int dx = -reach; dx <= reach; dx++)
			for (int dy = -reach; dy <= reach; dy++)
			{
				Vector2Int p = front + new Vector2Int(dx, dy);
				if (!Free(p) || Sq(p, front) > connSq || !Buildable(p)) continue;
				// Keep a visible margin from the contamination edge (a blueprint takes at least 20 ticks), unless the
				// S1 emergency radius covers the cell.
				if (!SyncInfrastructure.InCoreSafeZone(structures, p, settings) && ContaminationWithin(p, EdgeMargin)) continue;
				Vector2Int fromEmitter = p - EmitterPos;
				int off = Mathf.Abs(fromEmitter.x * lateral.x + fromEmitter.y * lateral.y);
				if (off > 2) continue;
				float dist = Mathf.Sqrt(Sq(p, EmitterPos));
				if (dist > Mathf.Sqrt(frontDist) - 3f) continue;
				int sq = Sq(p, EmitterPos);
				float desync = grid[p.x, p.y].Desync;
				if (sq < bestSq || (sq == bestSq && (off < bestOff || (off == bestOff && desync < bestDesync))))
				{
					best = p; bestSq = sq; bestOff = off; bestDesync = desync;
				}
			}
			return best;
		}

		// Nullifier cell: clean, free, within Nullifier range of the Emitter and connection range of a completed
		// relay. The cleanest wins (ties: further from the Emitter).
		private Vector2Int? BestNullifierCell(float connSq, float nullSq)
		{
			int r = Mathf.CeilToInt(nullifierRange);
			Vector2Int? best = null;
			float bestDesync = float.MaxValue;
			int bestSq = -1;

			for (int dx = -r; dx <= r; dx++)
			for (int dy = -r; dy <= r; dy++)
			{
				Vector2Int p = EmitterPos + new Vector2Int(dx, dy);
				if (!Free(p) || Sq(p, EmitterPos) > nullSq || !Buildable(p)) continue;
				bool reached = false;
				foreach (PlacedStructure s in structures)
					if (IsRelay(s) && Sq(s.Position, p) <= connSq) { reached = true; break; }
				if (!reached) continue;
				float desync = grid[p.x, p.y].Desync;
				int sq = Sq(p, EmitterPos);
				if (desync < bestDesync || (desync == bestDesync && sq > bestSq)) { best = p; bestDesync = desync; bestSq = sq; }
			}
			return best;
		}

		private static string Limit(Result r, Strategy s)
		{
			if (r.Won) return "none (won)";
			if (s == Strategy.Idle) return "no advance (control)";
			if (r.ReachedNullifierRangeAt < 0)
				return $"the chain never reached Nullifier range: closest completed relay {r.ClosestRelay} cells from the Emitter " +
					$"(no clean chain cell on {r.NoChainCandidateTicks} ticks; Stabilizer blueprints waited for clean ground {r.StabilizerCleanGroundWaitTicks} blueprint-ticks)";
			if (r.NullifierPlaced < 0)
				return $"no clean cell within Nullifier range reachable by a relay ({r.NoNullifierCandidateTicks} ticks checked after reaching range)";
			if (r.NullifierBuilt < 0)
				return $"the Nullifier blueprint never completed (waited for clean ground {r.NullifierCleanGroundWaitTicks} blueprint-ticks)";
			if (r.Ready < 0) return "the built Nullifier never filled its pulse store";
			if (r.Initiated < 0) return "never initiated";
			int done = r.StageDone[2] > 0 ? 3 : r.StageDone[1] > 0 ? 2 : r.StageDone[0] > 0 ? 1 : 0;
			return r.Reconfigured > 0
				? $"reconfigured at {r.Reconfigured}, after S4 at {r.DefeatAt}"
				: $"stages completed {done}/3 by the end (interruptions {r.Interruptions})";
		}

		private static string Summary(Result r)
		{
			return $"{r.Name}: {(r.Won ? "WON" : "NOT WON")} | Core t{r.CorePlaced}, foothold built t{r.FootholdBuilt}, chain Stabilizers built {r.ChainBuilt} (placed {r.StabilizersPlaced} incl. foothold), " +
				$"closest relay {(r.ClosestRelay == int.MaxValue ? -1 : r.ClosestRelay)} cells, reached Nullifier range t{r.ReachedNullifierRangeAt}, Nullifier placed t{r.NullifierPlaced} (x{r.NullifiersPlaced}) built t{r.NullifierBuilt}, " +
				$"Ready t{r.Ready}, initiated t{r.Initiated}, stages t{r.StageDone[0]}/t{r.StageDone[1]}/t{r.StageDone[2]}, reconfigured t{r.Reconfigured} at {r.IntegrityAtReconfigured:F1} %, " +
				$"S4 MISSION FAILED t{r.DefeatAt}, min integrity {r.MinIntegrity:F1} % | charges generated {r.Generated}: construction {r.ToConstruction}, supply {r.ToSupply}, Nullifier {r.ToNullifier}, unused {r.Unused}; " +
				$"supply-short ticks {r.SupplyShortTicks}, Nullifier-short ticks {r.NullifierShortTicks}, interruptions {r.Interruptions}; " +
				$"last tick with a clean cell within Nullifier range t{r.NullifierRangeLastCleanAt} | limit: {r.Limit}";
		}
	}
}
