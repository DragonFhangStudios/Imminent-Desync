using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Nullifier reconfiguration N1–N3 with N1-A (increment 3) inside the real GridManager and the real Desync
	// simulation. Each test opens Sandbox.unity and turns the opt-in systems on IN MEMORY (never saved).
	// Unless a test says otherwise it uses the SHIPPED PROVISIONAL defaults (decision 2 is open): only the
	// opt-in switches are set, nothing is tuned to pass.
	//
	// Layout: an Emitter placed at the grid centre (50,50); Sync Core at (50,57); Nullifiers within
	// the Core's S1 emergency radius (3 cells), so they can be built although the ground near the Emitter is
	// contaminated, and within Nullifier range (6) of the Emitter.
	public class NullifierReconfigurationSceneTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

		private static readonly Vector2Int EmitterPos = new(50, 50);
		private static readonly Vector2Int CorePos = new(50, 57);
		private static readonly Vector2Int N1 = new(50, 56);
		private static readonly Vector2Int N2 = new(49, 55);
		private static readonly Vector2Int N3 = new(51, 55);

		private GridManager gm;
		private Cell[,] grid;
		private List<PlacedStructure> structures;
		private List<Emitter> emitters;
		private SyncInfrastructureSettings settings;

		private void OpenSandbox(bool reconfiguration = true, bool supply = false, int? generation = null)
		{
			EditorSceneManager.OpenScene("Assets/_CoreFiles/Scenes/Sandbox.unity", OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm);

			settings = (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");
			settings.enabled = true;
			settings.stabilizerSupplyEnabled = supply;
			settings.nullifierReconfigurationEnabled = reconfiguration;
			if (generation.HasValue)
				settings.coreGenerationPerTick = generation.Value;

			SandboxSmokeHarness.Invoke(gm, "Start");
			grid = (Cell[,])SandboxSmokeHarness.Field(gm, "grid");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
			emitters = (List<Emitter>)SandboxSmokeHarness.Field(gm, "emitters");

			// The Sandbox starts without an Emitter; place one as the sandbox control does (Shift + Left Click).
			Assert.AreEqual(0, emitters.Count);
			Assert.IsTrue((bool)SandboxSmokeHarness.Invoke(gm, "TryAddEmitter", EmitterPos));
			Assert.AreEqual(EmitterPos, emitters[0].Position);
		}

		private void Place(Vector2Int p, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { p.x, p.y, tool });

		// The player's own removal (Shift + Middle Click); the marker is detached because Destroy() is not
		// allowed in edit mode.
		private void Remove(Vector2Int p)
		{
			At(p).Marker = null;
			SandboxSmokeHarness.Invoke(gm, "RemoveNearestStructure", p.x, p.y);
			Assert.IsNull(At(p));
		}

		// The command the N key sends, aimed at a cell.
		private bool Initiate(Vector2Int near) => (bool)SandboxSmokeHarness.Invoke(gm, "TryInitiateReconfiguration", near);

		private void Tick(int count = 1)
		{
			for (int i = 0; i < count; i++)
				SandboxSmokeHarness.Tick(gm);
		}

		private PlacedStructure At(Vector2Int p) => structures.Find(s => s.Position == p);

		private int TickUntil(System.Func<bool> condition, int max)
		{
			for (int t = 1; t <= max; t++)
			{
				Tick();
				if (condition()) return t;
			}
			return -1;
		}

		private double GridDesyncSum()
		{
			double sum = 0;
			foreach (Cell c in grid) sum += c.Desync;
			return sum;
		}

		private static int StageTicks => new SyncInfrastructureSettings().reconfigurationStageTicks;

		// ---------------------------------------------------------------

		[Test]
		public void Sandbox_KeepsTheIntegrityDrain_ReconfigurationIsOffInTheScene()
		{
			EditorSceneManager.OpenScene("Assets/_CoreFiles/Scenes/Sandbox.unity", OpenSceneMode.Single);
			var sceneGm = Object.FindAnyObjectByType<GridManager>();
			var sceneSettings = (SyncInfrastructureSettings)SandboxSmokeHarness.Field(sceneGm, "syncInfrastructure");
			Assert.IsFalse(sceneSettings.enabled);
			Assert.IsFalse(sceneSettings.nullifierReconfigurationEnabled);
			Assert.IsFalse(sceneSettings.ReconfigurationActive);
		}

		// Full sequence at the shipped provisional defaults with U2 supply off (generation 4).
		private struct SequenceResult
		{
			public int BuildTicks;
			public int ReadyTicks;
			public int ReconfigureTicks;
			public int[] StageCompletedAt;
			public double Fingerprint;
		}

		private SequenceResult RunFullSequence()
		{
			OpenSandbox();
			Emitter emitter = emitters[0];
			Place(CorePos, BuildTool.Core);
			Place(N1, BuildTool.Nullifier);
			PlacedStructure n = At(N1);
			Assert.IsTrue(n.UnderConstruction, "a Nullifier is a blueprint with Sync Infrastructure on");
			Assert.IsTrue(grid[N1.x, N1.y].Desync >= 0f);

			var r = new SequenceResult { StageCompletedAt = new int[3] };
			r.BuildTicks = TickUntil(() => !n.UnderConstruction, 1000);
			Assert.Greater(r.BuildTicks, 0, "the Nullifier blueprint completes (S1 radius)");
			r.ReadyTicks = TickUntil(() => n.NullifierState == NullifierStatus.Ready, 1000);
			Assert.Greater(r.ReadyTicks, 0, "the Nullifier becomes Ready with Sync Charges");

			// N1: no progress without the player's initiation.
			Tick(200);
			Assert.AreEqual(NullifierStatus.Ready, n.NullifierState);
			Assert.IsFalse(emitter.ReconfigurationStarted);
			Assert.AreEqual(emitter.MaxIntegrity, emitter.Integrity);
			Assert.AreEqual(250f, grid[EmitterPos.x, EmitterPos.y].Desync, "the Emitter still produces");

			Assert.IsTrue(Initiate(new Vector2Int(60, 60)), "nearest Ready Nullifier to the cursor");
			Assert.IsFalse(Initiate(N1), "once per sequence");

			int stagesSeen = 0;
			for (int t = 1; t <= 5000 && !emitter.IsReconfigured; t++)
			{
				Tick();
				if (emitter.CompletedStages > stagesSeen)
				{
					r.StageCompletedAt[stagesSeen] = t;
					stagesSeen = emitter.CompletedStages;
				}
				if (emitter.IsReconfigured) r.ReconfigureTicks = t;
			}

			Assert.IsTrue(emitter.IsReconfigured);
			Assert.IsTrue(emitter.IsNullified);
			Assert.IsTrue(gm.PrimaryObjectiveComplete, "the existing objective flag fires");
			Tick();
			Assert.AreEqual(0f, gm.SourceOutput, "production stopped");
			r.Fingerprint = GridDesyncSum();
			return r;
		}

		[Test]
		[Timeout(600000)]
		public void ShippedDefaults_FullSequence_ReadinessManualInitiationAndStagedPulses()
		{
			SequenceResult r = RunFullSequence();
			TestContext.WriteLine($"build {r.BuildTicks}, ready +{r.ReadyTicks}, stages at {string.Join("/", r.StageCompletedAt)}, reconfigured at {r.ReconfigureTicks} ticks after initiation");
			Assert.AreEqual(30, r.BuildTicks, "60 charges at 2 per tick");
			Assert.AreEqual(15, r.ReadyTicks, "30-charge store at 2 per tick");
			Assert.AreEqual(new[] { StageTicks, 2 * StageTicks, 3 * StageTicks }, r.StageCompletedAt);
			Assert.AreEqual(3 * StageTicks, r.ReconfigureTicks);
		}

		[Test]
		[Timeout(600000)]
		public void FullSequence_IsDeterministic()
		{
			SequenceResult a = RunFullSequence();
			SequenceResult b = RunFullSequence();
			Assert.AreEqual(a.ReconfigureTicks, b.ReconfigureTicks);
			Assert.AreEqual(a.Fingerprint, b.Fingerprint, $"A={a.Fingerprint:R} B={b.Fingerprint:R}");
		}

		[Test]
		[Timeout(600000)]
		public void SeveredNetworkMidStage_RestartsOnlyThatStage_CompletedStagesKept()
		{
			OpenSandbox();
			Emitter emitter = emitters[0];
			Place(CorePos, BuildTool.Core);
			Place(N1, BuildTool.Nullifier);
			PlacedStructure n = At(N1);
			Assert.Greater(TickUntil(() => n.NullifierState == NullifierStatus.Ready, 1000), 0);
			Assert.IsTrue(Initiate(N1));

			Tick(StageTicks + 100);
			Assert.AreEqual(1, emitter.CompletedStages);
			Assert.AreEqual(100, emitter.StageProgress);

			Remove(CorePos); // the player removes the Sync Core: the whole network goes down
			Tick();
			Assert.AreEqual(NullifierStatus.Disconnected, n.NullifierState);
			Assert.AreEqual(1, emitter.CompletedStages);
			Assert.AreEqual(0, emitter.StageProgress);
			Assert.AreEqual(1, emitter.Interruptions);
			Tick(100);
			Assert.AreEqual(1, emitter.CompletedStages);

			Place(CorePos, BuildTool.Core);
			Assert.IsTrue(n.IsConnected);
			int remaining = TickUntil(() => emitter.IsReconfigured, 5000);
			Assert.AreEqual(2 * StageTicks, remaining, "stage 2 again in full, then stage 3; no re-initiation");
			Assert.IsTrue(gm.PrimaryObjectiveComplete);
		}

		[Test]
		[Timeout(600000)]
		public void ThreeNullifiers_CompleteAtTheSameTickAsOne()
		{
			int Run(params Vector2Int[] spots)
			{
				OpenSandbox();
				Emitter emitter = emitters[0];
				Place(CorePos, BuildTool.Core);
				foreach (var p in spots)
					Place(p, BuildTool.Nullifier);
				Assert.Greater(TickUntil(() => spots.Length == structures.FindAll(s => s.NullifierState == NullifierStatus.Ready).Count, 2000), 0);
				Assert.IsTrue(Initiate(spots[0]));
				return TickUntil(() => emitter.IsReconfigured, 5000);
			}

			int one = Run(N1);
			int three = Run(N1, N2, N3);
			Assert.AreEqual(3 * StageTicks, one);
			Assert.AreEqual(one, three);
		}

		[Test]
		[Timeout(600000)]
		public void U2SupplyOn_TestFixtureGeneration8_SequenceCompletes()
		{
			OpenSandbox(supply: true, generation: 8);
			Emitter emitter = emitters[0];
			Place(CorePos, BuildTool.Core);
			Place(new Vector2Int(48, 58), BuildTool.Stabilizer);
			Place(new Vector2Int(52, 58), BuildTool.Stabilizer);
			Assert.Greater(TickUntil(() => structures.TrueForAll(s => !s.UnderConstruction), 1000), 0);
			Place(N1, BuildTool.Nullifier);
			PlacedStructure n = At(N1);
			int ready = TickUntil(() => n.NullifierState == NullifierStatus.Ready, 2000);
			Assert.Greater(ready, 0, "built and prepared after the foothold's upkeep");
			Assert.IsTrue(Initiate(N1));
			int done = TickUntil(() => emitter.IsReconfigured, 5000);
			TestContext.WriteLine($"U2 on, generation 8: Nullifier Ready {ready} ticks after placement, reconfigured {done} ticks after initiation");
			Assert.AreEqual(3 * StageTicks, done);
			Assert.IsTrue(gm.PrimaryObjectiveComplete);
		}

		// Finding F3, U2 supply on at the shipped defaults. Recorded 2026-11-25 (increment 3 alone, generation 4):
		// the two-Stabilizer foothold's reserved upkeep took the whole generation, so a Nullifier blueprint placed
		// after the foothold received 0 of 60 charges in 2,000 ticks. Since increment 2A was merged in (2026-11-26:
		// generation 8 and a 25 % construction share after the foothold's upkeep), construction must never fully
		// starve, so the Nullifier must now be built. Records the build time; supply tuning (decision 5) stays open.
		[Test]
		[Timeout(600000)]
		public void Diagnostic_U2SupplyOn_ShippedDefaults_NullifierAfterFoothold()
		{
			OpenSandbox(supply: true);
			Place(CorePos, BuildTool.Core);
			Place(new Vector2Int(48, 58), BuildTool.Stabilizer);
			Place(new Vector2Int(52, 58), BuildTool.Stabilizer);
			int foothold = TickUntil(() => structures.TrueForAll(s => !s.UnderConstruction), 1000);
			Assert.Greater(foothold, 0, "setup: the foothold completes");
			Place(N1, BuildTool.Nullifier);
			PlacedStructure n = At(N1);

			const int Window = 2000;
			int built = TickUntil(() => !n.UnderConstruction, Window);
			string line =
				$"F3 diagnostic (U2 on, generation {settings.coreGenerationPerTick}, foothold {settings.footholdStabilizerCount} x upkeep {settings.stabilizerUpkeepPerTick}): " +
				(built > 0
					? $"Nullifier built after {built} ticks"
					: $"Nullifier still a blueprint after {Window} ticks, {n.ChargesDelivered}/{n.ChargesRequired} charges ({n.Construction}); generated {SyncDiagnosticsOf().Generated}/tick, distributed {SyncDiagnosticsOf().Distributed}");
			TestContext.WriteLine(line);
			Debug.Log(line);
			Assert.Greater(built, 0, "F3 (fixed in increment 2A): the Nullifier blueprint is built after the foothold stands.");
		}

		private SyncDiagnostics SyncDiagnosticsOf() => gm.SyncDiagnostics;
	}
}
