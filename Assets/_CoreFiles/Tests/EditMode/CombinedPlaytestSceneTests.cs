using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ImminentDesync.Tests.EditMode
{
	// Combined playtest (increment 2A merged into increment 3, Director 2026-11-26). The scene, Emitter and
	// coordinates are the ones the Director's combined checklist uses, opened as saved with nothing overridden,
	// so the manual and automated runs start the same way. Values are the shipped provisional defaults; F6
	// (S4 at 30 %) is recorded, not tuned.
	public class CombinedPlaytestSceneTests
	{
		private const string ScenePath = "Assets/_CoreFiles/Scenes/Playtest_Combined.unity";
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

		// Checklist coordinates.
		internal static readonly Vector2Int EmitterPos = new(50, 50);
		internal static readonly Vector2Int CorePos = new(50, 57);
		internal static readonly Vector2Int StabA = new(48, 58);
		internal static readonly Vector2Int StabB = new(52, 58);
		internal static readonly Vector2Int NullifierPos = new(50, 56); // 6 cells from the Emitter, 1 from the Core

		private GridManager gm;
		private List<PlacedStructure> structures;
		private List<Emitter> emitters;

		private void Open()
		{
			EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			gm = Object.FindAnyObjectByType<GridManager>();
			Assert.IsNotNull(gm);
			SandboxSmokeHarness.Invoke(gm, "Start");
			structures = (List<PlacedStructure>)SandboxSmokeHarness.Field(gm, "structures");
			emitters = (List<Emitter>)SandboxSmokeHarness.Field(gm, "emitters");
		}

		private SyncInfrastructureSettings Settings => (SyncInfrastructureSettings)SandboxSmokeHarness.Field(gm, "syncInfrastructure");
		private void Tick() => SandboxSmokeHarness.Tick(gm);
		private PlacedStructure At(Vector2Int p) => structures.Find(s => s.Position == p);

		private void Place(Vector2Int p, BuildTool tool) =>
			typeof(GridManager).GetMethod("PlaceStructure", Any, null, new[] { typeof(int), typeof(int), typeof(BuildTool) }, null)
				.Invoke(gm, new object[] { p.x, p.y, tool });

		private bool Initiate(Vector2Int near) => (bool)SandboxSmokeHarness.Invoke(gm, "TryInitiateReconfiguration", near);

		[Test]
		public void CombinedScene_HasEverySystemOn_AtTheShippedDefaults_AndStartsWithOneEmitter()
		{
			Open();
			SyncInfrastructureSettings s = Settings;
			Assert.IsTrue(s.enabled);
			Assert.IsTrue(s.stabilizerSupplyEnabled);
			Assert.IsTrue(s.nullifierReconfigurationEnabled, "Nullifier reconfiguration (increment 3) on.");
			Assert.IsTrue(s.ReconfigurationActive);
			Assert.AreEqual(8, s.coreGenerationPerTick);
			Assert.AreEqual(0.25f, s.constructionMinimumShare);
			Assert.AreEqual(30, s.nullifierPulseCost);
			Assert.AreEqual(150, s.reconfigurationStageTicks);
			Assert.IsTrue((bool)SandboxSmokeHarness.Field(gm, "realityIntegrityDefeat"));
			Assert.AreEqual(30f, (float)SandboxSmokeHarness.Field(gm, "defeatIntegrityThreshold"), "F6 unresolved: threshold unchanged.");
			Assert.AreEqual(1, emitters.Count);
			Assert.AreEqual(EmitterPos, emitters[0].Position);
			Assert.IsFalse(gm.SyncDebugOverlayVisible);
		}

		// The combined checklist, scripted with the same coordinates. Prints the tick of each event.
		[Test, Timeout(600000)]
		public void CombinedChecklist_ScriptedRun_ReconfiguresTheEmitter_BeforeDefeat()
		{
			Open();
			Result r = Play(startTick: 0);
			TestContext.WriteLine(r.Log.ToString());

			Assert.Greater(r.FootholdBuilt, 0, "Both Stabilizers complete.");
			Assert.Greater(r.NullifierBuilt, 0, "The Nullifier blueprint completes alongside the foothold's upkeep.");
			Assert.Greater(r.Ready, 0, "The Nullifier becomes Ready.");
			Assert.IsTrue(r.Initiated, "N initiates from the Ready Nullifier.");
			Assert.Greater(r.Reconfigured, 0, "Three stages complete.");
			Assert.AreEqual(450, r.Reconfigured - r.InitiatedAt, "N2/N3: 3 stages x 150 ticks, unchanged by the 2A merge.");
			Assert.IsTrue(gm.PrimaryObjectiveComplete);
			Assert.Less(r.Reconfigured, r.DefeatAt < 0 ? int.MaxValue : r.DefeatAt, "Reconfigured before S4 would fail the mission.");
			Assert.AreEqual(1f, At(StabA).SupplyEffectiveness, "Foothold at full strength throughout.");
			Assert.AreEqual(1f, At(StabB).SupplyEffectiveness);
		}

		// F6 diagnostic (no tuning): the same build started later. Records whether the Emitter is reconfigured before
		// S4 at 30 % would end the mission (no player action: about tick 1389 in the 2A scene).
		[Test, Timeout(900000)]
		public void F6_LateStart_Diagnostic()
		{
			var sb = new StringBuilder();
			foreach (int start in new[] { 300, 600, 900 })
			{
				Open();
				Result r = Play(startTick: start);
				sb.AppendLine($"start at tick {start}: Ready {r.Ready}, initiated {r.InitiatedAt}, reconfigured {r.Reconfigured}, " +
					$"S4 MISSION FAILED {r.DefeatAt} -> {(r.Reconfigured > 0 && (r.DefeatAt < 0 || r.Reconfigured < r.DefeatAt) ? "WON" : "LOST")}; integrity at reconfiguration {r.IntegrityAtReconfigured:F1} %");
			}
			TestContext.WriteLine(sb.ToString());
		}

		[Test, Timeout(600000)]
		public void CombinedChecklist_IsDeterministic()
		{
			Open();
			string a = Play(0).Fingerprint;
			Open();
			string b = Play(0).Fingerprint;
			Assert.AreEqual(a, b);
		}

		private class Result
		{
			public StringBuilder Log = new();
			public int FootholdBuilt = -1, NullifierBuilt = -1, Ready = -1, InitiatedAt = -1, Reconfigured = -1, DefeatAt = -1;
			public bool Initiated;
			public float IntegrityAtReconfigured;
			public string Fingerprint;
		}

		// Waits until startTick, places the checklist build, initiates as soon as the Nullifier is Ready, and runs until
		// reconfigured (or tick 3000). The harness keeps ticking after a defeat, so the defeat tick is recorded.
		private Result Play(int startTick)
		{
			var r = new Result();
			int t = 0;
			void Step()
			{
				Tick(); t++;
				if (r.DefeatAt < 0 && gm.MissionDefeated) r.DefeatAt = t;
			}

			while (t < startTick) Step();
			Place(CorePos, BuildTool.Core);
			Place(StabA, BuildTool.Stabilizer);
			Place(StabB, BuildTool.Stabilizer);
			Place(NullifierPos, BuildTool.Nullifier);
			r.Log.AppendLine($"t{t}: Core {CorePos}, Stabilizers {StabA} {StabB}, Nullifier {NullifierPos} placed");

			while (t < startTick + 3000 && r.Reconfigured < 0)
			{
				Step();
				PlacedStructure n = At(NullifierPos);
				if (r.FootholdBuilt < 0 && !At(StabA).UnderConstruction && !At(StabB).UnderConstruction) { r.FootholdBuilt = t; r.Log.AppendLine($"t{t}: both Stabilizers built"); }
				if (r.NullifierBuilt < 0 && !n.UnderConstruction) { r.NullifierBuilt = t; r.Log.AppendLine($"t{t}: Nullifier built"); }
				if (r.Ready < 0 && n.NullifierState == NullifierStatus.Ready) { r.Ready = t; r.Log.AppendLine($"t{t}: Nullifier Ready"); }
				if (!r.Initiated && r.Ready > 0)
				{
					r.Initiated = Initiate(NullifierPos);
					if (r.Initiated) { r.InitiatedAt = t; r.Log.AppendLine($"t{t}: N pressed (initiated)"); }
				}
				if (emitters[0].IsReconfigured)
				{
					r.Reconfigured = t;
					r.IntegrityAtReconfigured = gm.RealityIntegrity;
					r.Log.AppendLine($"t{t}: Emitter reconfigured; Reality Integrity {gm.RealityIntegrity:F1} %; overlay:\n{gm.SyncDebugOverlayText}");
				}
			}

			r.Log.AppendLine($"S4 MISSION FAILED at tick {r.DefeatAt} (-1 = never)");
			double sum = 0d;
			foreach (Cell c in (Cell[,])SandboxSmokeHarness.Field(gm, "grid")) sum += c.Desync;
			r.Fingerprint = $"{sum:R}|{r.FootholdBuilt}|{r.NullifierBuilt}|{r.Ready}|{r.InitiatedAt}|{r.Reconfigured}|{r.DefeatAt}";
			return r;
		}
	}
}
